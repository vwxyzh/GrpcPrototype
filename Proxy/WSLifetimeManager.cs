using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Proxy
{
    public class WSLifetimeManager
    {
        private readonly ConcurrentDictionary<string, WebSocket> _dict =
            new ConcurrentDictionary<string, WebSocket>();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _handshakes =
            new ConcurrentDictionary<string, TaskCompletionSource<bool>>();
        private readonly ChannelWriter<ClientConnect> _c;
        private readonly ChannelWriter<ClientDisconnect> _d;
        private readonly ConcurrentDictionary<string, Channel<ClientMessage>> _m;
        private readonly ILogger<WSLifetimeManager> _logger;

        public WSLifetimeManager(
            ChannelWriter<ClientConnect> c,
            ChannelWriter<ClientDisconnect> d,
            ConcurrentDictionary<string, Channel<ClientMessage>> m,
            ILogger<WSLifetimeManager> logger)
        {
            _c = c;
            _d = d;
            _m = m;
            _logger = logger;
        }

        public async Task HandleWebSocketAsync(HttpContext context, WebSocket ws)
        {
            string cid = context.Connection.Id;
            if (!_dict.TryAdd(cid, ws))
            {
                return;
            }
            _logger.LogInformation("New connection: {0}", cid);
            var c = new ClientInfo { ConnectionId = cid, User = "" };
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _handshakes.TryAdd(cid, tcs);
            await _c.WriteAsync(new ClientConnect { Client = c });
            if (!await tcs.Task)
            {
                _logger.LogInformation("Connection declined: {0}", cid);
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Deny", default);
                return;
            }
            try
            {
                _logger.LogInformation("Connection accepted: {0}", cid);
                await HandleWebSocketInComingAsync(ws, c);
            }
            finally
            {
                _dict.TryRemove(cid, out _);
                _logger.LogInformation("Connection dropped: {0}", cid);
                await _d.WriteAsync(new ClientDisconnect { Client = c });
            }
        }

        public async Task SendToConnectionAsync(string connectionId, ReadOnlyMemory<byte> content)
        {
            if (_dict.TryGetValue(connectionId, out var ws))
            {
                // todo: Semaphore
                await ws.SendAsync(content, WebSocketMessageType.Binary, true, default);
            }
        }

        public void BroadcastAsync(ReadOnlyMemory<byte> memory)
        {
            foreach (var (_, ws) in _dict)
            {
                _ = ws.SendAsync(memory, WebSocketMessageType.Binary, true, default);
            }
        }

        private async Task HandleWebSocketInComingAsync(WebSocket ws, ClientInfo c)
        {
            while (true)
            {
                var seg = await ReadOneMessageAsync(ws);
                try
                {
                    _logger.LogInformation("Client message: {0}", BitConverter.ToString(seg.Array, seg.Offset, seg.Count));

                    try
                    {
                        var cm = ClientMessageWS.Parser.ParseFrom(seg.Array, seg.Offset, seg.Count);
                        if (_m.TryGetValue(cm.Action, out var ch))
                        {
                            await ch.Writer.WriteAsync(new ClientMessage { Client = c, Body = cm.Body });
                        }
                        else
                        {
                            await ws.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "No such method.", default);
                            return;
                        }
                    }
                    catch (Exception)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "Invalid message.", default);
                        return;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(seg.Array);
                }
            }
        }

        internal void AckConnect(ClientInfo client, bool allow)
        {
            if (_handshakes.TryGetValue(client.ConnectionId, out var tcs))
            {
                tcs.TrySetResult(allow);
            }
        }

        private static async Task<ArraySegment<byte>> ReadOneMessageAsync(WebSocket ws)
        {
            var o = ArrayPool<byte>.Shared.Rent(4096); // max 4K for prototype.
            int length = 0;
            while (true)
            {
                var r = await ws.ReceiveAsync(((Memory<byte>)o)[length..], default);
                length += r.Count;
                if (r.EndOfMessage)
                {
                    break;
                }
            }
            return new ArraySegment<byte>(o, 0, length);
        }
    }
}
