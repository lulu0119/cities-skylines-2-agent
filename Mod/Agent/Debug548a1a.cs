using System;
using System.IO;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>Temporary NDJSON debug sink for session 548a1a. Remove after verify.</summary>
    internal static class Debug548a1a
    {
        private const string LogPath =
            @"C:\Users\super\Documents\GitHub\cities-skylines-2-agent\debug-548a1a.log";

        public static void Log(string hypothesisId, string location, string message, string dataJson)
        {
            // #region agent log
            string line =
                "{\"sessionId\":\"548a1a\",\"hypothesisId\":\"" + hypothesisId +
                "\",\"location\":\"" + location +
                "\",\"message\":\"" + Escape(message) +
                "\",\"data\":" + (string.IsNullOrEmpty(dataJson) ? "{}" : dataJson) +
                ",\"timestamp\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
                ",\"runId\":\"pre-fix\"}\n";
            File.AppendAllText(LogPath, line);
            // #endregion
        }

        public static void LogUiPayload(string json)
        {
            // #region agent log
            File.AppendAllText(LogPath, json.TrimEnd() + "\n");
            // #endregion
        }

        private static string Escape(string value)
        {
            return (value ?? "")
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
        }
    }
}
