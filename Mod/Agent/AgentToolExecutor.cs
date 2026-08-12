using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace CitiesSkylines2Agent.Agent
{
    internal sealed class AgentToolExecutor
    {
        private const int MaxIdenticalToolRepeats = 3;
        private readonly AgentToolSurface m_ToolSurface;
        private readonly AgentClientFactory m_ClientFactory;
        private readonly AgentObservability m_Observability;
        private readonly Action<AgentUiEvent> m_Emit;
        private readonly Action<ChatMessage> m_AppendHistory;
        private string m_LastSignature = "";
        private int m_IdenticalCount;

        public AgentToolExecutor(AgentToolSurface toolSurface, AgentClientFactory clientFactory,
            AgentObservability observability, Action<AgentUiEvent> emit, Action<ChatMessage> appendHistory)
        {
            m_ToolSurface = toolSurface;
            m_ClientFactory = clientFactory;
            m_Observability = observability;
            m_Emit = emit;
            m_AppendHistory = appendHistory;
        }

        public int FunctionCount { get; private set; }

        public void Reset()
        {
            FunctionCount = 0;
            m_LastSignature = "";
            m_IdenticalCount = 0;
        }

        public async Task ExecuteAsync(IReadOnlyList<FunctionCallContent> toolCalls, CancellationToken cancellationToken)
        {
            m_Emit(new AgentUiEvent { Kind = "status", Status = AgentStatus.Working });
            foreach (FunctionCallContent call in toolCalls)
            {
                string argumentsJson = SerializeArguments(call.Arguments);
                string signature = (call.Name ?? "") + "|" + argumentsJson;
                if (string.Equals(signature, m_LastSignature, StringComparison.Ordinal)) m_IdenticalCount++;
                else { m_LastSignature = signature; m_IdenticalCount = 1; }
                Stopwatch timer = Stopwatch.StartNew();
                m_Emit(new AgentUiEvent { Kind = "tool", Tool = call.Name ?? call.CallId, Text = argumentsJson });
                ToolInvocationResult result;
                try
                {
                    result = m_IdenticalCount > MaxIdenticalToolRepeats
                        ? Error("refused repeated identical tool call (" + (call.Name ?? "") + "); change arguments or take a write action instead of polling")
                        : await InvokeAsync(call.Name, argumentsJson, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    timer.Stop();
                    FunctionCount++;
                    m_Observability.Function(call.Name, argumentsJson, "tool call interrupted", false, timer.ElapsedMilliseconds, 0, "interrupted");
                    m_AppendHistory(new ChatMessage(ChatRole.Tool,
                        new List<AIContent> { new FunctionResultContent(call.CallId, "tool call interrupted") }));
                    throw;
                }
                timer.Stop();
                FunctionCount++;
                m_Observability.Function(call.Name, argumentsJson, result.Text, result.Success, timer.ElapsedMilliseconds, 0,
                    result.Success ? null : result.Text);
                m_AppendHistory(new ChatMessage(ChatRole.Tool,
                    new List<AIContent> { new FunctionResultContent(call.CallId, result.Text) }));
                AppendToolImage(result.ImagePath);
            }
        }

        internal static string SerializeArguments(IDictionary<string, object> arguments)
        {
            return arguments == null || arguments.Count == 0 ? "{}" : JsonSerializer.Serialize(arguments);
        }

        private async Task<ToolInvocationResult> InvokeAsync(string name, string argumentsJson, CancellationToken cancellationToken)
        {
            try
            {
                if (m_ToolSurface.IsMetaTool(name)) return await InvokeMetaToolAsync(name, argumentsJson, cancellationToken);
                if (!m_ToolSurface.IsAvailable(name, m_ClientFactory.GetProfile()))
                {
                    return Error("tool is not enabled; call agent_enable_tool_group for its domain first, or use a vision-capable model for visual tools");
                }
                ToolDefinition tool = ToolCatalog.Find(name);
                if (tool == null) return Error("unknown tool: " + name);

                return await AgentToolBridge.InvokeAsync(tool, argumentsJson, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                m_Observability.Error("tool", e.ToString());
                return Error(AgentObservability.RedactSecrets(e.Message));
            }
        }

        private async Task<ToolInvocationResult> InvokeMetaToolAsync(string name, string argumentsJson, CancellationToken cancellationToken)
        {
            try
            {
                switch (name)
                {
                    case "agent_list_context_blocks": return Ok(ContextBlockStore.ToJsonString());
                    case "agent_enable_tool_group": return EnableToolGroup(argumentsJson);
                    case "agent_add_context_block": return AddContextBlock(argumentsJson);
                    case "agent_remove_context_block": return RemoveContextBlock(argumentsJson);
                    case "agent_read_skill": return ReadSkill(argumentsJson);
                    default: return Error("unknown meta tool " + name);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                m_Observability.Error("meta-tool", e.ToString());
                return Error(AgentObservability.RedactSecrets(e.Message));
            }
        }

        private ToolInvocationResult EnableToolGroup(string argumentsJson)
        {
            using (JsonDocument document = JsonDocument.Parse(argumentsJson))
            {
                string group = GetString(document.RootElement, "group", "");
                bool visionAvailable = m_ClientFactory.GetProfile().VisionAvailable;
                if (!m_ToolSurface.EnableGroup(group, visionAvailable, out string[] enabledTools))
                {
                    string message = string.Equals(group, "visual", StringComparison.OrdinalIgnoreCase) && !visionAvailable
                        ? "visual tools require a vision-capable model" : "unknown or unavailable tool group: " + group;
                    return Error(message);
                }
                m_Emit(new AgentUiEvent { Kind = "status", Text = "Enabled tool group: " + group });
                return Ok(JsonSerializer.Serialize(new
                {
                    enabled = true,
                    group,
                    tools = enabledTools,
                    instruction = "These tools are available in the next model round of this turn. Continue the current task and call them directly.",
                }));
            }
        }

        private ToolInvocationResult AddContextBlock(string argumentsJson)
        {
            using (JsonDocument document = JsonDocument.Parse(argumentsJson))
            {
                JsonElement root = document.RootElement;
                ContextBlock block = ContextBlockStore.Add(
                    GetString(root, "name", ""), GetString(root, "kind", "note"),
                    root.TryGetProperty("data", out JsonElement data) ? data.GetRawText() : "{}");
                m_Emit(new AgentUiEvent { Kind = "status", Text = "Added context block: " + block.Name });
                return Ok(JsonSerializer.Serialize(new { id = block.Id, name = block.Name }));
            }
        }

        private static ToolInvocationResult RemoveContextBlock(string argumentsJson)
        {
            using (JsonDocument document = JsonDocument.Parse(argumentsJson))
            {
                bool removed = ContextBlockStore.Remove(GetString(document.RootElement, "id", ""));
                return Ok(JsonSerializer.Serialize(new { removed }));
            }
        }

        private static ToolInvocationResult ReadSkill(string argumentsJson)
        {
            using (JsonDocument document = JsonDocument.Parse(argumentsJson))
            {
                if (!SkillStore.TryRead(GetString(document.RootElement, "name", ""), out AgentSkill skill)) return Error("Unknown skill");
                return new ToolInvocationResult { Success = true, Text = skill.Content };
            }
        }

        private void AppendToolImage(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !m_ClientFactory.GetProfile().VisionAvailable ||
                !File.Exists(imagePath)) return;
            try
            {
                byte[] image = File.ReadAllBytes(imagePath);
                if (image.Length == 0 || image.Length > 8 * 1024 * 1024)
                {
                    m_Observability.Error("vision-attach", "screenshot exceeds image attachment limit");
                    return;
                }
                m_AppendHistory(new ChatMessage(ChatRole.User, new List<AIContent>
                {
                    new TextContent("Screenshot returned by the screenshot tool."),
                    new DataContent(new ReadOnlyMemory<byte>(image), "image/png"),
                }));
            }
            catch (Exception e)
            {
                m_Observability.Error("vision-attach", e.ToString());
            }
        }

        private static ToolInvocationResult Ok(string text)
        {
            return new ToolInvocationResult { Success = true, Text = text };
        }

        private static ToolInvocationResult Error(string message)
        {
            return new ToolInvocationResult { Success = false, Text = JsonSerializer.Serialize(new { error = message }) };
        }

        private static string GetString(JsonElement element, string name, string fallback)
        {
            return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : fallback;
        }
    }
}
