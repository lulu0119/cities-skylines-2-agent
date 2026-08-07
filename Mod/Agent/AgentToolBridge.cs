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
    /// the game validates construction while the simulation runs. Timed
    /// /sim/run waits until auto-pause so the model does not busy-poll
    /// game_state.
    /// </summary>
    public static class AgentToolBridge
    {
        private const int SimWaitPollMs = 250;

        public static async Task<ToolInvocationResult> InvokeAsync(
            ToolDefinition tool,
            string argumentsJson,
            CancellationToken cancellationToken,
            Action<string> progress = null)
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
                return Error($"invalid arguments for {tool.Name}: {e.Message}");
            }

            bool simRun = string.Equals(tool.Route, "/sim/run", StringComparison.Ordinal);
            bool cancelRun = query.ContainsKey("cancel");

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
            if (simRun && !cancelRun)
            {
                text = await WaitForSimRunAsync(bridge, text, cancellationToken, progress);
            }
            return new ToolInvocationResult
            {
                Success = true,
                Text = text,
            };
        }

        /// <summary>
        /// Blocks until BridgeSystem clears AutoPauseTargetFrame, then appends
        /// a final /state snapshot so the model need not poll.
        /// </summary>
        private static async Task<string> WaitForSimRunAsync(
            CS2MCP.BridgeSystem bridge,
            string startJson,
            CancellationToken cancellationToken,
            Action<string> progress)
        {
            int waited = 0;
            uint startFrame = 0;
            uint targetFrame = 0;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(startJson))
                {
                    if (document.RootElement.TryGetProperty("startFrame", out JsonElement start) &&
                        start.ValueKind == JsonValueKind.Number)
                    {
                        startFrame = start.GetUInt32();
                    }
                    if (document.RootElement.TryGetProperty("targetFrame", out JsonElement target) &&
                        target.ValueKind == JsonValueKind.Number)
                    {
                        targetFrame = target.GetUInt32();
                    }
                }
            }
            catch
            {
                // progress stays time-based when the start JSON is not parseable
            }

            int maxWaitMs = CitiesSkylines2Agent.Setting.StaticMaxSimWaitSeconds * 1000;
            int lastProgressMs = -1000;
            while (bridge.AutoPauseTargetFrame != 0 && waited < maxWaitMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(SimWaitPollMs, cancellationToken);
                waited += SimWaitPollMs;

                if (progress != null && waited - lastProgressMs >= 1000)
                {
                    lastProgressMs = waited;
                    string text = string.Format(
                        CultureInfo.InvariantCulture,
                        "模拟推进中… 已等待 {0:F0}s",
                        waited / 1000f);
                    if (targetFrame > startFrame)
                    {
                        try
                        {
                            CS2MCP.BridgeResponse state = await bridge.InvokeAsync("/state");
                            if (state != null && state.Status == 200)
                            {
                                using (JsonDocument doc = JsonDocument.Parse(
                                    Encoding.UTF8.GetString(state.Body ?? Array.Empty<byte>())))
                                {
                                    if (doc.RootElement.TryGetProperty("simulation", out JsonElement sim) &&
                                        sim.TryGetProperty("frameIndex", out JsonElement frame) &&
                                        frame.ValueKind == JsonValueKind.Number)
                                    {
                                        uint current = frame.GetUInt32();
                                        float ratio = (current - startFrame) /
                                            (float)(targetFrame - startFrame);
                                        text = string.Format(
                                            CultureInfo.InvariantCulture,
                                            "模拟推进中… {0:P0}",
                                            Math.Max(0f, Math.Min(1f, ratio)));
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // progress is best-effort; the wait itself still works
                        }
                    }
                    progress(text);
                }
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
                CS2MCP.Mod.Log.Warn($"post-sim state failed: {e.Message}");
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
                    root["note"] = "timed run still in progress after wait cap; check game_state once";
                }
                else
                {
                root["note"] = "timed run finished and auto-paused; do not poll game_state for this run";
                progress?.Invoke("模拟完成，已自动暂停");
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
