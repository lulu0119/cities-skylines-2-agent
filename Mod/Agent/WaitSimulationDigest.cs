using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>
    /// Builds the model-facing wait_simulation result from fetched JSON.
    /// Callers do I/O; this module does not talk to the bridge.
    /// </summary>
    internal static class WaitSimulationDigest
    {
        private const string NoteTimeout =
            "wait did not finish in time; retry wait_simulation once";
        private const string NoteAborted =
            "wait aborted: simulation did not advance (game paused or a modal overlay is open)";
        private const string NoteFinished =
            "wait finished; simulation restored to its previous speed/pause state";

        private static readonly string[] OverviewFields =
        {
            "cityName",
            "population",
            "populationWithMoveIn",
            "averageHappiness",
            "averageHealth",
            "money",
            "xp",
            "gameYear",
            "gameDateTime",
            "simulationPaused",
            "simulationSpeed",
        };

        public static string Build(
            string waitJson,
            string overviewJson,
            string notificationsJson,
            string servicesJson,
            string stateJson,
            bool completed)
        {
            JsonObject waitRoot = ParseObject(waitJson);
            bool targetReached = TargetReached(waitRoot, ParseObject(stateJson));
            var result = new JsonObject
            {
                ["hours"] = CopyHours(waitRoot),
                ["completed"] = completed,
                ["targetReached"] = targetReached,
                ["note"] = Note(completed, targetReached),
                ["overview"] = Overview(ParseObject(overviewJson)),
                ["problems"] = new JsonObject
                {
                    ["notificationCounts"] = NotificationCounts(ParseObject(notificationsJson)),
                    ["serviceGaps"] = ServiceGaps(ParseObject(servicesJson)),
                },
            };
            return result.ToJsonString();
        }

        private static JsonNode CopyHours(JsonObject waitRoot)
        {
            JsonNode hours = waitRoot != null ? waitRoot["hours"] : null;
            return hours != null ? hours.DeepClone() : JsonValue.Create(1);
        }

        private static bool TargetReached(JsonObject waitRoot, JsonObject stateRoot)
        {
            JsonNode targetNode = waitRoot != null ? waitRoot["targetFrame"] : null;
            JsonNode frameNode = stateRoot != null ? stateRoot["simulation"]?["frameIndex"] : null;
            if (targetNode == null || frameNode == null)
            {
                return false;
            }
            if (!long.TryParse(
                    targetNode.ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long targetFrame))
            {
                return false;
            }
            return long.TryParse(
                    frameNode.ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long finalFrame) &&
                finalFrame >= targetFrame;
        }

        private static string Note(bool completed, bool targetReached)
        {
            if (!completed)
            {
                return NoteTimeout;
            }
            return targetReached ? NoteFinished : NoteAborted;
        }

        private static JsonObject Overview(JsonObject source)
        {
            var overview = new JsonObject();
            if (source == null)
            {
                return overview;
            }
            foreach (string key in OverviewFields)
            {
                JsonNode value = source[key];
                if (value != null)
                {
                    overview[key] = value.DeepClone();
                }
            }
            return overview;
        }

        private static JsonNode NotificationCounts(JsonObject source)
        {
            JsonObject counts = source != null ? source["countsByType"] as JsonObject : null;
            return counts != null ? counts.DeepClone() : new JsonObject();
        }

        private static JsonArray ServiceGaps(JsonObject source)
        {
            var gaps = new JsonArray();
            JsonArray problems = source != null ? source["problems"] as JsonArray : null;
            if (problems == null)
            {
                return gaps;
            }
            foreach (JsonNode item in problems)
            {
                JsonObject problem = item as JsonObject;
                if (problem == null)
                {
                    continue;
                }
                string id = ReadString(problem, "id");
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }
                var gap = new JsonObject { ["id"] = id };
                JsonNode severity = problem["severity"];
                if (severity != null)
                {
                    gap["severity"] = severity.DeepClone();
                }
                JsonNode message = problem["message"];
                if (message != null)
                {
                    gap["message"] = message.DeepClone();
                }
                gaps.Add(gap);
            }
            return gaps;
        }

        private static string ReadString(JsonObject obj, string name)
        {
            JsonValue value = obj[name] as JsonValue;
            string text;
            if (value != null && value.TryGetValue(out text))
            {
                return text;
            }
            return null;
        }

        private static JsonObject ParseObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                return JsonNode.Parse(json) as JsonObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
