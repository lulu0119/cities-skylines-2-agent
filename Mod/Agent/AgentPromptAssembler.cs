using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace CitiesSkylines2Agent.Agent
{
    internal sealed class AgentPromptAssembler
    {
        private const string SkillIndexPrefix = "Available skills (call agent_read_skill to load full instructions):";
        private const string ContextBlockPrefix = "Player context blocks:\n";
        private const string ProblemLedgerPrefix =
            "Problem ledger (source of truth; notifications is the raw snapshot):\n";

        public AgentPromptAssembler(string systemPrompt, string summaryPrefix)
        {
            SystemPrompt = systemPrompt;
            SummaryPrefix = summaryPrefix;
        }

        public string SystemPrompt { get; }
        public string SummaryPrefix { get; }

        public void Apply(List<ChatMessage> history, string problemLedger = null)
        {
            if (history == null) return;
            EnsureSystemPrompt(history);
            RemoveDynamicMessages(history);
            int insertAt = LeadingSystemMessageCount(history);
            string skillIndex = SkillStore.RenderIndex();
            if (!string.IsNullOrWhiteSpace(skillIndex))
            {
                history.Insert(insertAt++, new ChatMessage(ChatRole.System, skillIndex));
            }
            string contextBlocks = ContextBlockStore.RenderAll();
            if (!string.IsNullOrWhiteSpace(contextBlocks))
            {
                history.Insert(insertAt++, new ChatMessage(ChatRole.System, ContextBlockPrefix + contextBlocks));
            }
            if (!string.IsNullOrWhiteSpace(problemLedger))
            {
                history.Insert(insertAt, new ChatMessage(ChatRole.System, ProblemLedgerPrefix + problemLedger));
            }
        }

        public void Rebuild(
            List<ChatMessage> history,
            string summary,
            List<ChatMessage> keptMessages,
            string problemLedger = null)
        {
            history.Clear();
            history.Add(new ChatMessage(ChatRole.System, SystemPrompt));
            history.Add(new ChatMessage(ChatRole.System, SummaryPrefix + summary));
            history.AddRange(keptMessages);
            Apply(history, problemLedger);
        }

        private void EnsureSystemPrompt(List<ChatMessage> history)
        {
            int promptIndex = -1;
            for (int index = history.Count - 1; index >= 0; index--)
            {
                ChatMessage message = history[index];
                if (message.Role != ChatRole.System || !string.Equals(message.Text, SystemPrompt, StringComparison.Ordinal)) continue;
                if (promptIndex < 0) promptIndex = index;
                else { history.RemoveAt(index); promptIndex--; }
            }
            if (promptIndex < 0)
            {
                history.Insert(0, new ChatMessage(ChatRole.System, SystemPrompt));
            }
            else if (promptIndex != 0)
            {
                ChatMessage prompt = history[promptIndex];
                history.RemoveAt(promptIndex);
                history.Insert(0, prompt);
            }
        }

        private static void RemoveDynamicMessages(List<ChatMessage> history)
        {
            for (int index = history.Count - 1; index >= 0; index--)
            {
                ChatMessage message = history[index];
                if (message.Role != ChatRole.System) continue;
                string text = message.Text ?? "";
                if (text.StartsWith(SkillIndexPrefix, StringComparison.Ordinal)
                    || text.StartsWith(ContextBlockPrefix, StringComparison.Ordinal)
                    || text.StartsWith(ProblemLedgerPrefix, StringComparison.Ordinal))
                {
                    history.RemoveAt(index);
                }
            }
        }

        private static int LeadingSystemMessageCount(List<ChatMessage> history)
        {
            int count = 0;
            while (count < history.Count && history[count].Role == ChatRole.System) count++;
            return count;
        }
    }
}
