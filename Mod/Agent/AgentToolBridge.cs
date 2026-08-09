using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>Result of one in-process tool invocation.</summary>
    public sealed class ToolInvocationResult
    {
        public bool Success;
        public string Text;       // JSON text or error message
        public string ImagePath;  // screenshot path when the tool returned PNG
    }

    /// <summary>
    /// Translates model tool calls into CS2MCP bridge requests (in-process),
    /// builds query strings from the catalog. Writes are not gated on pausing:
    /// the game validates construction while the simulation runs. Short
    /// wait_simulation calls wait on the agent thread until the timed run
    /// finishes, so the model does not busy-poll game_state.
    /// </summary>
    public static class AgentToolBridge
    {
        private const int SimWaitPollMs = 250;

        public static async Task<ToolInvocationResult> InvokeAsync(
            ToolDefinition tool,
            string argumentsJson,
            CancellationToken cancellationToken)
        {
            CS2MCP.BridgeSystem bridge = CS2MCP.BridgeSystem.Instance;
            if (bridge == null)
            {
                return Error("bridge system not available (game still loading?)");
            }

            Dictionary<string, string> query;
            try
            {
                query = BuildQuery(tool, argumentsJson);
            }
            catch (Exception e)
            {
                return Error($"invalid arguments for {tool.Name}: {AgentObservability.RedactSecrets(e.Message)}");
            }

            CS2MCP.BridgeResponse response = await bridge.InvokeAsync(tool.Route, query);
            if (response == null)
            {
                return Error("bridge returned no response");
            }
            if (response.Status != 200)
            {
                string body = Encoding.UTF8.GetString(response.Body ?? Array.Empty<byte>());
                return Error(body);
            }
            if (string.Equals(tool.Response, "png", StringComparison.Ordinal))
            {
                string path = SaveScreenshot(response.Body);
                return new ToolInvocationResult
                {
                    Success = true,
                    ImagePath = path,
                    Text = "{\"saved\":\"" + JsonEncodedText.Encode(path).ToString() + "\"}",
                };
            }

            string text = Encoding.UTF8.GetString(response.Body ?? Array.Empty<byte>());
            if (string.Equals(tool.Route, "/sim/wait", StringComparison.Ordinal))
            {
                text = await WaitForWaitAsync(bridge, text, query, cancellationToken);
            }
            return new ToolInvocationResult
            {
                Success = true,
                Text = text,
            };
        }

        /// <summary>
        /// Waits on the agent thread until BridgeSystem clears
        /// AutoPauseTargetFrame (bounded by the requested seconds plus a small
        /// margin), then appends a final /state snapshot so the model need not
        /// poll.
        /// </summary>
        private static async Task<string> WaitForWaitAsync(
            CS2MCP.BridgeSystem bridge,
            string startJson,
            Dictionary<string, string> query,
            CancellationToken cancellationToken)
        {
            int requestedSeconds = 0;
            if (query.TryGetValue("seconds", out string rawSeconds))
            {
                int.TryParse(
                    rawSeconds,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out requestedSeconds);
            }
            int maxWaitMs = Math.Max(1, requestedSeconds) * 1000 + SimWaitPollMs * 4;
            int waited = 0;
            while (bridge.AutoPauseTargetFrame != 0 && waited < maxWaitMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(SimWaitPollMs, cancellationToken);
                waited += SimWaitPollMs;
            }

            string finalState = null;
            try
            {
                CS2MCP.BridgeResponse state = await bridge.InvokeAsync("/state");
                if (state != null && state.Status == 200)
                {
                    finalState = Encoding.UTF8.GetString(state.Body ?? Array.Empty<byte>());
                }
            }
            catch (Exception e)
            {
                CS2MCP.Mod.Log.Warn($"post-wait state failed: {AgentObservability.RedactSecrets(e.Message)}");
            }

            try
            {
                JsonNode root = JsonNode.Parse(string.IsNullOrWhiteSpace(startJson) ? "{}" : startJson)
                    ?? new JsonObject();
                root["completed"] = bridge.AutoPauseTargetFrame == 0;
                root["waitedMs"] = waited;
                if (finalState != null)
                {
                    root["finalState"] = JsonNode.Parse(finalState);
                }
                if (bridge.AutoPauseTargetFrame != 0)
                {
                    root["note"] = "wait did not finish in time; check game_state once";
                }
                else
                {
                    root["note"] = "wait finished; simulation restored to its previous speed/pause state";
                }
                return root.ToJsonString();
            }
            catch
            {
                return startJson;
            }
        }

        private static ToolInvocationResult Error(string message)
        {
            string json = JsonSerializer.Serialize(new { error = message });
            return new ToolInvocationResult { Success = false, Text = json };
        }

        private static string SaveScreenshot(byte[] png)
        {
            ModPaths.EnsureDirectories();
            string path = Path.Combine(
                ModPaths.ScreenshotsDirectory,
                "shot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".png");
            File.WriteAllBytes(path, png);
            return path;
        }

        private static Dictionary<string, string> BuildQuery(ToolDefinition tool, string argumentsJson)
        {
            var query = new Dictionary<string, string>(StringComparer.Ordinal);
            using (JsonDocument document = string.IsNullOrWhiteSpace(argumentsJson)
                ? JsonDocument.Parse("{}")
                : JsonDocument.Parse(argumentsJson))
            {
                JsonElement args = document.RootElement;
                foreach (ToolQuerySpec spec in tool.Query)
                {
                    if (spec.Literal != null)
                    {
                        query[spec.Key] = spec.Literal;
                        continue;
                    }
                    if (spec.Arg == null)
                    {
                        continue;
                    }
                    if (!args.TryGetProperty(spec.Arg, out JsonElement value))
                    {
                        if (spec.Default != null)
                        {
                            query[spec.Key] = spec.Default;
                        }
                        continue;
                    }
                    if (spec.BoolMode == "trueOnly" && value.ValueKind == JsonValueKind.False)
                    {
                        continue;
                    }
                    query[spec.Key] = JsonValueToString(value);
                }
            }
            return query;
        }

        private static string JsonValueToString(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return value.GetString();
                case JsonValueKind.Number:
                    return value.GetRawText();
                case JsonValueKind.True:
                    return "true";
                case JsonValueKind.False:
                    return "false";
                default:
                    return value.GetRawText();
            }
        }
    }
}
