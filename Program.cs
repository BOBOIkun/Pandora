using Pandora.Agent;
using Pandora.Agent.Tools;
using Pandora.WebSocket.Handler;
using Pandora.WebSocket.Server;

namespace Pandora
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var host = args.Length > 0 ? args[0] : "http://localhost:9527/";
            Console.WriteLine($"[Pandora] Starting WebSocket server on {host} (ws path: /ws)");

            var core = new Core();
            Console.WriteLine($"[Pandora] Core initialized, {core.ProviderManager.Providers.Count} provider(s) loaded");

            var handler = new WsMessageHandler(core);
            var server = new WsServer(host, handler);

            // 处理 Ctrl+C 优雅退出
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                Console.WriteLine("\n[Pandora] Shutting down...");
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                await server.StartAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[Pandora] Server stopped");
            }
        }
    }
}
