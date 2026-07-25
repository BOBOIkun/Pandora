using Pandora.Agent;
using Pandora.Network;
using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Interfaces
{
    public interface ICore
    {
        public PandoraHttpClientFactory HttpClientFactory { get;}
        public Dictionary<string, ISession> Sessions { get;}
        public ISession CreateSession(string? sessionId, WorkMode workMode);
        public PandoraHttpClientFactory HttpClientFactoryProxy { get;}
        public IConfigManager ConfigManager { get;}
        public void DeleteSession(string sessionId);
        public ISession LoadSessionFromDirectory(string directoryPath);
        public ProviderManager ProviderManager { get;}
    }
}
