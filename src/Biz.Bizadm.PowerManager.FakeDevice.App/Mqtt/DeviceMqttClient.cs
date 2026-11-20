using MQTTnet;
using MQTTnet.Exceptions;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using System.Buffers;
using System.Security.Authentication;
using System.Text;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Mqtt
{
    /// <summary>접속 결과. <c>ReturnCode</c>(3.1 CONNACK 값)가 있거나 <c>Error</c>가 있다.</summary>
    internal sealed record MqttConnectOutcome(int? ReturnCode, string? Error);

    /// <summary>구독 결과. <c>Results</c>(받은 정수 값)가 있거나 <c>Error</c>가 있다.</summary>
    internal sealed record MqttSubscribeOutcome(IReadOnlyList<int>? Results, string? Error)
    {
        /// <summary>허용 QoS(0~2)가 아닌 값이 있는지.</summary>
        public bool HasFailure => Results is not null && Results.Any(r => r is < 0 or > 2);
    }

    /// <summary>받은 메시지.</summary>
    internal sealed record MqttReceivedMessage(string Topic, byte[] Payload, int QoS, bool Retain);

    /// <summary>
    /// 장치처럼 다원 브로커에 MQTT 3.1 TLS 1.2로 접속·구독·발행한다(C4·C5). 받은 메시지와 끊김을 이벤트로 넘긴다.
    /// </summary>
    internal sealed class DeviceMqttClient : IDisposable
    {
        private readonly FakeDeviceOptions options;
        private readonly IMqttClient client;
        private volatile bool disconnecting;

        public DeviceMqttClient(FakeDeviceOptions options)
        {
            this.options = options;
            client = new MqttClientFactory().CreateMqttClient();
            client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
            client.DisconnectedAsync += OnDisconnectedAsync;
        }

        public bool IsConnected => client.IsConnected;

        /// <summary>받은 메시지. 처리기는 MQTT 처리를 막지 않도록 빨리 끝나야 한다.</summary>
        public event Func<MqttReceivedMessage, Task>? MessageReceived;

        /// <summary>접속된 뒤 다원 쪽에서 끊김(스스로 끊은 경우는 알리지 않는다).</summary>
        public event Func<string, Task>? DisconnectedByServer;

        /// <summary>
        /// 접속 옵션(plan 인터페이스 절): V310, clean session, keepalive, 시간 제한, TLS 1.2·대상 호스트·검증 끄기 설정,
        /// 사용자명·비밀번호 UTF-8, Will(QoS 0, retain 없음).
        /// </summary>
        public static MqttClientOptions BuildOptions(FakeDeviceOptions options, string? password)
        {
            MqttClientOptionsBuilder builder = new MqttClientOptionsBuilder()
                .WithTcpServer(options.MqttServerHost, options.MqttServerPort)
                .WithClientId(options.ClientId)
                .WithProtocolVersion(MqttProtocolVersion.V310)
                .WithCleanSession(true)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(options.KeepAliveSeconds))
                .WithTimeout(options.MqttTimeout)
                .WithTlsOptions(tls =>
                {
                    tls.UseTls().WithTargetHost(options.MqttServerHost).WithSslProtocols(SslProtocols.Tls12);
                    if (options.SkipCertificateValidation)
                        tls.WithCertificateValidationHandler(_ => true);
                })
                .WithCredentials(options.UserName, password is null ? null : Encoding.UTF8.GetBytes(password))
                .WithWillTopic(options.WillTopic)
                .WithWillPayload(Encoding.UTF8.GetBytes(options.WillPayload))
                .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                .WithWillRetain(false);
            return builder.Build();
        }

        /// <summary>
        /// MQTTnet이 3.1 CONNACK 반환 코드를 바꿔 준 값을 3.1 코드로 되돌린다(중계와 같은 표). 대응표에 없으면 3(서버 사용 불가).
        /// </summary>
        public static int ToMqtt31ReturnCode(MqttClientConnectResultCode code) => code switch
        {
            MqttClientConnectResultCode.Success => 0,
            MqttClientConnectResultCode.UnsupportedProtocolVersion => 1,
            MqttClientConnectResultCode.ClientIdentifierNotValid => 2,
            MqttClientConnectResultCode.ServerUnavailable => 3,
            MqttClientConnectResultCode.BadUserNameOrPassword => 4,
            MqttClientConnectResultCode.NotAuthorized => 5,
            _ => 3,
        };

        public async Task<MqttConnectOutcome> ConnectAsync(string? password, CancellationToken cancellationToken)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.MqttTimeout);
            try
            {
                MqttClientConnectResult result = await client.ConnectAsync(BuildOptions(options, password), timeout.Token);
                return new MqttConnectOutcome(ToMqtt31ReturnCode(result.ResultCode), null);
            }
            catch (Exception ex)
            {
                // MQTTnet 5는 거부 CONNACK을 결과로 돌려준다. MqttConnectingFailedException에는 결과 코드가 없어 오류로 본다(plan 위험 절)
                return new MqttConnectOutcome(null, ErrorText(ex, cancellationToken, timeout));
            }
        }

        public async Task<MqttSubscribeOutcome> SubscribeAsync(string topic, MqttQualityOfServiceLevel qos, CancellationToken cancellationToken)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.MqttTimeout);
            try
            {
                MqttClientSubscribeOptions subscribe = new MqttClientSubscribeOptionsBuilder().WithTopicFilter(topic, qos).Build();
                MqttClientSubscribeResult result = await client.SubscribeAsync(subscribe, timeout.Token);
                return new MqttSubscribeOutcome(result.Items.Select(i => (int)i.ResultCode).ToList(), null);
            }
            catch (Exception ex)
            {
                return new MqttSubscribeOutcome(null, ErrorText(ex, cancellationToken, timeout));
            }
        }

        public async Task PublishAsync(string topic, byte[] payload, MqttQualityOfServiceLevel qos, CancellationToken cancellationToken)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.MqttTimeout);
            MqttApplicationMessage message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(qos)
                .WithRetainFlag(false)
                .Build();
            await client.PublishAsync(message, timeout.Token);
        }

        /// <summary>DISCONNECT를 보내 정상 종료한다. 이 뒤의 끊김은 알리지 않는다.</summary>
        public async Task DisconnectAsync()
        {
            disconnecting = true;
            if (!client.IsConnected)
                return;
            using CancellationTokenSource timeout = new CancellationTokenSource(options.MqttTimeout);
            await client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection).Build(), timeout.Token);
        }

        /// <summary>
        /// 바깥 취소는 <c>cancelled</c>, 시간 초과는 <c>timeout</c>, 그 밖은 예외 메시지. MQTTnet은 시간 초과를 다른 예외로 감싸기도 해서
        /// 안쪽 예외와 시간 제한 토큰(<paramref name="timeout"/>)까지 본다.
        /// </summary>
        public static string ErrorText(Exception ex, CancellationToken cancellationToken, CancellationTokenSource? timeout = null)
        {
            if (cancellationToken.IsCancellationRequested)
                return "cancelled";
            if (timeout?.IsCancellationRequested == true)
                return "timeout";
            for (Exception? e = ex; e is not null; e = e.InnerException)
            {
                if (e is MqttCommunicationTimedOutException or OperationCanceledException or TimeoutException)
                    return "timeout";
            }
            return ex.Message;
        }

        private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            Func<MqttReceivedMessage, Task>? handler = MessageReceived;
            if (handler is null)
                return Task.CompletedTask;
            MqttApplicationMessage m = e.ApplicationMessage;
            return handler(new MqttReceivedMessage(m.Topic, m.Payload.ToArray(), (int)m.QualityOfServiceLevel, m.Retain));
        }

        private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
        {
            if (disconnecting || !e.ClientWasConnected)
                return Task.CompletedTask;
            Func<string, Task>? handler = DisconnectedByServer;
            return handler is null ? Task.CompletedTask : handler(e.Exception?.Message ?? e.Reason.ToString());
        }

        public void Dispose()
        {
            disconnecting = true;
            client.Dispose();
        }
    }
}
