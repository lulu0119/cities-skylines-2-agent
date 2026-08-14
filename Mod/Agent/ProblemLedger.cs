using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CitiesSkylines2Agent.Agent
{
    internal enum ProblemLifecycle
    {
        New,
        Active,
        Escalated,
        Resolved,
    }

    /// <summary>
    /// One deduped city problem. Identity is <c>service/{id}</c> for
    /// sewage/water/electricity gaps or <c>icon/{type}</c> for aggregated
    /// notification icons.
    /// </summary>
    internal readonly struct ProblemRecord
    {
        public ProblemRecord(
            string identity,
            string source,
            ProblemLifecycle lifecycle,
            DateTimeOffset firstSeen,
            DateTimeOffset lastSeen,
            int count,
            int severityRank,
            string severity,
            string detail)
        {
            Identity = identity;
            Source = source;
            Lifecycle = lifecycle;
            FirstSeen = firstSeen;
            LastSeen = lastSeen;
            Count = count;
            SeverityRank = severityRank;
            Severity = severity;
            Detail = detail;
        }

        public string Identity { get; }
        public string Source { get; }
        public ProblemLifecycle Lifecycle { get; }
        public DateTimeOffset FirstSeen { get; }
        public DateTimeOffset LastSeen { get; }
        public int Count { get; }
        public int SeverityRank { get; }
        public string Severity { get; }
        public string Detail { get; }
    }

    /// <summary>
    /// Snapshot merge of <c>notifications</c> (ECS icons) and
    /// <c>city_services.problems[]</c>. Callers pass JSON and a clock; they
    /// never need ECS. A failed or omitted source is left unchanged so a read
    /// error cannot mark the city clear. Resolved entries are injected once,
    /// then dropped. A new city session starts an empty ledger — do not wake
    /// an idle Agent from this module.
    /// </summary>
    internal sealed class ProblemLedger
    {
        private const string ServiceSource = "service";
        private const string IconSource = "icon";
        private const int MaxRenderLines = 20;

        private static readonly HashSet<string> ServiceIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "sewage", "water", "electricity",
            };

        private readonly List<ProblemRecord> m_Records = new List<ProblemRecord>();

        public IReadOnlyList<ProblemRecord> Records => m_Records;

        public void Clear()
        {
            m_Records.Clear();
        }

        /// <summary>
        /// Merge one or both snapshots. Pass null JSON to leave that source
        /// untouched. Invalid JSON is treated the same as a failed read.
        /// </summary>
        public void Merge(string notificationsJson, string servicesJson, DateTimeOffset now)
        {
            bool updateIcons = TryParseNotifications(notificationsJson, out List<ProblemObservation> icons);
            bool updateServices = TryParseServices(servicesJson, out List<ProblemObservation> services);
            if (!updateIcons && !updateServices)
            {
                return;
            }

            var observed = new List<ProblemObservation>();
            if (updateIcons)
            {
                observed.AddRange(icons);
            }
            if (updateServices)
            {
                observed.AddRange(services);
            }
            List<ProblemRecord> merged = Merge(m_Records, observed, now, updateIcons, updateServices);
            m_Records.Clear();
            m_Records.AddRange(merged);
        }

        public string Render(DateTimeOffset now)
        {
            return Render(m_Records, now);
        }

        private static List<ProblemRecord> Merge(
            IReadOnlyList<ProblemRecord> previous,
            IReadOnlyList<ProblemObservation> observed,
            DateTimeOffset now,
            bool updateIcons,
            bool updateServices)
        {
            var previousById = new Dictionary<string, ProblemRecord>(StringComparer.Ordinal);
            if (previous != null)
            {
                foreach (ProblemRecord record in previous)
                {
                    if (!string.IsNullOrEmpty(record.Identity))
                    {
                        previousById[record.Identity] = record;
                    }
                }
            }

            var next = new List<ProblemRecord>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (observed != null)
            {
                foreach (ProblemObservation item in observed)
                {
                    if (string.IsNullOrEmpty(item.Identity) || !seen.Add(item.Identity))
                    {
                        continue;
                    }

                    DateTimeOffset firstSeen = now;
                    ProblemLifecycle lifecycle = ProblemLifecycle.New;
                    if (previousById.TryGetValue(item.Identity, out ProblemRecord existing)
                        && existing.Lifecycle != ProblemLifecycle.Resolved)
                    {
                        firstSeen = existing.FirstSeen;
                        if (item.Count > existing.Count || item.SeverityRank > existing.SeverityRank)
                        {
                            lifecycle = ProblemLifecycle.Escalated;
                        }
                        else
                        {
                            lifecycle = ProblemLifecycle.Active;
                        }
                    }

                    next.Add(new ProblemRecord(
                        item.Identity,
                        item.Source,
                        lifecycle,
                        firstSeen,
                        now,
                        item.Count,
                        item.SeverityRank,
                        item.Severity,
                        item.Detail));
                }
            }

            if (previous != null)
            {
                foreach (ProblemRecord existing in previous)
                {
                    if (string.IsNullOrEmpty(existing.Identity) || seen.Contains(existing.Identity))
                    {
                        continue;
                    }
                    if (!SourceUpdated(existing.Source, updateIcons, updateServices))
                    {
                        next.Add(existing);
                        continue;
                    }
                    if (existing.Lifecycle == ProblemLifecycle.Resolved)
                    {
                        continue;
                    }
                    next.Add(new ProblemRecord(
                        existing.Identity,
                        existing.Source,
                        ProblemLifecycle.Resolved,
                        existing.FirstSeen,
                        now,
                        existing.Count,
                        existing.SeverityRank,
                        existing.Severity,
                        existing.Detail));
                }
            }

            return next;
        }

        private static bool TryParseNotifications(string json, out List<ProblemObservation> observations)
        {
            observations = new List<ProblemObservation>();
            if (json == null)
            {
                return false;
            }

            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }

                    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                    var maxPriority = new Dictionary<string, int>(StringComparer.Ordinal);
                    bool hasCountsByType = root.TryGetProperty("countsByType", out JsonElement countsByType)
                        && countsByType.ValueKind == JsonValueKind.Object;
                    if (hasCountsByType)
                    {
                        foreach (JsonProperty property in countsByType.EnumerateObject())
                        {
                            int count = ReadCount(property.Value);
                            if (count > 0 && !string.IsNullOrEmpty(property.Name))
                            {
                                counts[property.Name] = count;
                            }
                        }
                    }

                    if (root.TryGetProperty("notifications", out JsonElement details)
                        && details.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement item in details.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.Object)
                            {
                                continue;
                            }
                            string type = ReadString(item, "type");
                            if (string.IsNullOrEmpty(type))
                            {
                                continue;
                            }
                            if (!hasCountsByType)
                            {
                                counts[type] = counts.TryGetValue(type, out int count) ? count + 1 : 1;
                            }
                            int priority = ReadCount(item, "priority");
                            if (!maxPriority.TryGetValue(type, out int current) || priority > current)
                            {
                                maxPriority[type] = priority;
                            }
                        }
                    }

                    foreach (KeyValuePair<string, int> pair in counts)
                    {
                        maxPriority.TryGetValue(pair.Key, out int priority);
                        observations.Add(new ProblemObservation(
                            IconSource + "/" + pair.Key,
                            IconSource,
                            pair.Value,
                            priority,
                            null,
                            null));
                    }
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool TryParseServices(string json, out List<ProblemObservation> observations)
        {
            observations = new List<ProblemObservation>();
            if (json == null)
            {
                return false;
            }

            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }

                    if (!root.TryGetProperty("problems", out JsonElement problems))
                    {
                        return true;
                    }
                    if (problems.ValueKind != JsonValueKind.Array)
                    {
                        return false;
                    }

                    var byId = new Dictionary<string, ProblemObservation>(StringComparer.Ordinal);
                    foreach (JsonElement item in problems.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }
                        string id = ReadString(item, "id");
                        if (string.IsNullOrEmpty(id) || !ServiceIds.Contains(id))
                        {
                            continue;
                        }
                        string normalized = id.ToLowerInvariant();
                        string severity = ReadString(item, "severity");
                        int rank = SeverityRank(severity);
                        string detail = ReadString(item, "message");
                        var observation = new ProblemObservation(
                            ServiceSource + "/" + normalized,
                            ServiceSource,
                            1,
                            rank,
                            string.IsNullOrEmpty(severity) ? null : severity.ToLowerInvariant(),
                            detail);
                        if (!byId.TryGetValue(observation.Identity, out ProblemObservation existing)
                            || observation.SeverityRank > existing.SeverityRank)
                        {
                            byId[observation.Identity] = observation;
                        }
                    }
                    observations.AddRange(byId.Values);
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string Render(IReadOnlyList<ProblemRecord> records, DateTimeOffset now)
        {
            if (records == null || records.Count == 0)
            {
                return "";
            }

            var ordered = new List<ProblemRecord>(records);
            ordered.Sort(CompareForRender);
            int shown = Math.Min(MaxRenderLines, ordered.Count);
            var builder = new StringBuilder();
            for (int index = 0; index < shown; index++)
            {
                if (index > 0)
                {
                    builder.Append('\n');
                }
                builder.Append(FormatLine(ordered[index], now));
            }
            int hidden = ordered.Count - shown;
            if (hidden > 0)
            {
                builder.Append("\n+").Append(hidden).Append(" more");
            }
            return builder.ToString();
        }

        private readonly struct ProblemObservation
        {
            public ProblemObservation(
                string identity,
                string source,
                int count,
                int severityRank,
                string severity,
                string detail)
            {
                Identity = identity;
                Source = source;
                Count = count;
                SeverityRank = severityRank;
                Severity = severity;
                Detail = detail;
            }

            public string Identity { get; }
            public string Source { get; }
            public int Count { get; }
            public int SeverityRank { get; }
            public string Severity { get; }
            public string Detail { get; }
        }

        private static bool SourceUpdated(string source, bool updateIcons, bool updateServices)
        {
            if (string.Equals(source, IconSource, StringComparison.Ordinal))
            {
                return updateIcons;
            }
            if (string.Equals(source, ServiceSource, StringComparison.Ordinal))
            {
                return updateServices;
            }
            return updateIcons && updateServices;
        }

        private static int CompareForRender(ProblemRecord left, ProblemRecord right)
        {
            int lifecycle = LifecycleRank(left.Lifecycle).CompareTo(LifecycleRank(right.Lifecycle));
            if (lifecycle != 0)
            {
                return lifecycle;
            }
            int severity = right.SeverityRank.CompareTo(left.SeverityRank);
            if (severity != 0)
            {
                return severity;
            }
            int count = right.Count.CompareTo(left.Count);
            if (count != 0)
            {
                return count;
            }
            return string.Compare(left.Identity, right.Identity, StringComparison.Ordinal);
        }

        private static int LifecycleRank(ProblemLifecycle lifecycle)
        {
            switch (lifecycle)
            {
                case ProblemLifecycle.Escalated: return 0;
                case ProblemLifecycle.New: return 1;
                case ProblemLifecycle.Active: return 2;
                default: return 3;
            }
        }

        private static string FormatLine(ProblemRecord record, DateTimeOffset now)
        {
            var builder = new StringBuilder();
            builder.Append(LifecycleLabel(record.Lifecycle)).Append(' ').Append(record.Identity);
            if (string.Equals(record.Source, IconSource, StringComparison.Ordinal) && record.Count > 0)
            {
                builder.Append(" x").Append(record.Count);
            }
            if (record.Lifecycle != ProblemLifecycle.Resolved && !string.IsNullOrEmpty(record.Severity))
            {
                builder.Append(' ').Append(record.Severity);
            }
            builder.Append(" — ").Append(FormatDuration(record.FirstSeen, now));
            if (!string.IsNullOrEmpty(record.Detail) && record.Lifecycle != ProblemLifecycle.Resolved)
            {
                builder.Append(" — ").Append(Truncate(record.Detail, 160));
            }
            return builder.ToString();
        }

        private static string LifecycleLabel(ProblemLifecycle lifecycle)
        {
            switch (lifecycle)
            {
                case ProblemLifecycle.New: return "NEW";
                case ProblemLifecycle.Active: return "ACTIVE";
                case ProblemLifecycle.Escalated: return "ESCALATED";
                default: return "RESOLVED";
            }
        }

        internal static string FormatDuration(DateTimeOffset from, DateTimeOffset to)
        {
            TimeSpan span = to - from;
            if (span.Ticks < 0)
            {
                span = TimeSpan.Zero;
            }
            if (span.TotalSeconds < 60)
            {
                return "just now";
            }
            if (span.TotalMinutes < 60)
            {
                return ((int)span.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m";
            }
            if (span.TotalHours < 48)
            {
                return ((int)span.TotalHours).ToString(CultureInfo.InvariantCulture) + "h";
            }
            return ((int)span.TotalDays).ToString(CultureInfo.InvariantCulture) + "d";
        }

        private static int SeverityRank(string severity)
        {
            if (string.Equals(severity, "critical", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }
            if (string.Equals(severity, "high", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }
            if (string.Equals(severity, "warning", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }
            return 0;
        }

        private static string ReadString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value)
                || value.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            return value.GetString();
        }

        private static int ReadCount(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
            {
                return 0;
            }
            return ReadCount(value);
        }

        private static int ReadCount(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Number)
            {
                return 0;
            }
            if (value.TryGetInt32(out int count))
            {
                return count;
            }
            if (value.TryGetInt64(out long large) && large > 0 && large <= int.MaxValue)
            {
                return (int)large;
            }
            return 0;
        }

        private static string Truncate(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            {
                return text ?? "";
            }
            return text.Substring(0, maxChars) + "…";
        }
    }
}
