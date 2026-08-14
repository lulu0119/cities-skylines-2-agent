using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>
    /// Timeline usage JSON: omit absent MEAI fields, never coerce unknown to 0.
    /// CollectReasoning text is not token usage; reasoningTokens is ReasoningTokenCount.
    /// </summary>
    internal static class AgentUsageJson
    {
        internal sealed class Coverage
        {
            public int Generations;
            public int Input;
            public int Output;
            public int Total;
            public int CachedInput;
            public int ReasoningTokens;
            public Dictionary<string, int> Additional;
        }

        public static JsonObject Serialize(UsageDetails usage)
        {
            var obj = new JsonObject();
            if (usage == null)
            {
                return obj;
            }
            SetIfPresent(obj, "input", usage.InputTokenCount);
            SetIfPresent(obj, "output", usage.OutputTokenCount);
            SetIfPresent(obj, "total", usage.TotalTokenCount);
            SetIfPresent(obj, "cachedInput", usage.CachedInputTokenCount);
            SetIfPresent(obj, "reasoningTokens", usage.ReasoningTokenCount);
            JsonObject additional = SerializeAdditional(usage.AdditionalCounts);
            if (additional != null)
            {
                obj["additional"] = additional;
            }
            return obj;
        }

        public static void Accumulate(UsageDetails totals, Coverage coverage, UsageDetails usage)
        {
            coverage.Generations++;
            if (usage == null)
            {
                return;
            }
            if (usage.InputTokenCount.HasValue)
            {
                coverage.Input++;
            }
            if (usage.OutputTokenCount.HasValue)
            {
                coverage.Output++;
            }
            if (usage.TotalTokenCount.HasValue)
            {
                coverage.Total++;
            }
            if (usage.CachedInputTokenCount.HasValue)
            {
                coverage.CachedInput++;
            }
            if (usage.ReasoningTokenCount.HasValue)
            {
                coverage.ReasoningTokens++;
            }
            CountAdditional(coverage, usage.AdditionalCounts);
            totals.Add(usage);
        }

        public static JsonObject SerializeCoverage(Coverage coverage)
        {
            var obj = new JsonObject
            {
                ["generations"] = coverage.Generations,
                ["input"] = coverage.Input,
                ["output"] = coverage.Output,
                ["total"] = coverage.Total,
                ["cachedInput"] = coverage.CachedInput,
                ["reasoningTokens"] = coverage.ReasoningTokens,
            };
            JsonObject additional = SerializeAdditionalCoverage(coverage.Additional);
            if (additional != null)
            {
                obj["additional"] = additional;
            }
            return obj;
        }

        private static void CountAdditional(Coverage coverage, AdditionalPropertiesDictionary<long> additional)
        {
            if (additional == null || additional.Count == 0)
            {
                return;
            }
            if (coverage.Additional == null)
            {
                coverage.Additional = new Dictionary<string, int>(StringComparer.Ordinal);
            }
            foreach (KeyValuePair<string, long> entry in additional)
            {
                coverage.Additional.TryGetValue(entry.Key, out int present);
                coverage.Additional[entry.Key] = present + 1;
            }
        }

        private static JsonObject SerializeAdditional(AdditionalPropertiesDictionary<long> additional)
        {
            if (additional == null || additional.Count == 0)
            {
                return null;
            }
            var obj = new JsonObject();
            foreach (KeyValuePair<string, long> entry in additional)
            {
                obj[entry.Key] = entry.Value;
            }
            return obj;
        }

        private static JsonObject SerializeAdditionalCoverage(Dictionary<string, int> additional)
        {
            if (additional == null || additional.Count == 0)
            {
                return null;
            }
            var obj = new JsonObject();
            foreach (KeyValuePair<string, int> entry in additional)
            {
                obj[entry.Key] = entry.Value;
            }
            return obj;
        }

        private static void SetIfPresent(JsonObject obj, string key, long? value)
        {
            if (value.HasValue)
            {
                obj[key] = value.Value;
            }
        }
    }
}
