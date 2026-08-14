using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace CitiesSkylines2Agent.Agent
{
    public enum AgentStatus
    {
        Idle,
        Thinking,
        Working,
        Interrupted,
        Error,
    }

    /// <summary>UI-facing event emitted by the agent loop.</summary>
    public sealed class AgentUiEvent
    {
        public string Kind;      // status|delta|tool|user|error|compact|turn|progress
        public string Text;
        public string Tool;
        public AgentStatus Status;

        public string ToJsonString()
        {
            var obj = new JsonObject
            {
                ["kind"] = Kind ?? "",
                ["text"] = Text ?? "",
                ["status"] = Status.ToString(),
            };
            if (Tool != null)
            {
                obj["tool"] = Tool;
            }
            return obj.ToJsonString();
        }
    }

    internal sealed class AgentInput
    {
        public string Text;
    }

    /// <summary>
    /// In-process agent runtime: IChatClient + hand-rolled function-calling
    /// loop. Apeira-style interleaving: user messages queued during a turn are
    /// drained after the current model+tool round and continue the same turn.
    /// </summary>
    public sealed class AgentLoop : IDisposable
    {
        private const string SystemPrompt = @"You are the in-game AI mayor for Cities: Skylines 2.

Working style:
1. Observe briefly first (game_state / city_overview / demand / city_services / notifications), then act. Do not repeat the same read tool more than twice without a write.
2. Fix problems that block city growth FIRST: sewage, water, electricity, garbage, road access. Do not zone or expand while a red problem is unresolved.
3. For infrastructure or service buildings without a player-selected prefab, use find_prefabs with a typed role, choose one unlocked standalone prefab, then call place_building once. For every site you choose yourself, include a reasonable radius and omit rotation so placement can resolve clearance, frontage and orientation. Omit radius or set rotation only when the player or a context block explicitly requires that exact pose. If exact placement fails, retry with a larger radius and no rotation.
4. Use zone_area for regular residential / commercial / industrial / office growth. Use place_building only for standalone buildings (service buildings, unique/landmark/signature buildings, special production or extraction facilities).
5. place_building owns nearby search and native validation in one call. Placement follows prefab data: only RequireRoad buildings need road frontage, shoreline buildings snap to the wet/dry boundary, and off-road utility nodes receive the required pipe/cable connection.
6. build_road: use short segments (50-250m) on owned tiles near existing nodes. For roads, omit mode and e1/e2 for the default ground mode; it samples the route at roughly 4m or finer intervals for water and local grade, rejecting detected water crossings or grades above 10% (or a stricter prefab limit). Use mode=grade-separated only for an intentional bridge/elevated/tunnel segment; provide both e1/e2 with at least one nonzero. Never pass mode for pipes, cables or other utility networks; their normal burial behavior is separate. If a call fails, change the route instead of repeating the same call.
7. The simulation clock belongs to the player. Use wait_simulation to advance in-game time: one call advances exactly 1 in-game hour by default (high speed, roughly 20-30 real seconds), then restores the previous speed/pause state. Buildings take game hours to construct, level up and attract residents, so after zoning/placing call wait_simulation once or twice; never poll game_state in a loop.
8. Before demolition, identify the exact target with list_buildings or list_roads. If the demolition tool is available, the player has already granted permission; do not ask for a modal confirmation.
9. Ask for a player decision only when the desired outcome itself is ambiguous, not for permissions already represented by the available tool surface.
10. End every turn with a concise summary (what was done, results, next steps).

Skills: call agent_read_skill(""city-building"") for the full playbook.

Context blocks (map pins / selected networks) arrive as system messages and are the player's precise positions or targets; prefer them over guessing.";

        private const string CompactionTaskPrompt = @"COMPACTION TASK:
Ignore the normal assistant response format for this response.
Do not call tools. Do not emit tool_calls, DSML, XML tags, or function-call markup.
Return strict JSON with:
{
  ""time_anchor"": string,
  ""session_state"": string,
  ""active_goal"": string,
  ""active_commitments"": string[],
  ""durable_facts"": string[],
  ""relevant_people"": string[],
  ""open_loops"": string[],
  ""recent_timeline"": string[],
  ""forgettable_noise"": string[],
  ""current_plan"": string,
  ""context_blocks"": string[],
  ""paused_state"": string,
  ""last_world_snapshot"": string
}
Preserve user constraints, operator instructions, active goals, open loops,
important names, and current world/session state (city money, population,
demand, notifications). Prefer compressing assistant chatter, tool chatter,
and stale notices. Do not keep stale relative-time phrases; convert them into
stable facts or timeline notes. Keep each list item short and concrete.";

        private const string SummaryPrefix = "[context summary] ";
        private const int MaxToolRoundsPerTurn = 30;
        private const int ModelTimeoutMs = 120_000;

        public static AgentLoop Instance { get; private set; }

        /// <summary>Idempotent factory so Mod.OnLoad and the UI system agree on one instance.</summary>
        public static AgentLoop EnsureCreated()
        {
            if (Instance == null)
            {
                new AgentLoop(); // constructor sets Instance
            }
            return Instance;
        }

        /// <summary>
        /// Starts a clean session for a newly loaded city. The previous loop is
        /// cancelled and disposed so pending work, history and enabled tool
        /// groups cannot leak across saves.
        /// </summary>
        public static AgentLoop StartCitySession()
        {
            AgentLoop previous = Instance;
            var current = new AgentLoop();
            previous?.Dispose();
            return current;
        }

        private readonly Channel<AgentInput> m_Pending = Channel.CreateUnbounded<AgentInput>();
        private readonly List<ChatMessage> m_History = new List<ChatMessage>();
        private readonly object m_Lock = new object();
        private readonly AgentObservability m_Observability;
        private readonly AgentToolSurface m_ToolSurface = new AgentToolSurface();
        private readonly AgentPromptAssembler m_PromptAssembler;
        private readonly AgentToolExecutor m_ToolExecutor;

        private readonly AgentClientFactory m_ClientFactory;
        private Task m_LoopTask;
        private CancellationTokenSource m_TurnCts;
        private CancellationTokenSource m_LoopCts = new CancellationTokenSource();
        private string m_SessionId;
        private string m_TurnId;
        private long m_EstimatedTokens;
        private int m_TurnGenerationCount;
        private int m_SuppressAutoContinue;
        private bool m_TimeoutOccurred;
        private bool m_Disposed;

        public AgentLoop()
        {
            Instance = this;
            m_SessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
            m_Observability = new AgentObservability(m_SessionId);
            m_ClientFactory = new AgentClientFactory(m_Observability);
            m_PromptAssembler = new AgentPromptAssembler(SystemPrompt, SummaryPrefix);
            m_ToolExecutor = new AgentToolExecutor(
                m_ToolSurface,
                m_ClientFactory,
                m_Observability,
                Emit,
                AppendHistoryMessage);
        }

        public event Action<AgentUiEvent> UiEvent;

        public AgentObservability Observability => m_Observability;

        public AgentStatus Status { get; private set; } = AgentStatus.Idle;

        public bool IsBusy => Status == AgentStatus.Thinking || Status == AgentStatus.Working;

        /// <summary>Queue a user message; injected after the current tool round.</summary>
        public void Send(string text)
        {
            Interlocked.Exchange(ref m_SuppressAutoContinue, 0);
            m_Pending.Writer.TryWrite(new AgentInput { Text = text ?? "" });
            m_Observability.InterleavedQueued(text ?? "");
            Emit(new AgentUiEvent { Kind = "user", Text = text ?? "" });
            EnsureLoop();
        }

        public void Interrupt()
        {
            Interlocked.Exchange(ref m_SuppressAutoContinue, 1);
            m_TurnCts?.Cancel();
            Status = AgentStatus.Interrupted;
            Emit(new AgentUiEvent { Kind = "status", Status = AgentStatus.Interrupted, Text = "已中断当前回合" });
        }

        public void RefreshConfig()
        {
            m_ClientFactory.Refresh();
        }

        public string RenderChatStateJson()
        {
            lock (m_Lock)
            {
                // Tool results only carry CallId; resolve names from prior calls.
                var callNames = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (ChatMessage message in m_History)
                {
                    foreach (AIContent content in message.Contents)
                    {
                        if (content is FunctionCallContent call && !string.IsNullOrEmpty(call.CallId))
                        {
                            callNames[call.CallId] = call.Name ?? call.CallId;
                        }
                    }
                }

                var messages = new JsonArray();
                foreach (ChatMessage message in m_History)
                {
                    // System prompt / compaction / context blocks stay in the
                    // model history only — do not dump them into the chat UI.
                    if (message.Role == ChatRole.System)
                    {
                        continue;
                    }
                    if (message.Role == ChatRole.User &&
                        message.Contents.Any(content => content is DataContent))
                    {
                        continue;
                    }
                    string role = message.Role == ChatRole.Assistant ? "assistant"
                        : message.Role == ChatRole.Tool ? "tool"
                        : "user";
                    string text = message.Text ?? "";
                    string tool = null;

                    if (role == "tool")
                    {
                        foreach (AIContent content in message.Contents)
                        {
                            if (content is FunctionResultContent result)
                            {
                                if (!string.IsNullOrEmpty(result.CallId) &&
                                    callNames.TryGetValue(result.CallId, out string name))
                                {
                                    tool = name;
                                }
                                else
                                {
                                    tool = result.CallId ?? "tool";
                                }
                                text = result.Result?.ToString() ?? text;
                                break;
                            }
                        }
                        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrEmpty(tool))
                        {
                            continue;
                        }
                        text = TruncateForLog(text ?? "", 800);
                    }
                    else if (role == "assistant")
                    {
                        // Pure function-call turns are shown via the following tool rows.
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }
                        tool = ToolCallNames(message);
                    }

                    var entry = new JsonObject
                    {
                        ["role"] = role,
                        ["text"] = text,
                        ["tool"] = tool,
                    };
                    messages.Add(entry);
                }
                AgentModelProfile profile = m_ClientFactory.GetProfile();
                return new JsonObject
                {
                    ["status"] = Status.ToString(),
                    ["busy"] = IsBusy,
                    ["pendingInputs"] = m_Pending.Reader.Count,
                    ["session"] = m_SessionId,
                    ["turn"] = m_TurnId,
                    ["context"] = new JsonObject
                    {
                        ["windowTokens"] = profile.ContextWindowTokens,
                        ["estimatedTokens"] = m_EstimatedTokens,
                        ["compactAtTokens"] = profile.CompactAtTokens,
                        ["source"] = profile.Source,
                        ["vision"] = profile.VisionAvailable,
                    },
                    ["contextBlocks"] = JsonNode.Parse(ContextBlockStore.ToJsonString()),
                    ["messages"] = messages,
                }.ToJsonString();
            }
        }

        private void EnsureLoop()
        {
            if (m_LoopTask == null || m_LoopTask.IsCompleted)
            {
                m_LoopTask = RunLoopAsync();
            }
        }

        private async Task RunLoopAsync()
        {
            AgentModelProfile profile = m_ClientFactory.GetProfile();
            m_Observability.TaskStart(
                Setting.StaticModel,
                profile.ContextWindowTokens,
                (double)profile.CompactAtTokens / profile.ContextWindowTokens);
            while (!m_LoopCts.IsCancellationRequested)
            {
                AgentInput first;
                try
                {
                    first = await m_Pending.Reader.ReadAsync(m_LoopCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var inputs = new List<AgentInput> { first };
                while (m_Pending.Reader.TryRead(out AgentInput extra))
                {
                    inputs.Add(extra);
                }

                m_TurnId = Guid.NewGuid().ToString("N").Substring(0, 8);
                m_TurnCts = new CancellationTokenSource();
                m_TurnGenerationCount = 0;
                m_TimeoutOccurred = false;
                m_ToolSurface.Reset();
                m_ToolExecutor.Reset();
                Stopwatch turnTimer = Stopwatch.StartNew();

                try
                {
                    bool busy = true;
                    while (busy && !m_TurnCts.IsCancellationRequested)
                    {
                        while (inputs.Count > 0)
                        {
                            AgentInput input = inputs[0];
                            inputs.RemoveAt(0);
                            if (!string.IsNullOrWhiteSpace(input.Text))
                            {
                                lock (m_Lock)
                                {
                                    m_History.Add(new ChatMessage(
                                        ChatRole.User,
                                        input.Text));
                                }
                            }
                        }

                        if (m_TurnGenerationCount == 0)
                        {
                            m_Observability.TurnStart(m_TurnId, first.Text);
                        }

                        InjectContextBlocks();
                        var round = await RunModelRoundAsync(m_TurnCts.Token);
                        if (round.IsError)
                        {
                            break;
                        }

                        if (round.ToolCalls.Count > 0)
                        {
                            await m_ToolExecutor.ExecuteAsync(round.ToolCalls, m_TurnCts.Token);
                            await MaybeCompactAsync(m_TurnCts.Token);
                            DrainPending(inputs);
                            if (inputs.Count > 0)
                            {
                                m_Observability.InterleavedDrained(inputs.Count);
                                Emit(new AgentUiEvent
                                {
                                    Kind = "status",
                                    Status = AgentStatus.Working,
                                    Text = "已插入新消息，继续工作",
                                });
                                continue;
                            }
                            if (m_TurnGenerationCount >= MaxToolRoundsPerTurn)
                            {
                                Emit(new AgentUiEvent
                                {
                                    Kind = "status",
                                    Status = AgentStatus.Idle,
                                    Text = "达到最大工具轮次，本回合结束",
                                });
                                break;
                            }
                        }
                        else
                        {
                            DrainPending(inputs);
                            busy = inputs.Count > 0;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // interrupted
                }
                catch (Exception e)
                {
                    m_Observability.Error("loop", e.ToString());
                    Emit(new AgentUiEvent
                    {
                        Kind = "error",
                        Text = "循环错误：" + AgentObservability.RedactSecrets(e.Message),
                    });
                }

                turnTimer.Stop();
                m_Observability.TurnFinish(
                    m_TurnGenerationCount,
                    m_ToolExecutor.FunctionCount,
                    turnTimer.ElapsedMilliseconds,
                    null);
                Status = AgentStatus.Idle;
                Emit(new AgentUiEvent { Kind = "status", Status = AgentStatus.Idle });
                Emit(new AgentUiEvent { Kind = "turn", Text = m_TurnId });

                // Auto-continue: queue the continuation message so the loop picks it
                // up without user input. The simulation clock stays with the player.
                bool suppressAutoContinue = Interlocked.Exchange(ref m_SuppressAutoContinue, 0) != 0;
                if (!suppressAutoContinue && Setting.StaticContinuous && !m_TimeoutOccurred &&
                    m_Pending.Reader.Count == 0 &&
                    !m_LoopCts.IsCancellationRequested)
                {
                    m_Pending.Writer.TryWrite(new AgentInput
                    {
                        Text = "Build the city, grow population, solve problems. Keep working until the city thrives.",
                    });
                }
                m_TimeoutOccurred = false;
            }
        }

        private void InjectContextBlocks()
        {
            lock (m_Lock)
            {
                m_PromptAssembler.Apply(m_History);
            }
        }

        private void AppendHistoryMessage(ChatMessage message)
        {
            lock (m_Lock)
            {
                m_History.Add(message);
            }
        }

        private void DrainPending(List<AgentInput> inputs)
        {
            while (m_Pending.Reader.TryRead(out AgentInput extra))
            {
                inputs.Add(extra);
                m_Observability.InterleavedQueued(extra.Text ?? "");
            }
        }

        private async Task<ModelRound> RunModelRoundAsync(
            CancellationToken cancellationToken,
            bool allowContextRetry = true)
        {
            await MaybeCompactAsync(cancellationToken);

            IChatClient client = m_ClientFactory.GetClient();
            if (client == null)
            {
                Emit(new AgentUiEvent
                {
                    Kind = "error",
                    Text = "模型未配置：请在 Mod 设置中填写 Endpoint / API Key / Model。",
                });
                return ModelRound.Error("no client");
            }

            AgentModelProfile profile = m_ClientFactory.GetProfile();
            var options = new ChatOptions
            {
                ModelId = Setting.StaticModel,
                Temperature = 0.3f,
                MaxOutputTokens = (int)Math.Min(int.MaxValue, profile.OutputReserveTokens),
                Tools = m_ToolSurface.Build(profile),
                ToolMode = ChatToolMode.Auto,
            };

            var updates = new List<ChatResponseUpdate>();
            var pendingDelta = new StringBuilder();
            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                Status = AgentStatus.Thinking;
                Emit(new AgentUiEvent { Kind = "status", Status = AgentStatus.Thinking });
                using (CancellationTokenSource timeoutCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeoutCts.CancelAfter(ModelTimeoutMs);
                    await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
                        m_History, options, timeoutCts.Token))
                    {
                        updates.Add(update);
                        if (!string.IsNullOrEmpty(update.Text))
                        {
                            pendingDelta.Append(update.Text);
                        }
                    }
                }
                if (pendingDelta.Length > 0)
                {
                    Emit(new AgentUiEvent { Kind = "delta", Text = pendingDelta.ToString() });
                }
                timer.Stop();

                ChatResponse response = updates.ToChatResponse();
                IList<ChatMessage> responseMessages = response.Messages ?? new List<ChatMessage>();
                lock (m_Lock)
                {
                    m_History.AddRange(responseMessages);
                }
                m_TurnGenerationCount++;

                var toolCalls = new List<FunctionCallContent>();
                if (m_History.Count > 0)
                {
                    ChatMessage last;
                    lock (m_Lock)
                    {
                        last = m_History[m_History.Count - 1];
                    }
                    foreach (AIContent content in last.Contents)
                    {
                        if (content is FunctionCallContent call)
                        {
                            toolCalls.Add(call);
                        }
                    }
                }

                UpdateTokenEstimate(response);
                EmitGeneration(response, toolCalls, CollectReasoning(updates), timer.ElapsedMilliseconds);
                return new ModelRound
                {
                    Text = response.Text ?? "",
                    ToolCalls = toolCalls,
                    Usage = response.Usage,
                };
            }
            catch (OperationCanceledException)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    timer.Stop();
                    m_TimeoutOccurred = true;
                    m_Observability.Error(
                        "generation-timeout",
                        "model response exceeded " + ModelTimeoutMs + "ms");
                    Emit(new AgentUiEvent
                    {
                        Kind = "error",
                        Text = "模型响应超时（120 秒），本回合已停止，不会自动续跑。请重试。",
                    });
                    return ModelRound.Error("model response timeout");
                }
                throw;
            }
            catch (Exception e)
            {
                timer.Stop();
                if (allowContextRetry && IsContextLengthError(e.ToString()))
                {
                    int historyCount = m_History.Count;
                    long estimateBefore = m_EstimatedTokens;
                    m_Observability.Error("generation-context", e.ToString());
                    await MaybeCompactAsync(cancellationToken, true);
                    if (m_History.Count < historyCount || m_EstimatedTokens < estimateBefore)
                    {
                        return await RunModelRoundAsync(cancellationToken, false);
                    }
                }
                m_Observability.Error("generation", e.ToString());
                string safeMessage = AgentObservability.RedactSecrets(e.Message);
                Emit(new AgentUiEvent { Kind = "error", Text = "模型调用失败：" + safeMessage });
                return ModelRound.Error(safeMessage);
            }
        }

        private static bool IsContextLengthError(string message)
        {
            string normalized = (message ?? "").ToLowerInvariant();
            return normalized.Contains("context_length_exceeded") ||
                normalized.Contains("context length") ||
                normalized.Contains("maximum context") ||
                normalized.Contains("too many tokens") ||
                normalized.Contains("token limit") ||
                normalized.Contains("prompt is too long") ||
                normalized.Contains("input is too long");
        }

        private void UpdateTokenEstimate(ChatResponse response)
        {
            long historyEstimate = new AgentContextBudget(m_ClientFactory.GetProfile()).Estimate(m_History);
            if (response.Usage != null && response.Usage.InputTokenCount > 0)
            {
                m_EstimatedTokens = Math.Max(historyEstimate, response.Usage.InputTokenCount ?? 0);
            }
            else
            {
                m_EstimatedTokens = historyEstimate;
            }
        }

        private async Task MaybeCompactAsync(
            CancellationToken cancellationToken,
            bool forceAggressive = false)
        {
            AgentModelProfile profile = m_ClientFactory.GetProfile();
            var budget = new AgentContextBudget(profile);
            if (!budget.ShouldCompact(m_EstimatedTokens, forceAggressive))
            {
                return;
            }

            List<ChatMessage> oldMessages;
            List<ChatMessage> keptMessages;
            lock (m_Lock)
            {
                AgentContextBudget.CompactionSlice slice = budget.CreateSlice(m_History, forceAggressive);
                if (slice == null)
                {
                    return;
                }
                oldMessages = slice.OldMessages;
                keptMessages = slice.KeptMessages;
            }
            IChatClient client = m_ClientFactory.GetClient();
            if (client == null)
            {
                return;
            }

            try
            {
                // Flatten to plain user text so providers never see orphaned
                // assistant tool_calls without matching tool results.
                var summaryInput = new List<ChatMessage>
                {
                    new ChatMessage(ChatRole.System, CompactionTaskPrompt),
                };
                summaryInput.AddRange(AgentContextBudget.FlattenForSummary(oldMessages));
                ChatResponse summaryResponse = await client.GetResponseAsync(
                    summaryInput,
                    new ChatOptions
                    {
                        ModelId = Setting.StaticModel,
                        Temperature = 0f,
                        MaxOutputTokens = 1200,
                    },
                    cancellationToken);

                string summary = (summaryResponse.Text ?? "").Trim();
                if (!AgentContextBudget.IsUsableSummary(summary))
                {
                    m_Observability.Error(
                        "compact",
                        "rejected unusable summary: " + AgentContextBudget.Truncate(summary, 400));
                    Emit(new AgentUiEvent
                    {
                        Kind = "error",
                        Text = "压缩摘要无效（含工具标记或为空），已跳过本次压缩",
                    });
                    return;
                }

                lock (m_Lock)
                {
                    int nowCount = m_History.Count;
                    int keepStart = AgentContextBudget.FindSafeKeepStart(m_History, keptMessages.Count);
                    if (keepStart < nowCount)
                    {
                        keptMessages = m_History.Skip(keepStart).ToList();
                    }
                    m_PromptAssembler.Rebuild(m_History, summary, keptMessages);
                }
                m_EstimatedTokens = budget.Estimate(m_History);

                m_Observability.Compact(
                    (double)profile.CompactAtTokens / profile.ContextWindowTokens,
                    oldMessages.Count,
                    keptMessages.Count,
                    summary,
                    m_EstimatedTokens);
                Emit(new AgentUiEvent
                {
                    Kind = "compact",
                    Text = "上下文已压缩（移除了 " + oldMessages.Count + " 条旧消息）",
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                m_Observability.Error("compact", e.ToString());
                Emit(new AgentUiEvent
                {
                    Kind = "error",
                    Text = "压缩失败：" + AgentObservability.RedactSecrets(e.Message),
                });
            }
        }

        /// <summary>
        /// Chooses a keep-tail start index that does not split an assistant
        /// tool_calls message from its following tool results.
        /// </summary>
        private static string TruncateForLog(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            {
                return text ?? "";
            }
            return text.Substring(0, maxChars) + "…";
        }

        private static string CollectReasoning(List<ChatResponseUpdate> updates)
        {
            if (updates == null || updates.Count == 0)
            {
                return "";
            }
            var builder = new StringBuilder();
            foreach (ChatResponseUpdate update in updates)
            {
                foreach (AIContent content in update.Contents)
                {
                    if (content is TextReasoningContent reasoning && !string.IsNullOrEmpty(reasoning.Text))
                    {
                        builder.Append(reasoning.Text);
                    }
                }
            }
            return builder.ToString().Trim();
        }

        private void EmitGeneration(ChatResponse response, List<FunctionCallContent> toolCalls, string reasoning, long elapsedMs)
        {
            var calls = new JsonArray();
            foreach (FunctionCallContent call in toolCalls)
            {
                calls.Add(new JsonObject
                {
                    ["name"] = call.Name,
                    ["arguments"] = AgentToolExecutor.SerializeArguments(call.Arguments),
                });
            }
            var usage = new JsonObject();
            if (response.Usage != null)
            {
                usage["input"] = response.Usage.InputTokenCount;
                usage["output"] = response.Usage.OutputTokenCount;
                usage["total"] = response.Usage.TotalTokenCount;
            }
            m_Observability.Generation(
                response.ModelId ?? Setting.StaticModel,
                SummarizeHistory(m_History),
                reasoning,
                calls,
                usage,
                elapsedMs);
        }

        private static string SummarizeHistory(List<ChatMessage> messages)
        {
            var builder = new StringBuilder();
            int count = Math.Max(0, messages.Count - 6);
            if (count > 0)
            {
                builder.Append("[省略前 ").Append(count).Append(" 条] ");
            }
            for (int i = Math.Max(0, messages.Count - 6); i < messages.Count; i++)
            {
                ChatMessage message = messages[i];
                builder.Append(message.Role).Append(": ").Append((message.Text ?? "").Trim());
                foreach (AIContent content in message.Contents)
                {
                    if (content is FunctionCallContent call)
                    {
                        builder.Append(" [call:").Append(call.Name).Append("]");
                    }
                    else if (content is FunctionResultContent result)
                    {
                        builder.Append(" [result]");
                    }
                }
                builder.Append('\n');
            }
            return builder.ToString();
        }

        private static string ToolCallNames(ChatMessage message)
        {
            var names = new List<string>();
            foreach (AIContent content in message.Contents)
            {
                if (content is FunctionCallContent call)
                {
                    names.Add(call.Name);
                }
            }
            return names.Count == 0 ? null : string.Join(",", names);
        }

        private void Emit(AgentUiEvent uiEvent)
        {
            if (uiEvent.Kind == "status")
            {
                Status = uiEvent.Status;
            }
            UiEvent?.Invoke(uiEvent);
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            m_Disposed = true;
            m_LoopCts.Cancel();
            m_TurnCts?.Cancel();
            m_Observability.Dispose();
            m_ClientFactory.Dispose();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private sealed class ModelRound
        {
            public string Text = "";
            public List<FunctionCallContent> ToolCalls = new List<FunctionCallContent>();
            public UsageDetails Usage;
            public bool IsError;

            public static ModelRound Error(string message)
            {
                return new ModelRound { IsError = true, Text = message };
            }
        }
    }
}
