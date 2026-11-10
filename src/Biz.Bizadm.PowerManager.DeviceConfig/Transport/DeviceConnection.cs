using Biz.Bizadm.PowerManager.DeviceConfig.Protocol;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using System.Text;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Transport
{
    /// <summary>장치 TCP 연결 하나. 명령마다 새로 연다.</summary>
    internal sealed class DeviceConnection : IAsyncDisposable
    {
        private readonly TcpClient client;
        private readonly NetworkStream stream;
        private readonly TimeSpan timeout;
        private readonly ILogger logger;
        private readonly DeviceFrameReader reader = new();
        private readonly byte[] readBuffer = new byte[1024];

        private DeviceConnection(TcpClient client, TimeSpan timeout, ILogger logger)
        {
            this.client = client;
            stream = client.GetStream();
            this.timeout = timeout;
            this.logger = logger;
        }

        public static async Task<DeviceConnection> OpenAsync(string host, int port, TimeSpan timeout, ILogger logger, CancellationToken cancellationToken)
        {
            TcpClient client = new();
            try
            {
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);
                await client.ConnectAsync(host, port, cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                client.Dispose();
                throw new TimeoutException($"Device connection timed out after {timeout.TotalSeconds}s: {host}:{port}");
            }
            catch
            {
                client.Dispose();
                throw;
            }
            logger.LogInformation("Connected to device {Host}:{Port}", host, port);
            return new DeviceConnection(client, timeout, logger);
        }

        /// <summary>START → hello → START_OK 수신. MAC을 돌려준다.</summary>
        public async Task<string> HandshakeAsync(CancellationToken cancellationToken)
        {
            await SendAsync(DeviceMessages.Start, logSend: true, cancellationToken);
            return DeviceResponseParser.ParseStartOk(await RequestAsync(DeviceMessages.Hello, cancellationToken));
        }

        /// <summary>
        /// 송신 후 timeout 안에서 프레임 1개를 받는다. logSend가 false면 TX 로그를 남기지 않는다(설정 JSON은 호출자가 마스킹해 남김).
        /// 시간 초과는 TimeoutException, 응답 전 연결 종료는 IOException.
        /// 예외 메시지에는 송신 내용을 넣지 않는다(pass·mqtt_key 보호).
        /// </summary>
        public async Task<string> RequestAsync(string message, CancellationToken cancellationToken, bool logSend = true)
        {
            await SendAsync(message, logSend, cancellationToken);
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            try
            {
                string frame;
                while (!reader.TryTake(out frame))
                {
                    int read = await stream.ReadAsync(readBuffer, cts.Token);
                    if (read == 0)
                        throw new IOException("Device closed the connection before responding");
                    reader.Append(readBuffer.AsSpan(0, read));
                }
                logger.LogInformation("RX {Frame}", frame);
                return frame;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"No device response within {timeout.TotalSeconds}s");
            }
        }

        private async Task SendAsync(string message, bool logSend, CancellationToken cancellationToken)
        {
            if (logSend)
                logger.LogInformation("TX {Message}", message.TrimEnd('\n'));
            await stream.WriteAsync(Encoding.UTF8.GetBytes(message), cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
