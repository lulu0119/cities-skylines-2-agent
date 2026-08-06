using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>A named, natural-language context block (map pin, road network...).</summary>
    public sealed class ContextBlock
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public string Kind = "note"; // "pin" | "network" | "note"
        public string Data = "{}";
        public DateTimeOffset CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Persists named context blocks produced by map pins / network selection
    /// and injects them into the agent's context as natural language.
    /// </summary>
    public static class ContextBlockStore
    {
        private static readonly object s_Lock = new object();

        public static List<ContextBlock> Blocks { get; private set; } = Load();

        public static ContextBlock Add(string name, string kind, string data)
        {
            lock (s_Lock)
            {
                var block = new ContextBlock
                {
                    Name = string.IsNullOrWhiteSpace(name) ? "Context " + (Blocks.Count + 1) : name,
                    Kind = string.IsNullOrWhiteSpace(kind) ? "note" : kind,
                    Data = data ?? "{}",
                };
                Blocks.Add(block);
                Save();
                return block;
            }
        }

        public static bool Remove(string id)
        {
            lock (s_Lock)
            {
                int removed = Blocks.RemoveAll(b => b.Id == id);
                if (removed > 0)
                {
                    Save();
                }
                return removed > 0;
            }
        }

        public static string RenderAll()
        {
            lock (s_Lock)
            {
                var lines = new List<string>();
                foreach (ContextBlock block in Blocks)
                {
                    lines.Add("- [" + block.Name + "] (" + block.Kind + "): " + block.Data);
                }
                return string.Join("\n", lines);
            }
        }

        public static string ToJsonString()
        {
            lock (s_Lock)
            {
                var array = new JsonArray();
                foreach (ContextBlock block in Blocks)
                {
                    array.Add(new JsonObject
                    {
                        ["id"] = block.Id,
                        ["name"] = block.Name,
                        ["kind"] = block.Kind,
                        ["data"] = block.Data,
                        ["createdAt"] = block.CreatedAt.ToString("o"),
                    });
                }
                return array.ToJsonString();
            }
        }

        private static void Save()
        {
            try
            {
                ModPaths.EnsureDirectories();
                File.WriteAllText(ModPaths.ContextBlocksFile, ToJsonString());
            }
            catch (Exception e)
            {
                CS2MCP.Mod.Log.Warn($"context blocks save failed: {e.Message}");
            }
        }

        private static List<ContextBlock> Load()
        {
            var blocks = new List<ContextBlock>();
            try
            {
                if (!File.Exists(ModPaths.ContextBlocksFile))
                {
                    return blocks;
                }
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(ModPaths.ContextBlocksFile)))
                {
                    foreach (JsonElement element in document.RootElement.EnumerateArray())
                    {
                        blocks.Add(new ContextBlock
                        {
                            Id = GetString(element, "id", Guid.NewGuid().ToString("N")),
                            Name = GetString(element, "name", ""),
                            Kind = GetString(element, "kind", "note"),
                            Data = GetString(element, "data", "{}"),
                            CreatedAt = DateTimeOffset.TryParse(GetString(element, "createdAt", ""), out DateTimeOffset created)
                                ? created
                                : DateTimeOffset.UtcNow,
                        });
                    }
                }
            }
            catch (Exception e)
            {
                CS2MCP.Mod.Log.Warn($"context blocks load failed: {e.Message}");
            }
            return blocks;
        }

        private static string GetString(JsonElement element, string name, string fallback)
        {
            if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            return fallback;
        }
    }
}
