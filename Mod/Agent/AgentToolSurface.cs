using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>
    /// Owns the model-facing tool surface. Core tools are always visible;
    /// domain groups are enabled by the model when a task needs them.
    /// </summary>
    internal sealed class AgentToolSurface
    {
        private static readonly IReadOnlyDictionary<string, string[]> s_Groups =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["construction"] = new[]
                {
                    "find_prefabs", "place_building", "build_road",
                    "list_buildings", "get_operational_area", "expand_operational_area", "list_zones", "zone_area", "zone_rectangle", "set_road_features",
                    "list_roads", "demolish", "terrain", "gridmap", "zoning",
                    "list_tiles", "buy_tiles", "list_objects",
                },
                ["finance"] = new[]
                {
                    "get_taxes", "set_tax", "policies", "set_policy", "service_budgets",
                    "set_service_budget", "get_loan", "set_loan", "get_fees", "set_fee",
                },
                ["progression"] = new[]
                {
                    "get_progression", "purchase_development_node",
                },
                ["visual"] = new[]
                {
                    "screenshot", "get_camera", "set_camera",
                },
            };

        private static readonly HashSet<string> s_CoreTools =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "ping", "game_state", "city_overview", "demand",
                "budget", "city_services", "labor", "statistics", "notifications",
                "inspect", "wait_simulation",
            };

        private static readonly HashSet<string> s_DevelopmentTools =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "replace_road_type", "debug_zone_blocks", "save_game",
            };

        private static readonly HashSet<string> s_MetaTools =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "agent_enable_tool_group", "agent_list_context_blocks",
                "agent_read_skill", "agent_add_context_block",
                "agent_remove_context_block",
            };

        private readonly HashSet<string> m_EnabledGroups =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void Reset()
        {
            m_EnabledGroups.Clear();
        }

        public bool EnableGroup(string group, bool visionAvailable)
        {
            string normalized = group?.Trim();
            if (string.IsNullOrWhiteSpace(normalized) || !s_Groups.ContainsKey(normalized))
            {
                return false;
            }
            if (string.Equals(normalized, "visual", StringComparison.OrdinalIgnoreCase) &&
                !visionAvailable)
            {
                return false;
            }
            m_EnabledGroups.Add(normalized);
            return true;
        }

        public bool IsAvailable(string name, AgentModelProfile profile, bool visionEnabled)
        {
            return IsExposed(
                name,
                profile != null && profile.SupportsVision && visionEnabled);
        }

        public bool IsMetaTool(string name)
        {
            return s_MetaTools.Contains(name);
        }

        public List<AITool> Build(AgentModelProfile profile, bool visionEnabled)
        {
            var tools = new List<AITool>();
            foreach (ToolDefinition tool in ToolCatalog.Tools)
            {
                if (!IsExposed(
                    tool.Name,
                    profile != null && profile.SupportsVision && visionEnabled))
                {
                    continue;
                }
                tools.Add(AIFunctionFactory.CreateDeclaration(
                    tool.Name,
                    tool.Description,
                    tool.Parameters,
                    null));
            }
            AddMetaTools(tools, profile != null && profile.SupportsVision && visionEnabled);
            return tools;
        }

        private bool IsExposed(string name, bool visionAvailable)
        {
            if (!visionAvailable &&
                (name == "screenshot" || name == "get_camera" || name == "set_camera"))
            {
                return false;
            }
            if (s_DevelopmentTools.Contains(name))
            {
                return Setting.StaticEnableDevelopmentTools;
            }
            if (name == "purchase_development_node" &&
                !Setting.StaticAllowProgressionPurchases)
            {
                return false;
            }
            if (name == "demolish" && !Setting.StaticAllowDemolition)
            {
                return false;
            }
            if (s_CoreTools.Contains(name))
            {
                return true;
            }
            foreach (string group in m_EnabledGroups)
            {
                if (s_Groups.TryGetValue(group, out string[] names) &&
                    Array.IndexOf(names, name) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void AddMetaTools(List<AITool> tools, bool visionAvailable)
        {
            string groupEnum = visionAvailable
                ? "[\"construction\",\"finance\",\"progression\",\"visual\"]"
                : "[\"construction\",\"finance\",\"progression\"]";
            tools.Add(AIFunctionFactory.CreateDeclaration(
                "agent_enable_tool_group",
                visionAvailable
                    ? "Enable a specialized tool group for the current turn. Available groups: construction, finance, progression, visual."
                    : "Enable a specialized tool group for the current turn. Available groups: construction, finance, progression. Visual tools are unavailable for this model.",
                ParseSchema("{\"type\":\"object\",\"properties\":{\"group\":{\"type\":\"string\",\"enum\":" +
                    groupEnum + "}},\"required\":[\"group\"]}"),
                null));
            tools.Add(AIFunctionFactory.CreateDeclaration(
                "agent_list_context_blocks",
                "List the named context blocks the player created (map pins / selected networks).",
                ParseSchema(@"{ ""type"": ""object"", ""properties"": {} }"),
                null));
            tools.Add(AIFunctionFactory.CreateDeclaration(
                "agent_read_skill",
                "Load the full instructions for one skill from the available skill index.",
                ParseSchema(@"{
  ""type"": ""object"",
  ""properties"": { ""name"": { ""type"": ""string"" } },
  ""required"": [""name""]
}"),
                null));
            tools.Add(AIFunctionFactory.CreateDeclaration(
                "agent_add_context_block",
                "Register a piece of natural-language information as a named context block; it is provided to the model every turn.",
                ParseSchema(@"{
  ""type"":""object"",
  ""properties"":{
    ""name"":{ ""type"": ""string"" },
    ""kind"":{ ""type"": ""string"", ""enum"": [""pin"", ""network"", ""note""] },
    ""data"":{ ""type"": ""string"" }
  },
  ""required"": [""name"", ""data""]
}"),
                null));
            tools.Add(AIFunctionFactory.CreateDeclaration(
                "agent_remove_context_block",
                "Delete a context block by id.",
                ParseSchema(@"{
  ""type"": ""object"",
  ""properties"": { ""id"": { ""type"": ""string"" } },
  ""required"": [""id""]
}"),
                null));
        }

        private static JsonElement ParseSchema(string json)
        {
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                return document.RootElement.Clone();
            }
        }
    }
}
