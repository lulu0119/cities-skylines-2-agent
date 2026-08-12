using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace CS2MCP
{
    /// <summary>
    /// One in-process tool command handed to the simulation thread.
    /// </summary>
    public sealed class BridgeRequest
    {
        public string Path;
        public Dictionary<string, string> Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Body;

        private readonly TaskCompletionSource<BridgeResponse> m_Completion =
            new TaskCompletionSource<BridgeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Async completion for in-process callers (agent loop).</summary>
        public Task<BridgeResponse> CompletionTask => m_Completion.Task;

        public bool TryGetInt(string key, out int value)
        {
            value = 0;
            return Query.TryGetValue(key, out string raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public bool TryGetFloat(string key, out float value)
        {
            value = 0f;
            return Query.TryGetValue(key, out string raw)
                && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public bool TryGetBool(string key, out bool value)
        {
            value = false;
            return Query.TryGetValue(key, out string raw) && bool.TryParse(raw, out value);
        }

        public void Complete(BridgeResponse response)
        {
            m_Completion.TrySetResult(response);
        }
    }

    public enum BridgeErrorKind
    {
        InvalidArguments,
        NotFound,
        Conflict,
        Unavailable,
        Timeout,
        Internal,
    }

    public sealed class BridgeResponse
    {
        public bool Success = true;
        public BridgeErrorKind? ErrorKind;
        public byte[] Body = Array.Empty<byte>();

        public static BridgeResponse Json(object payload)
        {
            return new BridgeResponse
            {
                Body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload, Formatting.None)),
            };
        }

        public static BridgeResponse Error(BridgeErrorKind kind, string message)
        {
            return new BridgeResponse
            {
                Success = false,
                ErrorKind = kind,
                Body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
                    new { error = message, kind = ErrorKindName(kind) },
                    Formatting.None)),
            };
        }

        public static BridgeResponse Png(byte[] png)
        {
            return new BridgeResponse { Body = png };
        }

        private static string ErrorKindName(BridgeErrorKind kind)
        {
            switch (kind)
            {
                case BridgeErrorKind.InvalidArguments: return "invalid_arguments";
                case BridgeErrorKind.NotFound: return "not_found";
                case BridgeErrorKind.Conflict: return "conflict";
                case BridgeErrorKind.Unavailable: return "unavailable";
                case BridgeErrorKind.Timeout: return "timeout";
                default: return "internal";
            }
        }
    }
}
