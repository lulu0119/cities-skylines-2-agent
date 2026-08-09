using System;

namespace CitiesSkylines2Agent.Agent
{
    internal sealed class ModelCapabilities
    {
        public long ContextWindowTokens { get; set; }
        public long MaxOutputTokens { get; set; }
        public bool SupportsVision { get; set; }
        public string Source { get; set; } = "";

        /// <summary>Token count at which compaction is triggered (~82% of window).</summary>
        public long CompactAtTokens => Math.Max(8_000, (long)(ContextWindowTokens * 0.82));

        /// <summary>Tokens reserved for model output (capped at 64K, min 4K).</summary>
        public long OutputReserveTokens => Math.Min(Math.Max(4_096, ContextWindowTokens / 10), 64_000);

        /// <summary>How many tokens worth of tail messages to keep verbatim during compaction.</summary>
        public long TailBudgetTokens => Math.Min(16_384, Math.Max(4_096, CompactAtTokens / 16));
    }
}
