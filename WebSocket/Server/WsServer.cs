using System.Net;
using System.Net.WebSockets;
using Pandora.Interfaces;
using Pandora.WebSocket.Handler;

namespace Pandora.WebSocket.Server
{
    public class WsServer
    {
        private readonly HttpListener _listener;
        private readonly WsMessageHandler _handler;
        private CancellationTokenSource? _cts;

        public WsServer(string url, WsMessageHandler handler)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(url);
            _handler = handler;
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listener.Start();
            Logger.Instance.Log(LogLevel.Info, $"Listening on {string.Join(", ", _listener.Prefixes)}");

            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync().WaitAsync(_cts.Token);
                    _ = HandleRequestAsync(context, _cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (HttpListenerException) when (_cts.Token.IsCancellationRequested) { }
            finally
            {
                _listener.Stop();
                Logger.Instance.Log(LogLevel.Info, "Server stopped");
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
        {
            HttpListenerRequest request = context.Request;
            // 只处理 /ws 路径的 WebSocket 请求
            if (!request.Url!.AbsolutePath.StartsWith("/ws", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }
            if (!request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                return;
            }

            HttpListenerWebSocketContext wsContext;
            try
            {
                wsContext = await context.AcceptWebSocketAsync(null);
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogLevel.Error, $"WebSocket accept failed: {ex}", nameof(HandleRequestAsync));
                return;
            }

            var connection = new WsConnection(wsContext.WebSocket, _handler);
            await connection.RunAsync(ct);
        }
    }
}
