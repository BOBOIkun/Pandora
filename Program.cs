using Pandora.Agent;
using Pandora.Agent.Tools;
using Pandora.Interfaces;
using Pandora.WebSocket.Handler;
using Pandora.WebSocket.Server;

namespace Pandora
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var host = args.Length > 0 ? args[0] : "http://localhost:9527/";
            Logger.Instance.Log(LogLevel.Info, $"Starting WebSocket server on {host} (ws path: /ws)");

            var core = new Core();
            Logger.Instance.Log(LogLevel.Info, $"Core initialized, {core.ProviderManager.Providers.Count} provider(s) loaded");

            var handler = new WsMessageHandler(core);
            var server = new WsServer(host, handler);

            // 处理 Ctrl+C 优雅退出
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                Logger.Instance.Log(LogLevel.Info, "Server shutting down");
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                await server.StartAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Logger.Instance.Log(LogLevel.Info, "Server stopped");
            }
        }
    }
}
