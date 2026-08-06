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

    /// <summary>One tool definition from the 44-tool CS2MCP catalog.</summary>
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
    /// Loads Mod/Agent/ToolCatalog.json (generated from the upstream MCP server
    /// by Mod/CS2MCP/Server/extract-tools.mjs) and exposes the 44 tools.
    /// </summary>
    public static class ToolCatalog
    {
        private static readonly Lazy<IReadOnlyList<ToolDefinition>> s_Tools =
            new Lazy<IReadOnlyList<ToolDefinition>>(Load);

        public static IReadOnlyList<ToolDefinition> Tools => s_Tools.Value;

        public static ToolDefinition Find(string name)
        {
            foreach (ToolDefinition tool in s_Tools.Value)
            {
                if (string.Equals(tool.Name, name, StringComparison.Ordinal))
                {
                    return tool;
                }
            }
            return null;
        }

        private static IReadOnlyList<ToolDefinition> Load()
        {
            using (Stream stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("CitiesSkylines2Agent.Agent.ToolCatalog.json"))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("ToolCatalog.json embedded resource missing");
                }
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
                    return tools;
                }
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
