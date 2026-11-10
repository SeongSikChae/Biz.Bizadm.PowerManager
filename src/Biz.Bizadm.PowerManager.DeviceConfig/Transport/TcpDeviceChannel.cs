using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using System.Text;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Transport
{
    /// <summary>
    /// TCP(<c>espconn</c> 서버, 기본 포트 5000)로 장치와 통신한다. 인코딩은 ASCII.
    /// </summary>
    public sealed class TcpDeviceChannel : IDeviceChannel
    {
        private readonly TcpClient client;
        private readonly NetworkStream stream;
        private readonly ILogger logger;
        private readonly DeviceFrameBuffer buffer = new();
        private readonly byte[] readBuffer = new byte[1500];

        private TcpDeviceChannel(TcpClient client, ILogger logger)
        {
            this.client = client;
            this.logger = logger;
            stream = client.GetStream();
        }

        /// <exception cref="TimeoutException"><paramref name="connectTimeout"/> 안에 연결하지 못했다.</exception>
        public static async Task<TcpDeviceChannel> ConnectAsync(string host, int port, TimeSpan connectTimeout, ILogger logger, CancellationToken cancellationToken)
        {
            TcpClient client = new() { NoDelay = true };
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(connectTimeout);
            try
            {
                await client.ConnectAsync(host, port, cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                client.Dispose();
                throw new TimeoutException($"Device connect timeout: {host}:{port}");
            }
            catch
            {
                client.Dispose();
                throw;
            }
            logger.LogInformation("Connected to device {Host}:{Port}", host, port);
            return new TcpDeviceChannel(client, logger);
        }

        public async Task SendLineAsync(string line, CancellationToken cancellationToken, string? logText = null)
        {
            logger.LogInformation("TX {Message}", logText ?? line);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(line + "\n"), cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        public async Task<string> ReceiveAsync(DeviceFrame frame, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            while (true)
            {
                while (buffer.TryTakeEvent(out string evt))
                    logger.LogInformation("Device event {Event}", evt);
                if (buffer.TryTake(frame, out string message))
                {
                    logger.LogInformation("RX {Message}", message);
                    return message;
                }

                int read;
                try
                {
                    read = await stream.ReadAsync(readBuffer, cts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    string partial = buffer.Peek();
                    logger.LogWarning("Device response timeout ({Frame}). Received: {Received}", frame, partial);
                    throw new TimeoutException($"Device response timeout ({frame}). Received: {partial}");
                }
                catch (IOException ex) when (ex.InnerException is SocketException)
                {
                    throw new DeviceDisconnectedException(buffer.Peek());
                }

                if (read == 0)
                    throw new DeviceDisconnectedException(buffer.Peek());
                buffer.Append(readBuffer.AsSpan(0, read));
            }
        }

        public ValueTask DisposeAsync()
        {
            stream.Dispose();
            client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
