using Pandora.Interfaces;
using Pandora.Network;
using System;
using System.Collections.Generic;
using System.Text;

namespace Pandora.Agent
{
    public class Core: ICore
    {
        public Dictionary<string, ISession> Sessions {get;}= [];
        public PandoraHttpClientFactory HttpClientFactory {get;}
        public PandoraHttpClientFactory HttpClientFactoryProxy {get;}
        public ProviderManager ProviderManager {get; private set;}
        public IConfigManager ConfigManager { get; private set; }

        public ISession CreateSession(string? sessionId,WorkMode workMode)
        {
            sessionId ??= Guid.NewGuid().ToString();
            var session = new Session(this,sessionId, workMode);
            if (!Sessions.TryAdd(sessionId,session))
            {
                throw new PandoraException($"Session {sessionId} already exists");
            }
            return session;
        }

        public void DeleteSession(string sessionId)
        {
            if (Sessions.TryGetValue(sessionId, out var session) && session != null)
            {
                session.DataManager.DeleteSession();
                Sessions.Remove(sessionId);
            }
        }

        public ISession LoadSessionFromDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                throw new PandoraException($"Directory {directoryPath} not found.");
            }
            string file = DataManagerStatic.GetSessionInfoPathFromDir(directoryPath);
            if (!File.Exists(file))
            {
                throw new PandoraException($"Directory {directoryPath} not a valid session directory.");
            }
            SessionInfo? sessionInfo = DataManagerStatic.GetSessionInfoByFile(file);
            if (sessionInfo!= null)
            {
                ISession session = new Session(this,sessionInfo);
                if (!Sessions.TryAdd(sessionInfo.SessionId,session))
                {
                    throw new PandoraException($"Session {sessionInfo.SessionId} already exists");
                }
                return session;
            }
            throw new PandoraException("Invalid session info");
        }

        public Core()
        {
            ConfigManager = new ConfigManager();
            HttpClientFactory = new PandoraHttpClientFactory();
            string? proxyUrl = ConfigManager.GetValue<string>("ProxyUrl");
            if (!string.IsNullOrEmpty(proxyUrl))
            {
                HttpClientFactoryProxy = new PandoraHttpClientFactory(proxyUrl);
            }
            else
            {
                HttpClientFactoryProxy = HttpClientFactory;
            }
            ProviderManager = new ProviderManager(AppContext.BaseDirectory);
        }
    }
}
