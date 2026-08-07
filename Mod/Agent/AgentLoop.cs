using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
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
        private const string SystemPrompt = @"You are the in-game AI city mayor assistant for Cities: Skylines 2, running inside the game process. Your goal is to help the player manage the city and to autonomously execute tasks over long horizons.

Working style:
1. Observe briefly first (game_state / city_overview / demand / screenshot), then act. Do not repeat the same read tool more than twice without a write. terrain / gridmap require a map range and return compact 8×8 samples (not full arrays).
2. No tool requires the game to be paused. The game validates construction while the simulation runs; if a build/zone/upgrade call fails, the world changed between your read and the write — retry with find_placement or a nearby position instead of repeating the same call.
3. Before destructive actions (demolish), list the targets and explain why.
4. Use screenshot to verify your work; use set_camera to inspect the city.
5. When you need a player decision (major spending, demolishing something unexpected), ask explicitly and do not act until confirmed.
6. End every turn with a concise summary (what was done, results, next steps).
7. Use agent_advance_time to advance time; it waits (progress is shown in the chat), auto-pauses and returns the final state. Never poll game_state in a loop waiting for simulation.
8. Prefer place / road / zone tools that change the city over endless prefab listing and state reads. For roads: short segments on owned land near existing nodes; e1/e2 are elevation meters, never entity ids.

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
        private const int MaxIdenticalToolRepeats = 3;

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

        private readonly Channel<AgentInput> m_Pending = Channel.CreateUnbounded<AgentInput>();
        private readonly List<ChatMessage> m_History = new List<ChatMessage>();
        private readonly object m_Lock = new object();
        private readonly AgentObservability m_Observability;

        private IChatClient m_ChatClient;
        private Task m_LoopTask;
        private CancellationTokenSource m_TurnCts;
        private CancellationTokenSource m_LoopCts = new CancellationTokenSource();
        private string m_SessionId;
        private string m_TurnId;
        private string m_ConfigSignature;
        private long m_EstimatedTokens;
        private int m_TurnGenerationCount;
        private int m_TurnFunctionCount;
        private string m_TurnLastToolSignature = "";
        private int m_TurnIdenticalToolCount;
        private string m_ContextSignature = "";
        private string m_SkillsRendered = "";
        private int m_SkillsMessageIndex = -1;
        private bool m_Disposed;

        public AgentLoop()
        {
            Instance = this;
            m_SessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
            m_Observability = new AgentObservability(m_SessionId);
        }

        public event Action<AgentUiEvent> UiEvent;

        public AgentObservability Observability => m_Observability;

        public AgentStatus Status { get; private set; } = AgentStatus.Idle;

        public bool IsBusy => Status == AgentStatus.Thinking || Status == AgentStatus.Working;

        /// <summary>Queue a user message; injected after the current tool round.</summary>
        public void Send(string text)
        {
            // #region agent log
            Debug548a1a.Log(
                "H-DUP-A",
                "AgentLoop.Send",
                "csharp_emit_user",
                "{\"textLen\":" + (text ?? "").Length + "}");
            // #endregion
            m_Pending.Writer.TryWrite(new AgentInput { Text = text ?? "" });
            m_Observability.InterleavedQueued(text ?? "");
            Emit(new AgentUiEvent { Kind = "user", Text = text ?? "" });
            EnsureLoop();
        }

        public void Interrupt()
        {
            m_TurnCts?.Cancel();
            Status = AgentStatus.Interrupted;
            Emit(new AgentUiEvent { Kind = "status", Status = AgentStatus.Interrupted, Text = "已中断当前回合" });
        }

        public void RefreshConfig()
        {
            lock (m_Lock)
            {
                m_ChatClient = null;
            }
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
                return new JsonObject
                {
                    ["status"] = Status.ToString(),
                    ["busy"] = IsBusy,
                    ["pendingInputs"] = m_Pending.Reader.Count,
                    ["session"] = m_SessionId,
                    ["turn"] = m_TurnId,
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
            m_Observability.TaskStart(Setting.StaticModel, Setting.StaticWindowTokens, Setting.StaticCompactThreshold);
            lock (m_Lock)
            {
                m_History.Add(new ChatMessage(ChatRole.System, SystemPrompt));
            }
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
                m_TurnFunctionCount = 0;
                m_TurnLastToolSignature = "";
                m_TurnIdenticalToolCount = 0;
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
                            await ExecuteToolCallsAsync(round.ToolCalls, m_TurnCts.Token);
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
                            if (m_TurnGenerationCount >= Setting.StaticMaxToolRounds)
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
                    Emit(new AgentUiEvent { Kind = "error", Text = "循环错误：" + e.Message });
                }

                turnTimer.Stop();
                m_Observability.TurnFinish(
                    m_TurnGenerationCount,
                    m_TurnFunctionCount,
                    turnTimer.ElapsedMilliseconds,
                    null);
                Status = AgentStatus.Idle;
                Emit(new AgentUiEvent { Kind = "status", Status = AgentStatus.Idle });
                Emit(new AgentUiEvent { Kind = "turn", Text = m_TurnId });
            }
        }

        /// <summary>
        /// Injects player context blocks (map pins / selected networks) as a
        /// system message whenever the set changes.
        /// </summary>
        private void InjectContextBlocks()
        {
            EnsureSkillsInjected();
            string signature = string.Join(",", ContextBlockStore.Blocks.ConvertAll(b => b.Id));
            if (signature == m_ContextSignature)
            {
                return;
            }
            string rendered = ContextBlockStore.RenderAll();
            if (string.IsNullOrWhiteSpace(rendered))
            {
                m_ContextSignature = signature;
                return;
            }
            lock (m_Lock)
            {
                m_History.Add(new ChatMessage(
                    ChatRole.System,
                    "Player context blocks:\n" + rendered));
            }
            m_ContextSignature = signature;
        }

        /// <summary>
        /// Injects the enabled skills (from Setting.EnabledSkills) as one system
        /// message and swaps it in place when the set or content changes.
        /// </summary>
        private void EnsureSkillsInjected()
        {
            string rendered = SkillStore.RenderEnabled(ParseEnabledSkills());
            if (string.Equals(rendered, m_SkillsRendered, StringComparison.Ordinal))
            {
                return;
            }
            lock (m_Lock)
            {
                if (m_SkillsMessageIndex >= 0 && m_SkillsMessageIndex < m_History.Count)
                {
                    m_History.RemoveAt(m_SkillsMessageIndex);
                }
                if (!string.IsNullOrWhiteSpace(rendered))
                {
                    m_History.Add(new ChatMessage(ChatRole.System, "Active skills:\n" + rendered));
                    m_SkillsMessageIndex = m_History.Count - 1;
                }
                else
                {
                    m_SkillsMessageIndex = -1;
                }
            }
            m_SkillsRendered = rendered;
        }

        private static List<string> ParseEnabledSkills()
        {
            var names = new List<string>();
            string raw = Setting.StaticEnabledSkills ?? "";
            foreach (string part in raw.Split(','))
            {
                string name = part.Trim();
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }
            return names;
        }

        private void DrainPending(List<AgentInput> inputs)
        {
            while (m_Pending.Reader.TryRead(out AgentInput extra))
            {
                inputs.Add(extra);
                m_Observability.InterleavedQueued(extra.Text ?? "");
            }
        }

        private async Task<ModelRound> RunModelRoundAsync(CancellationToken cancellationToken)
        {
            await MaybeCompactAsync(cancellationToken);

            IChatClient client = EnsureChatClient();
            if (client == null)
            {
                Emit(new AgentUiEvent
                {
                    Kind = "error",
                    Text = "模型未配置：请在 Mod 设置中填写 Endpoint / API Key / Model。",
                });
                return ModelRound.Error("no client");
            }

            var options = new ChatOptions
            {
                ModelId = Setting.StaticModel,
                Temperature = 0.3f,
                Tools = BuildToolDeclarations(),
                ToolMode = ChatToolMode.Auto,
            };

            var updates = new List<ChatResponseUpdate>();
            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                Status = AgentStatus.Thinking;
                Emit(new AgentUiEvent { Kind = "status", Status = AgentStatus.Thinking });
                await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(m_History, options, cancellationToken))
                {
                    updates.Add(update);
                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        Emit(new AgentUiEvent { Kind = "delta", Text = update.Text });
                    }
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

                UpdateTokenEstimate(response, toolCalls.Count);
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
                throw;
            }
            catch (Exception e)
            {
                timer.Stop();
                m_Observability.Error("generation", e.ToString());
                Emit(new AgentUiEvent { Kind = "error", Text = "模型调用失败：" + e.Message });
                return ModelRound.Error(e.Message);
            }
        }

        private async Task ExecuteToolCallsAsync(
            List<FunctionCallContent> toolCalls,
            CancellationToken cancellationToken)
        {
            Status = AgentStatus.Working;
            Emit(new AgentUiEvent { Kind = "status", Status = AgentStatus.Working });

            foreach (FunctionCallContent call in toolCalls)
            {
                string argumentsJson = SerializeArguments(call.Arguments);
                string signature = (call.Name ?? "") + "|" + argumentsJson;
                if (string.Equals(signature, m_TurnLastToolSignature, StringComparison.Ordinal))
                {
                    m_TurnIdenticalToolCount++;
                }
                else
                {
                    m_TurnLastToolSignature = signature;
                    m_TurnIdenticalToolCount = 1;
                }

                Stopwatch timer = Stopwatch.StartNew();
                // #region agent log
                // Skip tool_start spam; only log slow/end results (H-BLK-K).
                // #endregion
                Emit(new AgentUiEvent
                {
                    Kind = "tool",
                    Tool = call.Name ?? call.CallId,
                    Text = argumentsJson,
                });

                ToolInvocationResult result;
                if (m_TurnIdenticalToolCount > MaxIdenticalToolRepeats)
                {
                    result = new ToolInvocationResult
                    {
                        Success = false,
                        Text = "{\"error\":\"refused repeated identical tool call (" +
                            (call.Name ?? "") +
                            "); change arguments or take a write action instead of polling\"}",
                    };
                }
                else
                {
                    bool isMeta = call.Name != null && call.Name.StartsWith("agent_", StringComparison.Ordinal);
                    if (isMeta)
                    {
                        result = await InvokeMetaToolAsync(call.Name, argumentsJson, cancellationToken);
                    }
                else
                {
                    ToolDefinition tool = ToolCatalog.Find(call.Name);
                    if (tool == null)
                        {
                            result = new ToolInvocationResult
                            {
                                Success = false,
                                Text = "{\"error\":\"unknown tool: " + call.Name + "\"}",
                            };
                        }
                        else
                        {
                            Action<string> progress = null;
                            if (string.Equals(call.Name, "agent_advance_time", StringComparison.Ordinal))
                            {
                                progress = text => Emit(new AgentUiEvent { Kind = "progress", Text = text });
                            }
                            result = await AgentToolBridge.InvokeAsync(
                                tool,
                                argumentsJson,
                                cancellationToken,
                                progress);
                        }
                    }
                }
                timer.Stop();
                m_TurnFunctionCount++;
                // #region agent log
                if (timer.ElapsedMilliseconds >= 100)
                {
                    string blkId =
                        string.Equals(call.Name, "screenshot", StringComparison.Ordinal) ? "H-BLK-B" :
                        (call.Name != null && (call.Name.Contains("simulation") || call.Name.Contains("advance_time")))
                            ? "H-BLK-A" : "H-BLK-C";
                    Debug548a1a.Log(
                        blkId,
                        "AgentLoop.ExecuteToolCallsAsync",
                        "tool_end_slow",
                        "{\"tool\":\"" + (call.Name ?? "") +
                        "\",\"ms\":" + timer.ElapsedMilliseconds +
                        ",\"ok\":" + (result.Success ? "true" : "false") + "}");
                }
                // #endregion

                m_Observability.Function(
                    call.Name,
                    argumentsJson,
                    result.Text,
                    result.Success,
                    timer.ElapsedMilliseconds,
                    0,
                    result.Success ? null : result.Text);

                var toolMessage = new ChatMessage(
                    ChatRole.Tool,
                    new List<AIContent>
                    {
                        new FunctionResultContent(call.CallId, result.Text),
                    });
                lock (m_Lock)
                {
                    m_History.Add(toolMessage);
                }
            }
        }

        private async Task<ToolInvocationResult> InvokeMetaToolAsync(
            string name,
            string argumentsJson,
            CancellationToken cancellationToken)
        {
            try
            {
                switch (name)
                {
                    case "agent_list_context_blocks":
                        return Ok(ContextBlockStore.ToJsonString());
                    case "agent_add_context_block":
                        return AddContextBlock(argumentsJson);
                    case "agent_remove_context_block":
                        return RemoveContextBlock(argumentsJson);
                    default:
                        return Ok("{\"error\":\"unknown meta tool " + name + "\"}");
                }
            }
            catch (Exception e)
            {
                m_Observability.Error("meta-tool", e.ToString());
                return Ok("{\"error\":\"" + JsonEncodedText.Encode(e.Message).ToString() + "\"}");
            }
        }

        private ToolInvocationResult AddContextBlock(string argumentsJson)
        {
            using (JsonDocument document = JsonDocument.Parse(argumentsJson))
            {
                JsonElement root = document.RootElement;
                ContextBlock block = ContextBlockStore.Add(
                    GetString(root, "name", ""),
                    GetString(root, "kind", "note"),
                    root.TryGetProperty("data", out JsonElement dataElement)
                        ? dataElement.GetRawText()
                        : "{}");
                Emit(new AgentUiEvent { Kind = "status", Text = "已添加上下文块：" + block.Name });
                return Ok("{\"id\":\"" + block.Id + "\",\"name\":\"" + block.Name + "\"}");
            }
        }

        private ToolInvocationResult RemoveContextBlock(string argumentsJson)
        {
            using (JsonDocument document = JsonDocument.Parse(argumentsJson))
            {
                string id = GetString(document.RootElement, "id", "");
                bool removed = ContextBlockStore.Remove(id);
                return Ok("{\"removed\":" + (removed ? "true" : "false") + "}");
            }
        }

        private static ToolInvocationResult Ok(string json)
        {
            return new ToolInvocationResult { Success = true, Text = json };
        }

        private static string GetString(JsonElement element, string name, string fallback)
        {
            if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            return fallback;
        }

        private static string SerializeArguments(IDictionary<string, object> arguments)
        {
            if (arguments == null || arguments.Count == 0)
            {
                return "{}";
            }
            return JsonSerializer.Serialize(arguments);
        }

        private void UpdateTokenEstimate(ChatResponse response, int toolCallCount)
        {
            if (response.Usage != null && response.Usage.InputTokenCount > 0)
            {
                m_EstimatedTokens = (response.Usage.InputTokenCount ?? 0) +
                                     EstimateTokens(response.Text ?? "", toolCallCount);
            }
            else
            {
                m_EstimatedTokens = EstimateTokens(m_History);
            }
        }

        private long EstimateTokens(List<ChatMessage> messages)
        {
            long total = 0;
            foreach (ChatMessage message in messages)
            {
                total += EstimateTokens(message.Text ?? "", message.Contents.Count);
            }
            return total;
        }

        private static long EstimateTokens(string text, int contentCount)
        {
            return (text == null ? 0 : text.Length / 4) + contentCount;
        }

        private async Task MaybeCompactAsync(CancellationToken cancellationToken)
        {
            if (m_EstimatedTokens <= 0 ||
                m_EstimatedTokens < Setting.StaticWindowTokens * Setting.StaticCompactThreshold)
            {
                return;
            }

            List<ChatMessage> oldMessages;
            List<ChatMessage> keptMessages;
            lock (m_Lock)
            {
                int totalCount = m_History.Count;
                int keepStart = FindSafeKeepStart(m_History, Setting.StaticKeepTailMessages);
                if (keepStart <= 1 || keepStart >= totalCount)
                {
                    return;
                }
                oldMessages = m_History.Take(keepStart).ToList();
                keptMessages = m_History.Skip(keepStart).ToList();
            }
            IChatClient client = EnsureChatClient();
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
                summaryInput.AddRange(FlattenMessagesForSummary(oldMessages));
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
                if (!IsUsableSummary(summary))
                {
                    m_Observability.Error(
                        "compact",
                        "rejected unusable summary: " + TruncateForLog(summary, 400));
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
                    int keepStart = FindSafeKeepStart(m_History, keptMessages.Count);
                    if (keepStart < nowCount)
                    {
                        keptMessages = m_History.Skip(keepStart).ToList();
                    }
                    m_History.Clear();
                    m_History.Add(new ChatMessage(ChatRole.System, SystemPrompt));
                    m_History.Add(new ChatMessage(ChatRole.System, SummaryPrefix + summary));
                    m_History.AddRange(keptMessages);
                    m_SkillsMessageIndex = -1;
                    m_SkillsRendered = "";
                }
                m_EstimatedTokens = EstimateTokens(m_History);

                m_Observability.Compact(
                    Setting.StaticCompactThreshold,
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
                Emit(new AgentUiEvent { Kind = "error", Text = "压缩失败：" + e.Message });
            }
        }

        /// <summary>
        /// Chooses a keep-tail start index that does not split an assistant
        /// tool_calls message from its following tool results.
        /// </summary>
        private static int FindSafeKeepStart(List<ChatMessage> history, int desiredKeepCount)
        {
            if (history == null || history.Count == 0)
            {
                return 0;
            }
            int start = Math.Max(0, history.Count - Math.Max(1, desiredKeepCount));
            while (start < history.Count && IsToolResultMessage(history[start]))
            {
                if (start == 0)
                {
                    start++;
                    break;
                }
                start--;
            }
            return start;
        }

        private static bool IsToolResultMessage(ChatMessage message)
        {
            if (message.Role == ChatRole.Tool)
            {
                return true;
            }
            foreach (AIContent content in message.Contents)
            {
                if (content is FunctionResultContent)
                {
                    return true;
                }
            }
            return false;
        }

        private static List<ChatMessage> FlattenMessagesForSummary(List<ChatMessage> messages)
        {
            var flattened = new List<ChatMessage>();
            foreach (ChatMessage message in messages)
            {
                var builder = new StringBuilder();
                string role = message.Role == ChatRole.Assistant ? "assistant"
                    : message.Role == ChatRole.Tool ? "tool"
                    : message.Role == ChatRole.System ? "system"
                    : "user";
                builder.Append(role).Append(": ");
                if (!string.IsNullOrWhiteSpace(message.Text))
                {
                    builder.Append(message.Text.Trim());
                }
                foreach (AIContent content in message.Contents)
                {
                    if (content is FunctionCallContent call)
                    {
                        builder.Append(" [call:").Append(call.Name).Append(']');
                    }
                    else if (content is FunctionResultContent result)
                    {
                        string value = result.Result?.ToString() ?? "";
                        builder.Append(" [result:").Append(TruncateForLog(value, 240)).Append(']');
                    }
                }
                string line = builder.ToString().Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                flattened.Add(new ChatMessage(ChatRole.User, TruncateForLog(line, 2500)));
            }
            return flattened;
        }

        private static bool IsUsableSummary(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary) || summary.Length < 8)
            {
                return false;
            }
            if (summary.IndexOf("DSML", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
            if (summary.IndexOf("tool_calls", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
            if (summary.IndexOf("<|", StringComparison.Ordinal) >= 0)
            {
                return false;
            }
            if (summary.IndexOf("invoke name=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
            return true;
        }

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
                    ["arguments"] = SerializeArguments(call.Arguments),
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

        private IChatClient EnsureChatClient()
        {
            string signature = Setting.StaticProvider + "|" + Setting.StaticEndpoint + "|" + Setting.StaticApiKey + "|" + Setting.StaticModel;
            lock (m_Lock)
            {
                if (m_ChatClient != null && string.Equals(m_ConfigSignature, signature, StringComparison.Ordinal))
                {
                    return m_ChatClient;
                }
                m_ChatClient?.Dispose();
                m_ChatClient = null;
                m_ConfigSignature = signature;

                if (string.IsNullOrWhiteSpace(Setting.StaticEndpoint) ||
                    string.IsNullOrWhiteSpace(Setting.StaticApiKey) ||
                    string.IsNullOrWhiteSpace(Setting.StaticModel))
                {
                    return null;
                }

                try
                {
                    var options = new OpenAIClientOptions
                    {
                        Endpoint = new Uri(Setting.StaticEndpoint),
                    };
                    var openAiClient = new OpenAIClient(new ApiKeyCredential(Setting.StaticApiKey), options);
                    ChatClient chatClient = openAiClient.GetChatClient(Setting.StaticModel);
                    m_ChatClient = chatClient.AsIChatClient();
                    return m_ChatClient;
                }
                catch (Exception e)
                {
                    m_Observability.Error("client-create", e.ToString());
                    return null;
                }
            }
        }

        private static List<AITool> BuildToolDeclarations()
        {
            var tools = new List<AITool>();
            foreach (ToolDefinition tool in ToolCatalog.Tools)
            {
                if (!Setting.StaticEnableVisionTools &&
                    (string.Equals(tool.Name, "screenshot", StringComparison.Ordinal) ||
                     string.Equals(tool.Name, "set_camera", StringComparison.Ordinal)))
                {
                    continue;
                }
                tools.Add(AIFunctionFactory.CreateDeclaration(
                    tool.Name,
                    tool.Description,
                    tool.Parameters,
                    null));
            }
            tools.Add(AIFunctionFactory.CreateDeclaration(
                "agent_list_context_blocks",
                "List the named context blocks the player created (map pins / selected networks).",
                JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}").RootElement.Clone(),
                null));
            tools.Add(AIFunctionFactory.CreateDeclaration(
                "agent_add_context_block",
                "Register a piece of natural-language information as a named context block; it is provided to the model every turn.",
                JsonDocument.Parse(@"{
  ""type"":""object"",
  ""properties"":{
    ""name"":{""type"":""string"",""description"":""Block name""},
    ""kind"":{""type"":""string"",""enum"":[""pin"",""network"",""note""]},
    ""data"":{""type"":""string"",""description"":""Content (coordinates / network description, etc.)""}
  },
  ""required"":[""name"",""data""]
}").RootElement.Clone(),
                null));
            tools.Add(AIFunctionFactory.CreateDeclaration(
                "agent_remove_context_block",
                "Delete a context block by id.",
                JsonDocument.Parse(@"{
  ""type"":""object"",
  ""properties"":{""id"":{""type"":""string""}},
  ""required"":[""id""]
}").RootElement.Clone(),
                null));
            return tools;
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
            m_ChatClient?.Dispose();
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
