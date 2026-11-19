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
        public Task<string> HandshakeAsync(CancellationToken cancellationToken) => HandshakeAsync(0, cancellationToken);

        /// <summary>
        /// START → hello → START_OK 수신. START_OK가 아닌 프레임은 최대 maxSkippedFrames개까지 건너뛴다
        /// (V4: 참고 구현 ConfigratorV4가 START_OK 앞 프레임을 읽어 버림). 그 뒤에도 START_OK가 아니면 DeviceProtocolException.
        /// </summary>
        public async Task<string> HandshakeAsync(int maxSkippedFrames, CancellationToken cancellationToken)
        {
            await SendAsync(DeviceMessages.Start, logSend: true, cancellationToken);
            string frame = await RequestAsync(DeviceMessages.Hello, cancellationToken);
            for (int skipped = 0; ; skipped++)
            {
                try
                {
                    return DeviceResponseParser.ParseStartOk(frame);
                }
                catch (DeviceProtocolException) when (skipped < maxSkippedFrames)
                {
                    logger.LogInformation("Skipping frame before START_OK ({Skipped}/{Max})", skipped + 1, maxSkippedFrames);
                    frame = await ReceiveAsync(cancellationToken);
                }
            }
        }

        /// <summary>
        /// 송신 후 timeout 안에서 프레임 1개를 받는다. logSend가 false면 TX 로그를 남기지 않는다(설정 JSON은 호출자가 마스킹해 남김).
        /// 시간 초과는 TimeoutException, 응답 전 연결 종료는 IOException.
        /// 예외 메시지에는 송신 내용을 넣지 않는다(pass·mqtt_key 보호).
        /// </summary>
        public async Task<string> RequestAsync(string message, CancellationToken cancellationToken, bool logSend = true)
        {
            await SendAsync(message, logSend, cancellationToken);
            return await ReceiveAsync(cancellationToken);
        }

        /// <summary>응답을 기다리지 않고 보낸다. logSend가 false면 TX 로그를 남기지 않는다.</summary>
        public async Task SendAsync(string message, bool logSend, CancellationToken cancellationToken)
        {
            if (logSend)
                logger.LogInformation("TX {Message}", message.TrimEnd('\n'));
            await stream.WriteAsync(Encoding.UTF8.GetBytes(message), cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        /// <summary>
        /// 송신을 정상 종료(FIN)하고, 장치가 연결을 닫을 때까지(수신 0바이트) 남은 수신을 읽어 비운 뒤 닫을 준비를 한다.
        /// 읽지 않은 수신 데이터가 남은 채 닫으면 Windows가 RST를 보내 마지막 송신이 장치에 닿지 않을 수 있다.
        /// 받은 프레임은 RX 로그만 남기고 검사하지 않는다. timeout 안에 닫히지 않거나 연결 오류가 나도 실패로 보지 않는다.
        /// </summary>
        public async Task CloseGracefullyAsync(CancellationToken cancellationToken)
        {
            try
            {
                client.Client.Shutdown(SocketShutdown.Send);
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);
                while (true)
                {
                    while (reader.TryTake(out string frame))
                        logger.LogInformation("RX {Frame}", frame);
                    int read = await stream.ReadAsync(readBuffer, cts.Token);
                    if (read == 0)
                        break;
                    reader.Append(readBuffer.AsSpan(0, read));
                }
                logger.LogInformation("Device closed the connection");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Device did not close the connection within {Seconds}s; closing", timeout.TotalSeconds);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                logger.LogInformation("Connection ended while closing: {Reason}", ex.Message);
            }
        }

        /// <summary>송신 없이 timeout 안에서 프레임 1개를 받는다. 시간 초과는 TimeoutException, 연결 종료는 IOException.</summary>
        private async Task<string> ReceiveAsync(CancellationToken cancellationToken)
        {
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

        public ValueTask DisposeAsync()
        {
            client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
