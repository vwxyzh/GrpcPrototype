using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Proxy
{
    public class GrpcServerConnection : Server.ServerBase
    {
        private readonly ChannelReader<ClientConnect> _c;
        private readonly ChannelReader<ClientDisconnect> _d;
        private readonly ConcurrentDictionary<string, Channel<ClientMessage>> _m;
        private readonly WSLifetimeManager _lm;

        public GrpcServerConnection(
            ChannelReader<ClientConnect> c,
            ChannelReader<ClientDisconnect> d,
            ConcurrentDictionary<string, Channel<ClientMessage>> m,
            WSLifetimeManager lm)
        {
            _c = c;
            _d = d;
            _m = m;
            _lm = lm;
        }

        public override async Task OnConnect(Empty request, IServerStreamWriter<ClientConnect> responseStream, ServerCallContext context)
        {
            while (await _c.WaitToReadAsync(context.CancellationToken))
            {
                while (_c.TryRead(out var cr))
                {
                    await responseStream.WriteAsync(cr);
                }
            }
        }

        public override async Task OnDisconnect(Empty request, IServerStreamWriter<ClientDisconnect> responseStream, ServerCallContext context)
        {
            while (await _d.WaitToReadAsync(context.CancellationToken))
            {
                while (_d.TryRead(out var dr))
                {
                    await responseStream.WriteAsync(dr);
                }
            }
        }

        public override async Task OnMessage(ListenRequest request, IServerStreamWriter<ClientMessage> responseStream, ServerCallContext context)
        {
            var ch = _m.GetOrAdd(request.Action, _ => Channel.CreateBounded<ClientMessage>(10)).Reader;
            while (await ch.WaitToReadAsync(context.CancellationToken))
            {
                // todo: filter
                while (ch.TryRead(out var hr))
                {
                    await responseStream.WriteAsync(hr);
                }
            }
        }

        public override Task<Empty> AckConnect(ClientConnectAck request, ServerCallContext context)
        {
            _lm.AckConnect(request.Client, request.Allow);
            return Task.FromResult(new Empty());
        }

        public override Task<Empty> SendTo(SendToRequest request, ServerCallContext context)
        {
            foreach (var cid in request.ConnectionId)
            {
                _ = _lm.SendToConnectionAsync(cid, request.Body.Value.Memory);
            }
            return Task.FromResult(new Empty());
        }

        public override Task<Empty> Broadcast(BroadcastRequest request, ServerCallContext context)
        {
            _lm.BroadcastAsync(request.Body.Value.Memory);
            return Task.FromResult(new Empty());
        }
    }
}
