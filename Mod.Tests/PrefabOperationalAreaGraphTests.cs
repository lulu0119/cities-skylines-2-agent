using Xunit;

namespace CS2MCP
{
    public sealed class PrefabOperationalAreaGraphTests
    {
        [Fact]
        public void Facility_only_node_does_not_declare_an_extractor_area()
        {
            Assert.False(GuaranteesExtractorArea(
                root: "ExtractorAgricultureCattleShed01",
                extractorNodes: Array.Empty<string>(),
                placeholders: new Dictionary<string, string[]>()));
        }

        [Fact]
        public void Direct_extractor_area_declares_a_hub()
        {
            Assert.True(GuaranteesExtractorArea(
                root: "AgricultureExtractorArea",
                extractorNodes: new[] { "AgricultureExtractorArea" },
                placeholders: new Dictionary<string, string[]>()));
        }

        [Fact]
        public void Mixed_placeholder_candidates_fail_closed()
        {
            Assert.False(GuaranteesExtractorArea(
                root: "AgricultureAreaPlaceholder",
                extractorNodes: new[] { "AgricultureExtractorArea" },
                placeholders: new Dictionary<string, string[]>
                {
                    ["AgricultureAreaPlaceholder"] = new[]
                    {
                        "AgricultureExtractorArea",
                        "HangaroundArea",
                    },
                }));
        }

        [Fact]
        public void Cyclic_placeholder_graph_fails_closed()
        {
            Assert.False(GuaranteesExtractorArea(
                root: "A",
                extractorNodes: Array.Empty<string>(),
                placeholders: new Dictionary<string, string[]>
                {
                    ["A"] = new[] { "B" },
                    ["B"] = new[] { "A" },
                }));
        }

        [Fact]
        public void Shared_extractor_candidate_is_not_mistaken_for_a_cycle()
        {
            Assert.True(GuaranteesExtractorArea(
                root: "Root",
                extractorNodes: new[] { "Extractor" },
                placeholders: new Dictionary<string, string[]>
                {
                    ["Root"] = new[] { "Left", "Right" },
                    ["Left"] = new[] { "Extractor" },
                    ["Right"] = new[] { "Extractor" },
                }));
        }

        private static bool GuaranteesExtractorArea(
            string root,
            IReadOnlyCollection<string> extractorNodes,
            IReadOnlyDictionary<string, string[]> placeholders)
        {
            return PrefabOperationalAreaGraph.GuaranteesExtractorArea(
                root,
                extractorNodes.Contains,
                node => placeholders.TryGetValue(node, out string[] candidates)
                    ? candidates
                    : Array.Empty<string>());
        }
    }
}
