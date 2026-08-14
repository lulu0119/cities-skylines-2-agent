using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>One skill package (folder + SKILL.md).</summary>
    public sealed class AgentSkill
    {
        public string Name;
        public string Description;
        public string Content;
        public string SourcePath;
        public DateTime LastWriteTimeUtc;
    }

    /// <summary>
    /// Loads skill packages from &lt;user data&gt;/Mods/CitiesSkylines2Agent/Skills.
    /// Bundled names (utility-networks, city-building, transit-lines) are
    /// refreshed from the assembly when their bytes change. Extra folders are
    /// left alone. A newer hot-reload copy of the same name wins.
    /// </summary>
    public static class SkillStore
    {
        public const string SkillsDirectoryName = "Skills";

        private const string BuiltinResourcePrefix = "CitiesSkylines2Agent.Agent.Skills.";

        public static string SkillsDirectory => Path.Combine(ModPaths.ModDataDirectory, SkillsDirectoryName);

        /// <summary>Ensures the folder exists and refreshes bundled skills.</summary>
        public static void EnsureDefaults()
        {
            Directory.CreateDirectory(SkillsDirectory);
            CopyBuiltin("utility-networks");
            CopyBuiltin("city-building");
            CopyBuiltin("transit-lines");
        }

        public static List<AgentSkill> LoadAll()
        {
            return LoadSkills(true);
        }

        private static List<AgentSkill> LoadSkills(bool includeContent)
        {
            EnsureDefaults();
            var skills = new List<AgentSkill>();
            var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            LoadSkillsFrom(SkillsDirectory, includeContent, skills, indexes, false);
            LoadSkillsFrom(ModPaths.HotReloadSkillsDirectory, includeContent, skills, indexes, true);
            return skills;
        }

        private static void LoadSkillsFrom(
            string root,
            bool includeContent,
            List<AgentSkill> skills,
            Dictionary<string, int> indexes,
            bool replaceIfNewer)
        {
            if (!Directory.Exists(root))
            {
                return;
            }
            foreach (string directory in Directory.GetDirectories(root))
            {
                string skillFile = Path.Combine(directory, "SKILL.md");
                if (!File.Exists(skillFile))
                {
                    continue;
                }
                try
                {
                    AgentSkill skill = ParseSkill(directory, skillFile, includeContent);
                    if (indexes.TryGetValue(skill.Name, out int index))
                    {
                        if (replaceIfNewer
                            && skill.LastWriteTimeUtc <= skills[index].LastWriteTimeUtc)
                        {
                            continue;
                        }
                        skills[index] = skill;
                    }
                    else
                    {
                        indexes[skill.Name] = skills.Count;
                        skills.Add(skill);
                    }
                }
                catch (Exception e)
                {
                    CS2MCP.Mod.Log.Warn($"skill load failed for {directory}: {e.Message}");
                }
            }
        }

        /// <summary>Renders skill names and descriptions without full instructions.</summary>
        public static string RenderIndex()
        {
            var builder = new StringBuilder();
            foreach (AgentSkill skill in LoadSkills(false))
            {
                builder.Append("- ").Append(skill.Name).Append(": ")
                    .AppendLine(string.IsNullOrWhiteSpace(skill.Description)
                        ? "No description available."
                        : skill.Description);
            }
            if (builder.Length == 0)
            {
                return "No skills are installed.";
            }
            return "Available skills (call agent_read_skill to load full instructions):\n" +
                builder.ToString().Trim();
        }

        public static bool TryRead(string name, out AgentSkill skill)
        {
            skill = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }
            foreach (AgentSkill candidate in LoadSkills(false))
            {
                if (string.Equals(candidate.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    skill = ParseSkill(
                        Path.GetDirectoryName(candidate.SourcePath),
                        candidate.SourcePath,
                        true);
                    return true;
                }
            }
            return false;
        }

        private static AgentSkill ParseSkill(
            string directory,
            string skillFile,
            bool includeContent)
        {
            string name = Path.GetFileName(directory);
            string description = "";
            string content = null;
            if (includeContent)
            {
                string text = File.ReadAllText(skillFile);
                content = text;
                if (text.StartsWith("---", StringComparison.Ordinal))
                {
                    int end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
                    if (end > 0)
                    {
                        string header = text.Substring(3, end - 3);
                        content = text.Substring(end + 4);
                        foreach (string line in header.Split('\n'))
                        {
                            ApplyFrontMatterLine(line, ref name, ref description);
                        }
                    }
                }
            }
            else
            {
                using (var reader = File.OpenText(skillFile))
                {
                    string firstLine = reader.ReadLine();
                    if (string.Equals(firstLine, "---", StringComparison.Ordinal))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null &&
                            !string.Equals(line, "---", StringComparison.Ordinal))
                        {
                            ApplyFrontMatterLine(line, ref name, ref description);
                        }
                    }
                }
            }

            return new AgentSkill
            {
                Name = name,
                Description = description,
                Content = content?.Trim(),
                SourcePath = skillFile,
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(skillFile),
            };
        }

        private static void ApplyFrontMatterLine(
            string line,
            ref string name,
            ref string description)
        {
            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                return;
            }
            string key = line.Substring(0, colon).Trim();
            string value = line.Substring(colon + 1).Trim();
            if (key.Equals("name", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
            {
                name = value;
            }
            else if (key.Equals("description", StringComparison.OrdinalIgnoreCase))
            {
                description = value;
            }
        }

        private static void CopyBuiltin(string name)
        {
            // MSBuild sanitizes folder names in resource identifiers: '-' and '.'
            // become '_'. The front matter keeps the human-readable name.
            string resourceName = BuiltinResourcePrefix +
                name.Replace('-', '_').Replace('.', '_') +
                ".SKILL.md";
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    return;
                }
                string target = Path.Combine(SkillsDirectory, name, "SKILL.md");
                byte[] bundled;
                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    bundled = copy.ToArray();
                }
                if (File.Exists(target) && BytesEqual(File.ReadAllBytes(target), bundled))
                {
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllBytes(target, bundled);
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
