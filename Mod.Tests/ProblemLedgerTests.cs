using System;
using System.Linq;
using CitiesSkylines2Agent.Agent;
using Xunit;

namespace CitiesSkylines2Agent.Agent.Tests
{
    public sealed class ProblemLedgerTests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 8, 14, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Identity_is_service_id_or_aggregated_icon_type()
        {
            var ledger = new ProblemLedger();
            ledger.Merge(Icons(("Sewage Notification", 10)), Services(("sewage", "critical", "no capacity")), T0);

            Assert.Equal(2, ledger.Records.Count);
            Assert.Contains(ledger.Records, record => record.Identity == "service/sewage");
            Assert.Contains(ledger.Records, record => record.Identity == "icon/Sewage Notification");
            Assert.Equal(10, Record(ledger, "icon/Sewage Notification").Count);
        }

        [Fact]
        public void Duplicate_icons_of_the_same_type_are_one_record()
        {
            var ledger = new ProblemLedger();
            ledger.Merge(
                "{\"countsByType\":{\"No Electricity\":3},\"notifications\":[" +
                "{\"type\":\"No Electricity\",\"priority\":1}," +
                "{\"type\":\"No Electricity\",\"priority\":4}," +
                "{\"type\":\"No Electricity\",\"priority\":2}]}",
                "{}",
                T0);

            ProblemRecord record = Assert.Single(ledger.Records);
            Assert.Equal("icon/No Electricity", record.Identity);
            Assert.Equal(3, record.Count);
            Assert.Equal(4, record.SeverityRank);
            Assert.Equal(ProblemLifecycle.New, record.Lifecycle);
        }

        [Fact]
        public void Lifecycle_is_new_active_escalated_resolved_then_dropped()
        {
            var ledger = new ProblemLedger();
            ledger.Merge(Icons(("Traffic Bottleneck Notification", 2)), "{}", T0);
            Assert.Equal(ProblemLifecycle.New, Single(ledger).Lifecycle);

            ledger.Merge(Icons(("Traffic Bottleneck Notification", 2)), "{}", T0.AddMinutes(5));
            Assert.Equal(ProblemLifecycle.Active, Single(ledger).Lifecycle);

            ledger.Merge(Icons(("Traffic Bottleneck Notification", 5)), "{}", T0.AddMinutes(10));
            Assert.Equal(ProblemLifecycle.Escalated, Single(ledger).Lifecycle);
            Assert.Equal(5, Single(ledger).Count);

            ledger.Merge("{\"countsByType\":{}}", "{}", T0.AddMinutes(15));
            Assert.Equal(ProblemLifecycle.Resolved, Single(ledger).Lifecycle);

            ledger.Merge("{\"countsByType\":{}}", "{}", T0.AddMinutes(20));
            Assert.Empty(ledger.Records);
        }

        [Fact]
        public void Resolved_problem_that_returns_is_new()
        {
            var ledger = new ProblemLedger();
            ledger.Merge(Icons(("GarbagePilingUp", 1)), "{}", T0);
            ledger.Merge("{\"countsByType\":{}}", "{}", T0.AddMinutes(1));
            ledger.Merge(Icons(("GarbagePilingUp", 1)), "{}", T0.AddMinutes(2));

            ProblemRecord record = Single(ledger);
            Assert.Equal(ProblemLifecycle.New, record.Lifecycle);
            Assert.Equal(T0.AddMinutes(2), record.FirstSeen);
        }

        [Fact]
        public void Service_severity_increase_escalates()
        {
            var ledger = new ProblemLedger();
            ledger.Merge("{}", Services(("electricity", "warning", "tight")), T0);
            ledger.Merge("{}", Services(("electricity", "high", "short")), T0.AddMinutes(3));

            ProblemRecord record = Single(ledger);
            Assert.Equal("service/electricity", record.Identity);
            Assert.Equal(ProblemLifecycle.Escalated, record.Lifecycle);
            Assert.Equal("high", record.Severity);
        }

        [Fact]
        public void Failed_source_does_not_resolve_the_other()
        {
            var ledger = new ProblemLedger();
            ledger.Merge(Icons(("Sewage Notification", 4)), Services(("water", "high", "low pressure")), T0);
            ledger.Merge(Icons(("Sewage Notification", 4)), Services(("water", "high", "low pressure")), T0.AddMinutes(1));
            ledger.Merge(null, "{}", T0.AddMinutes(2));

            Assert.Equal(ProblemLifecycle.Active, Record(ledger, "icon/Sewage Notification").Lifecycle);
            Assert.Equal(ProblemLifecycle.Resolved, Record(ledger, "service/water").Lifecycle);
        }

        [Fact]
        public void Invalid_json_leaves_the_ledger_unchanged()
        {
            var ledger = new ProblemLedger();
            ledger.Merge(Icons(("Sewage Notification", 1)), Services(("sewage", "critical", "none")), T0);
            ledger.Merge("{", "not-json", T0.AddMinutes(1));

            Assert.Equal(2, ledger.Records.Count);
            Assert.All(ledger.Records, record => Assert.Equal(ProblemLifecycle.New, record.Lifecycle));
        }

        [Fact]
        public void Unknown_service_ids_are_ignored()
        {
            var ledger = new ProblemLedger();
            ledger.Merge("{}", Services(("garbage", "critical", "piling"), ("sewage", "high", "over")), T0);

            ProblemRecord record = Assert.Single(ledger.Records);
            Assert.Equal("service/sewage", record.Identity);
        }

        [Fact]
        public void Clear_drops_every_record()
        {
            var ledger = new ProblemLedger();
            ledger.Merge(Icons(("No Water", 1)), "{}", T0);
            ledger.Clear();
            Assert.Empty(ledger.Records);
            Assert.Equal("", ledger.Render(T0));
        }

        [Fact]
        public void Render_is_compact_and_orders_escalated_first()
        {
            var ledger = new ProblemLedger();
            ledger.Merge(
                Icons(("No Water", 1), ("Traffic Bottleneck Notification", 2)),
                Services(("sewage", "warning", "catching up")),
                T0);
            ledger.Merge(
                Icons(("No Water", 1), ("Traffic Bottleneck Notification", 6)),
                "{}",
                T0.AddMinutes(12));

            string text = ledger.Render(T0.AddMinutes(12));
            Assert.Contains("ESCALATED icon/Traffic Bottleneck Notification x6 — 12m", text);
            Assert.Contains("ACTIVE icon/No Water x1 — 12m", text);
            Assert.Contains("RESOLVED service/sewage — 12m", text);
            Assert.DoesNotContain("notifications", text);
            int escalatedAt = text.IndexOf("ESCALATED", StringComparison.Ordinal);
            int resolvedAt = text.IndexOf("RESOLVED", StringComparison.Ordinal);
            Assert.True(escalatedAt >= 0 && resolvedAt > escalatedAt);
        }

        [Fact]
        public void Duration_uses_first_seen()
        {
            Assert.Equal("just now", ProblemLedger.FormatDuration(T0, T0.AddSeconds(59)));
            Assert.Equal("12m", ProblemLedger.FormatDuration(T0, T0.AddMinutes(12)));
            Assert.Equal("3h", ProblemLedger.FormatDuration(T0, T0.AddHours(3)));
            Assert.Equal("2d", ProblemLedger.FormatDuration(T0, T0.AddDays(2)));
        }

        private static ProblemRecord Single(ProblemLedger ledger)
        {
            return Assert.Single(ledger.Records);
        }

        private static ProblemRecord Record(ProblemLedger ledger, string identity)
        {
            return ledger.Records.Single(record => record.Identity == identity);
        }

        private static string Icons(params (string type, int count)[] types)
        {
            string body = string.Join(",", types.Select(item => "\"" + item.type + "\":" + item.count));
            return "{\"countsByType\":{" + body + "}}";
        }

        private static string Services(params (string id, string severity, string message)[] problems)
        {
            string body = string.Join(",", problems.Select(item =>
                "{\"id\":\"" + item.id + "\",\"severity\":\"" + item.severity + "\",\"message\":\"" + item.message + "\"}"));
            return "{\"problems\":[" + body + "]}";
        }
    }
}
