using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>
    /// JSONL agent timeline: task/turn/generation/function/plan/compact/
    /// interleaved_input/error events for improving tools, prompts and skills.
    /// One event per line, append-only, size-rotated. API keys never appear.
    /// </summary>
    public sealed class AgentObservability : IDisposable
    {
        public const long MaxFileBytes = 50L * 1024 * 1024;
        public const int KeepFiles = 5;

        private readonly object m_Lock = new object();
        private readonly string m_FilePath;
        private readonly string m_SessionId;
        private StreamWriter m_Writer;
        private long m_Sequence;
        private long m_Size;
        private bool m_Disposed;

        public AgentObservability(string sessionId)
        {
            m_SessionId = sessionId;
            ModPaths.EnsureDirectories();
            m_FilePath = Path.Combine(
                ModPaths.LogsDirectory,
                "agent-timeline-" + sessionId + ".jsonl");
            try
            {
                m_Writer = new StreamWriter(m_FilePath, true, Encoding.UTF8);
                m_Size = new FileInfo(m_FilePath).Length;
            }
            catch (Exception e)
            {
                CS2MCP.Mod.Log.Warn($"observability log unavailable: {e.Message}");
            }
        }

        public string SessionId => m_SessionId;

        public string TurnId { get; set; }

        public void Record(string type, JsonObject data)
        {
            lock (m_Lock)
            {
                if (m_Writer == null || m_Disposed)
                {
                    return;
                }
                var line = new JsonObject
                {
                    ["seq"] = ++m_Sequence,
                    ["ts"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    ["session"] = m_SessionId,
                    ["turn"] = TurnId,
                    ["type"] = type,
                    ["data"] = data ?? new JsonObject(),
                };
                string json = line.ToJsonString();
                try
                {
                    m_Writer.WriteLine(json);
                    m_Writer.Flush();
                    m_Size += Encoding.UTF8.GetByteCount(json) + 1;
                    if (m_Size > MaxFileBytes)
                    {
                        Rotate();
                    }
                }
                catch (Exception e)
                {
                    CS2MCP.Mod.Log.Warn($"observability write failed: {e.Message}");
                }
            }
        }

        public void TaskStart(string model, long windowTokens, double compactThreshold)
        {
            Record("task.start", new JsonObject
            {
                ["model"] = model,
                ["windowTokens"] = windowTokens,
                ["compactThreshold"] = compactThreshold,
            });
        }

        public void TurnStart(string turnId, string userText)
        {
            TurnId = turnId;
            Record("turn.start", new JsonObject { ["user"] = userText });
        }

        public void TurnFinish(int generationCount, int functionCount, long elapsedMs, JsonObject usage)
        {
            Record("turn.finish", new JsonObject
            {
                ["generations"] = generationCount,
                ["functions"] = functionCount,
                ["elapsedMs"] = elapsedMs,
                ["usage"] = usage ?? new JsonObject(),
            });
        }

        public void Generation(string model, string messageSummary, string reasoning, JsonArray toolCalls, JsonObject usage, long elapsedMs)
        {
            Record("generation", new JsonObject
            {
                ["model"] = model,
                ["input"] = Truncate(messageSummary, 65536),
                ["reasoning"] = Truncate(reasoning, 65536),
                ["toolCalls"] = toolCalls ?? new JsonArray(),
                ["usage"] = usage ?? new JsonObject(),
                ["elapsedMs"] = elapsedMs,
            });
        }

        public void Function(
            string toolName,
            string arguments,
            string result,
            bool success,
            long elapsedMs,
            long queuedMs,
            string error = null)
        {
            Record("function", new JsonObject
            {
                ["tool"] = toolName,
                ["arguments"] = Truncate(arguments, 32768),
                ["result"] = Truncate(result, 32768),
                ["success"] = success,
                ["elapsedMs"] = elapsedMs,
                ["queuedMs"] = queuedMs,
                ["error"] = error,
            });
        }

        public void InterleavedQueued(string text)
        {
            Record("interleaved_input", new JsonObject { ["state"] = "queued", ["text"] = text });
        }

        public void InterleavedDrained(int count)
        {
            Record("interleaved_input", new JsonObject { ["state"] = "drained", ["count"] = count });
        }

        public void Compact(double threshold, int removedMessages, int keptMessages, string summary, long newEstimate)
        {
            Record("compact", new JsonObject
            {
                ["threshold"] = threshold,
                ["removedMessages"] = removedMessages,
                ["keptMessages"] = keptMessages,
                ["summary"] = Truncate(summary, 32768),
                ["newEstimate"] = newEstimate,
            });
        }

        public void Plan(string action, JsonObject plan)
        {
            Record("plan", new JsonObject { ["action"] = action, ["plan"] = plan ?? new JsonObject() });
        }

        public void Error(string source, string message)
        {
            Record("error", new JsonObject
            {
                ["source"] = source,
                ["message"] = Truncate(message, 8192),
            });
        }

        public static string Truncate(string value, int maxChars)
        {
            if (value == null)
            {
                return null;
            }
            return value.Length <= maxChars ? value : value.Substring(0, maxChars) + "…[truncated]";
        }

        private void Rotate()
        {
            if (m_Writer == null)
            {
                return;
            }
            try
            {
                m_Writer.Dispose();
                m_Writer = null;
                for (int i = KeepFiles - 1; i >= 1; i--)
                {
                    string from = m_FilePath + "." + i;
                    string to = m_FilePath + "." + (i + 1);
                    if (File.Exists(from))
                    {
                        File.Delete(to);
                        File.Move(from, to);
                    }
                }
                if (File.Exists(m_FilePath))
                {
                    File.Move(m_FilePath, m_FilePath + ".1");
                }
                m_Writer = new StreamWriter(m_FilePath, true, Encoding.UTF8);
                m_Size = 0;
            }
            catch (Exception e)
            {
                CS2MCP.Mod.Log.Warn($"observability rotation failed: {e.Message}");
            }
        }

        public void Dispose()
        {
            lock (m_Lock)
            {
                m_Disposed = true;
                m_Writer?.Dispose();
                m_Writer = null;
            }
        }
    }
}
