using System;
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>
    /// Owns the OpenAI-compatible client cache and the resolved model profile.
    /// </summary>
    internal sealed class AgentClientFactory : IDisposable
    {
        private readonly AgentObservability m_Observability;
        private readonly object m_Lock = new object();
        private IChatClient m_Client;
        private AgentModelProfile m_Profile;
        private string m_ConfigSignature;

        public AgentClientFactory(AgentObservability observability)
        {
            m_Observability = observability;
        }

        public IChatClient GetClient()
        {
            lock (m_Lock)
            {
                RefreshConfigurationLocked();
                if (m_Client != null)
                {
                    return m_Client;
                }
                if (string.IsNullOrWhiteSpace(Setting.StaticEndpoint) ||
                    string.IsNullOrWhiteSpace(Setting.StaticApiKey) ||
                    string.IsNullOrWhiteSpace(Setting.StaticModel))
                {
                    return null;
                }

                try
                {
                    var options = new OpenAIClientOptions
                    {
                        Endpoint = new Uri(Setting.StaticEndpoint),
                    };
                    var openAiClient = new OpenAIClient(
                        new ApiKeyCredential(Setting.StaticApiKey),
                        options);
                    ChatClient chatClient = openAiClient.GetChatClient(Setting.StaticModel);
                    m_Client = chatClient.AsIChatClient();
                    return m_Client;
                }
                catch (Exception e)
                {
                    m_Observability.Error("client-create", e.ToString());
                    return null;
                }
            }
        }

        public AgentModelProfile GetProfile()
        {
            lock (m_Lock)
            {
                RefreshConfigurationLocked();
                return m_Profile;
            }
        }

        public void Refresh()
        {
            lock (m_Lock)
            {
                m_Client?.Dispose();
                m_Client = null;
                m_Profile = null;
                m_ConfigSignature = null;
            }
        }

        private void RefreshConfigurationLocked()
        {
            string signature = Setting.StaticEndpoint + "|" +
                Setting.StaticApiKey + "|" + Setting.StaticModel + "|" +
                Setting.StaticWindowTokens + "|" + Setting.StaticVisionToolMode;
            if (string.Equals(m_ConfigSignature, signature, StringComparison.Ordinal))
            {
                return;
            }

            m_Client?.Dispose();
            m_Client = null;
            m_ConfigSignature = signature;
            m_Profile = AgentModelProfile.Resolve(
                Setting.StaticModel,
                Setting.StaticWindowTokens,
                Setting.StaticVisionToolMode);
        }

        public void Dispose()
        {
            lock (m_Lock)
            {
                m_Client?.Dispose();
                m_Client = null;
            }
        }
    }
}
