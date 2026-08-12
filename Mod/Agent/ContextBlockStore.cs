using System;
using System.Collections.Generic;
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
    /// Holds named context blocks produced by map pins / network selection for
    /// the current city session and injects them as natural language.
    /// </summary>
    public static class ContextBlockStore
    {
        private static readonly object s_Lock = new object();

        public static List<ContextBlock> Blocks { get; private set; } =
            new List<ContextBlock>();

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
                return block;
            }
        }

        public static bool Remove(string id)
        {
            lock (s_Lock)
            {
                int removed = Blocks.RemoveAll(b => b.Id == id);
                return removed > 0;
            }
        }

        public static void Clear()
        {
            lock (s_Lock)
            {
                Blocks.Clear();
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

    }
}
