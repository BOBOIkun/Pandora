using System.Net.WebSockets;
using System.Text;
using Pandora.WebSocket.Handler;

namespace Pandora.WebSocket.Server
{
    public class WsConnection
    {
        private readonly System.Net.WebSockets.WebSocket _ws;
        private readonly WsMessageHandler _handler;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public string? SessionId => _handler.GetSessionForConnection(this);

        public WsConnection(System.Net.WebSockets.WebSocket ws, WsMessageHandler handler)
        {
            _ws = ws;
            _handler = handler;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var buffer = new byte[4096];
            var messageBuilder = new StringBuilder();
            Console.WriteLine("[WsConnection] Connected");

            try
            {
                while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    try
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine("[WsConnection] Client sent close frame");
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
                        break;
                    }

                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        var json = messageBuilder.ToString();
                        messageBuilder.Clear();
                        _ = _handler.HandleMessageAsync(json, this, ct);
                    }
                }
            }
            catch (WebSocketException ex)
            {
                Console.WriteLine($"[WsConnection] WebSocket error: {ex.Message}");
            }
            finally
            {
                Console.WriteLine("[WsConnection] Disconnected");
                _handler.OnDisconnected(this);
                if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
                {
                    try
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                    catch { }
                }
                _ws.Dispose();
            }
        }

        public async Task SendAsync(string json, CancellationToken ct = default)
        {
            var data = Encoding.UTF8.GetBytes(json);
            await _sendLock.WaitAsync(ct);
            try
            {
                if (_ws.State == WebSocketState.Open)
                {
                    await _ws.SendAsync(new ArraySegment<byte>(data),
                        WebSocketMessageType.Text, true, ct);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>Fire-and-forget send (不阻塞调用线程)</summary>
        public void SendFireAndForget(string json)
        {
            _ = SendAsync(json);
        }
    }
}
