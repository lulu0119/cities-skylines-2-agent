using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>
    /// Owns the model-facing tool surface for a round: catalog tools plus meta
    /// tools, filtered by vision capability and settings.
    /// </summary>
    internal sealed class AgentToolSurface
    {
        private static readonly HashSet<string> s_DevelopmentTools =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "replace_road_type", "debug_zone_blocks", "save_game",
            };

        private static readonly HashSet<string> s_MetaTools =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "agent_list_context_blocks",
                "agent_read_skill", "agent_add_context_block",
                "agent_remove_context_block",
            };

        public bool IsAvailable(string name, AgentModelProfile profile)
        {
            return IsAllowed(name, profile != null && profile.VisionAvailable);
        }

        public bool IsMetaTool(string name)
        {
            return s_MetaTools.Contains(name);
        }

        public List<AITool> Build(AgentModelProfile profile)
        {
            bool visionAvailable = profile != null && profile.VisionAvailable;
            var tools = new List<AITool>();
            foreach (ToolDefinition tool in ToolCatalog.Tools)
            {
                if (!IsAllowed(tool.Name, visionAvailable))
                {
                    continue;
                }
                tools.Add(AIFunctionFactory.CreateDeclaration(
                    tool.Name,
                    tool.Description,
                    tool.Parameters,
                    null));
            }
            AddMetaTools(tools);
            return tools;
        }

        private static bool IsAllowed(string name, bool visionAvailable)
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
            return name != "demolish" || Setting.StaticAllowDemolition;
        }

        private static void AddMetaTools(List<AITool> tools)
        {
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
    ""data"":{ ""type"": ""string"", ""maxLength"": 4000 }
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
