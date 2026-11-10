using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.TestSupport
{
    /// <summary>
    /// loopback 임시 포트의 모의 장치. 연결마다 받은 줄('\n' 기준)에 응답한다.
    /// START는 무응답, hello에는 START_OK를 보내고, 나머지 줄은 respond가 정한다.
    /// respond가 null을 돌려주면 응답하지 않고(타임아웃 재현), <see cref="Close"/>를 돌려주면 연결을 끊는다.
    /// </summary>
    internal sealed class MockDevice : IAsyncDisposable
    {
        public const string Mac = "a1b2c3d4e5f6";
        public const string Close = "\0close";

        private readonly TcpListener listener;
        private readonly Func<string, string?> respond;
        private readonly CancellationTokenSource stop = new();
        private readonly Task acceptLoop;
        private int connectionCount;

        public MockDevice(Func<string, string?> respond)
        {
            this.respond = respond;
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            acceptLoop = AcceptLoopAsync();
        }

        public string Host => IPAddress.Loopback.ToString();

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public int ConnectionCount => Volatile.Read(ref connectionCount);

        /// <summary>받은 줄. 항목마다 (연결 번호, 줄).</summary>
        public ConcurrentQueue<(int Connection, string Line)> Received { get; } = new();

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (true)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync(stop.Token);
                    int number = Interlocked.Increment(ref connectionCount);
                    _ = ServeAsync(client, number);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task ServeAsync(TcpClient client, int number)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                StringBuilder pending = new();
                byte[] buffer = new byte[4096];
                try
                {
                    while (true)
                    {
                        int read = await stream.ReadAsync(buffer, stop.Token);
                        if (read == 0)
                            return;
                        pending.Append(Encoding.UTF8.GetString(buffer, 0, read));
                        int newline;
                        while ((newline = pending.ToString().IndexOf('\n')) >= 0)
                        {
                            string line = pending.ToString(0, newline);
                            pending.Remove(0, newline + 1);
                            Received.Enqueue((number, line));
                            string? response = line switch
                            {
                                "[DUT<-PC] START" => null,
                                "hello tcp SUCCESS_CONNECT" => $"[DUT->PC] START_OK:{Mac}#",
                                _ => respond(line),
                            };
                            if (response == Close)
                                return;
                            if (response is not null)
                                await stream.WriteAsync(Encoding.UTF8.GetBytes(response), stop.Token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (IOException)
                {
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            stop.Cancel();
            listener.Stop();
            await acceptLoop;
            stop.Dispose();
        }
    }
}
