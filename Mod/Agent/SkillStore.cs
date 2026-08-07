using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>One user-editable skill package (folder + SKILL.md).</summary>
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
    /// Each skill is a folder containing SKILL.md with optional front matter
    /// (name / description) followed by Markdown instructions. Skills are text
    /// only: the agent loop injects enabled ones into the model context; no
    /// code from skills is ever executed.
    /// </summary>
    public static class SkillStore
    {
        public const string SkillsDirectoryName = "Skills";

        private const string BuiltinResourcePrefix = "CitiesSkylines2Agent.Agent.Skills.";

        public static string SkillsDirectory => Path.Combine(ModPaths.ModDataDirectory, SkillsDirectoryName);

        /// <summary>Ensures the folder exists and ships bundled skills on first run.</summary>
        public static void EnsureDefaults()
        {
            Directory.CreateDirectory(SkillsDirectory);
            CopyBuiltin("utility-networks");
        }

        public static List<AgentSkill> LoadAll()
        {
            EnsureDefaults();
            var skills = new List<AgentSkill>();
            if (!Directory.Exists(SkillsDirectory))
            {
                return skills;
            }
            foreach (string directory in Directory.GetDirectories(SkillsDirectory))
            {
                string skillFile = Path.Combine(directory, "SKILL.md");
                if (!File.Exists(skillFile))
                {
                    continue;
                }
                try
                {
                    skills.Add(ParseSkill(directory, skillFile));
                }
                catch (Exception e)
                {
                    CS2MCP.Mod.Log.Warn($"skill load failed for {directory}: {e.Message}");
                }
            }
            return skills;
        }

        /// <summary>Renders the enabled skills as one Markdown block for the model.</summary>
        public static string RenderEnabled(IReadOnlyCollection<string> enabledNames)
        {
            var builder = new StringBuilder();
            foreach (AgentSkill skill in LoadAll())
            {
                if (!ContainsIgnoreCase(enabledNames, skill.Name))
                {
                    continue;
                }
                builder.AppendLine("## Skill: " + skill.Name);
                if (!string.IsNullOrWhiteSpace(skill.Description))
                {
                    builder.AppendLine(skill.Description);
                }
                builder.AppendLine(skill.Content);
                builder.AppendLine();
            }
            return builder.ToString().Trim();
        }

        private static AgentSkill ParseSkill(string directory, string skillFile)
        {
            string text = File.ReadAllText(skillFile);
            string name = Path.GetFileName(directory);
            string description = "";
            string content = text;

            if (text.StartsWith("---", StringComparison.Ordinal))
            {
                int end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
                if (end > 0)
                {
                    string header = text.Substring(3, end - 3);
                    content = text.Substring(end + 4);
                    foreach (string line in header.Split('\n'))
                    {
                        int colon = line.IndexOf(':');
                        if (colon <= 0)
                        {
                            continue;
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
                }
            }

            return new AgentSkill
            {
                Name = name,
                Description = description,
                Content = content.Trim(),
                SourcePath = skillFile,
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(skillFile),
            };
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
                if (File.Exists(target))
                {
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (FileStream file = File.Create(target))
                {
                    stream.CopyTo(file);
                }
            }
        }

        private static bool ContainsIgnoreCase(IReadOnlyCollection<string> names, string name)
        {
            foreach (string candidate in names)
            {
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
