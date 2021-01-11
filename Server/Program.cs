using Google.Protobuf.WellKnownTypes;
using Greet;
using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.Threading.Tasks;
using Proxy;

namespace CustomerServer
{
    internal class Program
    {
        private readonly Server.ServerClient _c;

        private static void Main(string[] args)
        {
            using var channel = GrpcChannel.ForAddress("https://localhost:5001/");
            new Program(channel).Start();
            Console.ReadLine();
        }

        public Program(GrpcChannel channel)
        {
            _c = new Server.ServerClient(channel);
        }

        private void Start()
        {
            _ = HandleConnect(_c.OnConnect(new Empty()));
            _ = HandleDisconnect(_c.OnDisconnect(new Empty()));
            _ = HandleEcho(_c.OnMessage(new ListenRequest { Action = "echo" }));
            _ = HandleBroadcast(_c.OnMessage(new ListenRequest { Action = "broadcast" }));
        }

        private async Task HandleConnect(AsyncServerStreamingCall<ClientConnect> asyncServerStreamingCall)
        {
            await foreach (var c in asyncServerStreamingCall.ResponseStream.ReadAllAsync())
            {
                Console.WriteLine($"New connection: {c.Client.ConnectionId}");
                await _c.AckConnectAsync(new ClientConnectAck { Client = c.Client, Allow = true });
            }
        }

        private async Task HandleDisconnect(AsyncServerStreamingCall<ClientDisconnect> asyncServerStreamingCall)
        {
            await foreach (var c in asyncServerStreamingCall.ResponseStream.ReadAllAsync())
            {
                Console.WriteLine($"Connection disconnected: {c.Client.ConnectionId}");
            }
        }

        private async Task HandleEcho(AsyncServerStreamingCall<ClientMessage> asyncServerStreamingCall)
        {
            await foreach (var c in asyncServerStreamingCall.ResponseStream.ReadAllAsync())
            {
                Console.WriteLine($"Echo from: {c.Client.ConnectionId}");
                await _c.SendToAsync(
                    new SendToRequest
                    {
                        ConnectionId = { c.Client.ConnectionId },
                        Body = Any.Pack(new HelloReply { Message = $"Echo from {c.Client.ConnectionId}: {c.Body.Unpack<HelloRequest>().Name }" }),
                    });
            }
        }

        private async Task HandleBroadcast(AsyncServerStreamingCall<ClientMessage> asyncServerStreamingCall)
        {
            await foreach (var c in asyncServerStreamingCall.ResponseStream.ReadAllAsync())
            {
                Console.WriteLine($"Broadcast from: {c.Client.ConnectionId}");
                await _c.BroadcastAsync(
                    new BroadcastRequest
                    {
                        Body = Any.Pack(new HelloReply { Message = $"Broadcast from {c.Client.ConnectionId}: {c.Body.Unpack<HelloRequest>().Name }" }),
                    });
            }
        }
    }
}
