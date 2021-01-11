using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Greet;

namespace Clients
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri("wss://localhost:5001/client"), default);
            var task = OnMessage(ws, m =>
            {
                var hr = HelloReply.Parser.ParseFrom(new ReadOnlySequence<byte>(m));
                Console.WriteLine(hr.Message);
            });
            ShowHelp();
            while (true)
            {
                var line = Console.ReadLine();
                if (line == null)
                {
                    break;
                }
                var c = line.Split(" ", 2);
                if (c.Length != 2)
                {
                    ShowHelp();
                    continue;
                }
                var cm = new ClientMessageWS { Action = c[0], Body = Any.Pack(new HelloRequest { Name = c[1] }) };
                await ws.SendAsync(cm.ToByteArray(), WebSocketMessageType.Binary, true, default);
            }
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Command:");
            Console.WriteLine("  echo <name>");
            Console.WriteLine("  broadcast <name>");
        }

        private async static Task OnMessage(ClientWebSocket ws, Action<Memory<byte>> action)
        {
            while (true)
            {
                var seg = await ReadOneMessageAsync(ws);
                try
                {
                    action(seg);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(seg.Array);
                }
            }
        }

        private static async Task<ArraySegment<byte>> ReadOneMessageAsync(ClientWebSocket ws)
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
