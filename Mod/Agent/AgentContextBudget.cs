using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace CitiesSkylines2Agent.Agent
{
    internal sealed class AgentContextBudget
    {
        private readonly AgentModelProfile m_Profile;

        public AgentContextBudget(AgentModelProfile profile)
        {
            m_Profile = profile;
        }

        public long Estimate(IReadOnlyList<ChatMessage> messages)
        {
            long total = 0;
            foreach (ChatMessage message in messages) total += EstimateMessage(message);
            return total;
        }

        public bool ShouldCompact(long estimatedTokens, bool forceAggressive)
        {
            return forceAggressive || (estimatedTokens > 0 && estimatedTokens >= m_Profile.CompactAtTokens);
        }

        public CompactionSlice CreateSlice(IReadOnlyList<ChatMessage> history, bool forceAggressive)
        {
            long tailBudget = forceAggressive ? Math.Max(2048, m_Profile.TailBudgetTokens / 2) : m_Profile.TailBudgetTokens;
            int keepStart = FindSafeKeepStartByTokens(history, tailBudget);
            if (keepStart <= 1 || keepStart >= history.Count) return null;
            return new CompactionSlice(history.Take(keepStart).ToList(), history.Skip(keepStart).ToList());
        }

        public static int FindSafeKeepStart(IReadOnlyList<ChatMessage> history, int desiredKeepCount)
        {
            if (history == null || history.Count == 0) return 0;
            int start = Math.Max(0, history.Count - Math.Max(1, desiredKeepCount));
            while (start < history.Count && (IsToolResultMessage(history[start]) || IsImageMessage(history[start]))) start = Math.Max(1, start - 1);
            return start;
        }

        public static List<ChatMessage> FlattenForSummary(IReadOnlyList<ChatMessage> messages)
        {
            var flattened = new List<ChatMessage>();
            foreach (ChatMessage message in messages)
            {
                var builder = new StringBuilder();
                string role = message.Role == ChatRole.Assistant ? "assistant" :
                    message.Role == ChatRole.Tool ? "tool" :
                    message.Role == ChatRole.System ? "system" : "user";
                builder.Append(role).Append(": ");
                if (!string.IsNullOrWhiteSpace(message.Text)) builder.Append(message.Text.Trim());
                foreach (AIContent content in message.Contents)
                {
                    if (content is FunctionCallContent call) builder.Append(" [call:").Append(call.Name).Append(']');
                    else if (content is FunctionResultContent result) builder.Append(" [result:").Append(Truncate(result.Result?.ToString() ?? "", 240)).Append(']');
                }
                string line = builder.ToString().Trim();
                if (line.Length > 0) flattened.Add(new ChatMessage(ChatRole.User, Truncate(line, 2500)));
            }
            return flattened;
        }

        public static bool IsUsableSummary(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary) || summary.Length < 8) return false;
            return summary.IndexOf("DSML", StringComparison.OrdinalIgnoreCase) < 0 &&
                summary.IndexOf("tool_calls", StringComparison.OrdinalIgnoreCase) < 0 &&
                summary.IndexOf("<|", StringComparison.Ordinal) < 0 &&
                summary.IndexOf("invoke name=", StringComparison.OrdinalIgnoreCase) < 0;
        }

        public static string Truncate(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text ?? "";
            return text.Substring(0, maxChars) + "...";
        }

        private static long EstimateMessage(ChatMessage message)
        {
            long total = EstimateText(message.Text ?? "", message.Contents.Count);
            foreach (AIContent content in message.Contents)
            {
                if (content is FunctionCallContent call) total += EstimateText(JsonSerializer.Serialize(call.Arguments), 1);
                else if (content is FunctionResultContent result) total += EstimateText(result.Result?.ToString() ?? "", 1);
                else if (content is TextReasoningContent reasoning) total += EstimateText(reasoning.Text ?? "", 1);
                else if (content is DataContent) total += 2048;
            }
            return total;
        }

        private static long EstimateText(string text, int contentCount)
        {
            if (string.IsNullOrEmpty(text))
            {
                return contentCount;
            }

            long asciiCharacters = 0;
            long nonAsciiCharacters = 0;
            foreach (char character in text)
            {
                if (character <= 0x7f)
                {
                    asciiCharacters++;
                }
                else
                {
                    nonAsciiCharacters++;
                }
            }
            return nonAsciiCharacters + (asciiCharacters + 3) / 4 + contentCount;
        }

        private static int FindSafeKeepStartByTokens(IReadOnlyList<ChatMessage> history, long desiredKeepTokens)
        {
            if (history == null || history.Count == 0) return 0;
            long retainedTokens = 0;
            int start = history.Count;
            while (start > 1)
            {
                long candidateTokens = EstimateMessage(history[start - 1]);
                if (retainedTokens > 0 && retainedTokens + candidateTokens > desiredKeepTokens) break;
                retainedTokens += candidateTokens;
                start--;
            }
            while (start < history.Count && (IsToolResultMessage(history[start]) || IsImageMessage(history[start]))) start = Math.Max(1, start - 1);
            return start;
        }

        private static bool IsToolResultMessage(ChatMessage message)
        {
            if (message.Role == ChatRole.Tool) return true;
            foreach (AIContent content in message.Contents) if (content is FunctionResultContent) return true;
            return false;
        }

        private static bool IsImageMessage(ChatMessage message)
        {
            if (message.Role != ChatRole.User) return false;
            foreach (AIContent content in message.Contents) if (content is DataContent) return true;
            return false;
        }

        internal sealed class CompactionSlice
        {
            public CompactionSlice(List<ChatMessage> oldMessages, List<ChatMessage> keptMessages)
            {
                OldMessages = oldMessages;
                KeptMessages = keptMessages;
            }

            public List<ChatMessage> OldMessages { get; }
            public List<ChatMessage> KeptMessages { get; }
        }
    }
}
