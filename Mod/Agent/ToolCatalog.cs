using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>One query parameter mapping for a bridge route.</summary>
    public sealed class ToolQuerySpec
    {
        public string Key;
        public string Arg;
        public string Literal;
        public string Default;
        public string BoolMode; // "trueOnly" | null
    }

    /// <summary>One tool definition from the 45-tool catalog.</summary>
    public sealed class ToolDefinition
    {
        public string Name;
        public string Description;
        public JsonElement Parameters;
        public string Route;
        public List<ToolQuerySpec> Query = new List<ToolQuerySpec>();
        public string Response = "json"; // "json" | "png"
    }

    /// <summary>
    /// Loads Mod/Agent/ToolCatalog.json (owned by this mod; originally derived
    /// from the CS2MCP upstream catalog, see Mod/CS2MCP/NOTICE.txt) and exposes
    /// the 45 tools.
    /// </summary>
    public static class ToolCatalog
    {
        private static readonly object s_Gate = new object();
        private static IReadOnlyList<ToolDefinition> s_Tools;
        private static DateTime s_OverrideWriteUtc;
        private static long s_OverrideLength = -1;
        private static bool s_UsingOverride;

        public static IReadOnlyList<ToolDefinition> Tools
        {
            get
            {
                lock (s_Gate)
                {
                    RefreshOverride();
                    if (s_Tools == null)
                    {
                        s_Tools = LoadEmbedded();
                    }
                    return s_Tools;
                }
            }
        }

        public static ToolDefinition Find(string name)
        {
            foreach (ToolDefinition tool in Tools)
            {
                if (string.Equals(tool.Name, name, StringComparison.Ordinal))
                {
                    return tool;
                }
            }
            return null;
        }

        private static void RefreshOverride()
        {
            string path = ModPaths.HotReloadToolCatalogFile;
            if (!File.Exists(path))
            {
                if (s_UsingOverride)
                {
                    s_Tools = LoadEmbedded();
                    s_UsingOverride = false;
                    s_OverrideWriteUtc = default;
                    s_OverrideLength = -1;
                    CS2MCP.Mod.Log.Info("hot-reload tool catalog removed; restored embedded catalog");
                }
                return;
            }

            var file = new FileInfo(path);
            if (s_UsingOverride &&
                file.LastWriteTimeUtc == s_OverrideWriteUtc &&
                file.Length == s_OverrideLength)
            {
                return;
            }

            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    s_Tools = Parse(stream);
                }
                s_UsingOverride = true;
                s_OverrideWriteUtc = file.LastWriteTimeUtc;
                s_OverrideLength = file.Length;
                CS2MCP.Mod.Log.Info("hot-reloaded tool catalog");
            }
            catch (Exception e)
            {
                // Keep the last valid catalog. A later read retries after the
                // build finishes replacing the file.
                CS2MCP.Mod.Log.Warn(
                    "hot-reload tool catalog rejected; keeping last known-good catalog: " +
                    e.Message);
            }
        }

        private static IReadOnlyList<ToolDefinition> LoadEmbedded()
        {
            using (Stream stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("CitiesSkylines2Agent.Agent.ToolCatalog.json"))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("ToolCatalog.json embedded resource missing");
                }
                return Parse(stream);
            }
        }

        private static IReadOnlyList<ToolDefinition> Parse(Stream stream)
        {
            using (JsonDocument document = JsonDocument.Parse(stream))
            {
                JsonElement root = document.RootElement;
                JsonElement toolsElement = root.GetProperty("tools");
                var tools = new List<ToolDefinition>(toolsElement.GetArrayLength());
                foreach (JsonElement toolElement in toolsElement.EnumerateArray())
                {
                    var tool = new ToolDefinition
                    {
                        Name = toolElement.GetProperty("name").GetString(),
                        Description = GetStringOrEmpty(toolElement, "description"),
                        Route = toolElement.GetProperty("route").GetString(),
                        Response = GetStringOrDefault(toolElement, "response", "json"),
                    };
                    if (toolElement.TryGetProperty("parameters", out JsonElement parameters))
                    {
                        tool.Parameters = parameters.Clone();
                    }
                    if (toolElement.TryGetProperty("query", out JsonElement query) &&
                        query.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement specElement in query.EnumerateArray())
                        {
                            var spec = new ToolQuerySpec
                            {
                                Key = specElement.GetProperty("key").GetString(),
                                Arg = GetStringOrNull(specElement, "arg"),
                                Literal = GetStringOrNull(specElement, "literal"),
                                Default = GetStringOrNull(specElement, "default"),
                                BoolMode = GetStringOrNull(specElement, "boolMode"),
                            };
                            tool.Query.Add(spec);
                        }
                    }
                    tools.Add(tool);
                }
                for (int i = 0; i < tools.Count; i++)
                {
                    for (int j = i + 1; j < tools.Count; j++)
                    {
                        if (string.Equals(tools[i].Name, tools[j].Name, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                "ToolCatalog.json contains duplicate tool name: " + tools[i].Name);
                        }
                    }
                }
                return tools;
            }
        }

        private static string GetStringOrEmpty(JsonElement element, string name)
        {
            string value = GetStringOrNull(element, name);
            return value ?? string.Empty;
        }

        private static string GetStringOrDefault(JsonElement element, string name, string fallback)
        {
            string value = GetStringOrNull(element, name);
            return value ?? fallback;
        }

        private static string GetStringOrNull(JsonElement element, string name)
        {
            if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            return null;
        }
    }
}
