using System.Text.Json;
using CitiesSkylines2Agent.Agent;
using Xunit;

namespace CitiesSkylines2Agent.Agent.Tests
{
    public sealed class WaitSimulationDigestTests
    {
        private const string WaitJson =
            "{\"running\":true,\"hours\":4,\"speed\":8,\"restoreSpeed\":0,\"startFrame\":10,\"targetFrame\":100," +
            "\"note\":\"simulation runs until exactly the requested in-game hours have passed\"}";
        private const string OverviewJson =
            "{\"cityName\":\"Handoff\",\"population\":12800,\"populationWithMoveIn\":13000," +
            "\"averageHappiness\":50,\"averageHealth\":60,\"money\":123456,\"xp\":8300," +
            "\"gameYear\":2026,\"gameDateTime\":\"2026-08-16 02:00\",\"simulationPaused\":false," +
            "\"simulationSpeed\":1,\"note\":\"omit me\"}";
        private const string NotificationsJson =
            "{\"countsByType\":{\"Sewage Notification\":3,\"Traffic Bottleneck Notification\":1}," +
            "\"notifications\":[{\"type\":\"Sewage Notification\",\"x\":10,\"z\":20,\"priority\":4}]," +
            "\"topIssues\":[{\"type\":\"Sewage Notification\",\"count\":3}]}";
        private const string ServicesJson =
            "{\"problems\":[{\"id\":\"sewage\",\"severity\":\"critical\",\"message\":\"no capacity\",\"extra\":\"drop\"}]," +
            "\"electricity\":{\"production\":1}}";
        private const string StateReached = "{\"simulation\":{\"frameIndex\":100}}";
        private const string StateShort = "{\"simulation\":{\"frameIndex\":99}}";

        [Fact]
        public void Nests_overview_and_problems_without_flattening()
        {
            using (JsonDocument document = Parse(true, StateReached))
            {
                JsonElement root = document.RootElement;
                Assert.Equal(4, root.GetProperty("hours").GetInt32());
                Assert.True(root.GetProperty("completed").GetBoolean());
                Assert.True(root.GetProperty("targetReached").GetBoolean());
                Assert.Equal(
                    "wait finished; simulation restored to its previous speed/pause state",
                    root.GetProperty("note").GetString());

                JsonElement overview = root.GetProperty("overview");
                Assert.Equal("Handoff", overview.GetProperty("cityName").GetString());
                Assert.Equal(12800, overview.GetProperty("population").GetInt32());
                Assert.Equal(13000, overview.GetProperty("populationWithMoveIn").GetInt32());
                Assert.Equal(50, overview.GetProperty("averageHappiness").GetInt32());
                Assert.Equal(60, overview.GetProperty("averageHealth").GetInt32());
                Assert.Equal(123456, overview.GetProperty("money").GetInt32());
                Assert.Equal(8300, overview.GetProperty("xp").GetInt32());
                Assert.Equal(2026, overview.GetProperty("gameYear").GetInt32());
                Assert.Equal("2026-08-16 02:00", overview.GetProperty("gameDateTime").GetString());
                Assert.False(overview.GetProperty("simulationPaused").GetBoolean());
                Assert.Equal(1, overview.GetProperty("simulationSpeed").GetInt32());
                Assert.False(overview.TryGetProperty("note", out _));
                Assert.False(root.TryGetProperty("population", out _));
                Assert.False(root.TryGetProperty("cityName", out _));
            }
        }

        [Fact]
        public void Strips_sim_wait_internals_and_waitedMs()
        {
            using (JsonDocument document = Parse(true, StateReached))
            {
                JsonElement root = document.RootElement;
                Assert.False(root.TryGetProperty("running", out _));
                Assert.False(root.TryGetProperty("speed", out _));
                Assert.False(root.TryGetProperty("restoreSpeed", out _));
                Assert.False(root.TryGetProperty("startFrame", out _));
                Assert.False(root.TryGetProperty("targetFrame", out _));
                Assert.False(root.TryGetProperty("waitedMs", out _));
            }
        }

        [Theory]
        [InlineData(false, true, "wait did not finish in time; retry wait_simulation once")]
        [InlineData(true, false, "wait aborted: simulation did not advance (game paused or a modal overlay is open)")]
        [InlineData(true, true, "wait finished; simulation restored to its previous speed/pause state")]
        public void Uses_the_three_stable_notes(bool completed, bool reached, string note)
        {
            using (JsonDocument document = Parse(completed, reached ? StateReached : StateShort))
            {
                Assert.Equal(note, document.RootElement.GetProperty("note").GetString());
                Assert.Equal(completed, document.RootElement.GetProperty("completed").GetBoolean());
                Assert.Equal(reached, document.RootElement.GetProperty("targetReached").GetBoolean());
            }
        }

        [Fact]
        public void Notification_counts_are_citywide_countsByType_without_icon_coords()
        {
            using (JsonDocument document = Parse(true, StateReached))
            {
                JsonElement problems = document.RootElement.GetProperty("problems");
                JsonElement counts = problems.GetProperty("notificationCounts");
                Assert.Equal(3, counts.GetProperty("Sewage Notification").GetInt32());
                Assert.Equal(1, counts.GetProperty("Traffic Bottleneck Notification").GetInt32());
                foreach (JsonProperty property in problems.EnumerateObject())
                {
                    Assert.True(
                        property.Name == "notificationCounts" || property.Name == "serviceGaps",
                        property.Name);
                }
                Assert.False(counts.TryGetProperty("x", out _));
            }
        }

        [Fact]
        public void Service_gaps_keep_id_severity_message_only()
        {
            using (JsonDocument document = Parse(true, StateReached))
            {
                JsonElement gap = Assert.Single(document.RootElement.GetProperty("problems").GetProperty("serviceGaps").EnumerateArray());
                Assert.Equal("sewage", gap.GetProperty("id").GetString());
                Assert.Equal("critical", gap.GetProperty("severity").GetString());
                Assert.Equal("no capacity", gap.GetProperty("message").GetString());
                Assert.False(gap.TryGetProperty("extra", out _));
                Assert.False(gap.TryGetProperty("lifecycle", out _));
            }
        }

        [Fact]
        public void Missing_or_invalid_json_yields_empty_overview_and_problems()
        {
            string json = WaitSimulationDigest.Build("{", "not-json", null, "", "[]", true);
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                JsonElement root = document.RootElement;
                Assert.Equal(1, root.GetProperty("hours").GetInt32());
                Assert.True(root.GetProperty("completed").GetBoolean());
                Assert.False(root.GetProperty("targetReached").GetBoolean());
                Assert.Equal(0, root.GetProperty("overview").GetPropertyCount());
                Assert.Equal(0, root.GetProperty("problems").GetProperty("notificationCounts").GetPropertyCount());
                Assert.Equal(0, root.GetProperty("problems").GetProperty("serviceGaps").GetArrayLength());
            }
        }

        [Fact]
        public void Does_not_emit_lifecycle_or_relative_time()
        {
            using (JsonDocument document = Parse(true, StateReached))
            {
                string json = document.RootElement.GetRawText();
                Assert.DoesNotContain("lifecycle", json);
                Assert.DoesNotContain("just now", json);
                Assert.DoesNotContain("firstSeen", json);
            }
        }

        private static JsonDocument Parse(bool completed, string stateJson)
        {
            return JsonDocument.Parse(
                WaitSimulationDigest.Build(
                    WaitJson,
                    OverviewJson,
                    NotificationsJson,
                    ServicesJson,
                    stateJson,
                    completed));
        }
    }
}
