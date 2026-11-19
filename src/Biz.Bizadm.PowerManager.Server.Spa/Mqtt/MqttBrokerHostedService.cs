using Biz.Bizadm.PowerManager.Core.Devices;
using Biz.Bizadm.PowerManager.Core.Mqtt;
using Biz.Bizadm.PowerManager.Server.Core;
using Biz.Bizadm.PowerManager.Server.Spa.Devices;
using Biz.Bizadm.PowerManager.Server.Spa.Relay;
using MQTTnet.Server;
using System.Buffers;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Mqtt
{
    /// <summary>
    /// 내장 MQTT 브로커. MQTT 포트와 (TLS 사용 시) MQTTS 포트를 열고, 장치 → 서버 메시지를 <see cref="IDeviceMessageHandler"/>로 분배한다.
    /// 접속 인증은 하지 않으며, 접속마다 자격 증명이 등록 키와 맞는지 관찰 로그만 남긴다.
    /// </summary>
    public sealed class MqttBrokerHostedService(AppConfiguration configuration, IDeviceMessageHandler handler,
        DeviceRegistrationService registrations, ILogger<MqttBrokerHostedService> logger, DawonMqttRelay? relay = null)
        : IHostedService, IAsyncDisposable
    {
        /// <summary>
        /// 로그에 남길 원본 페이로드의 최대 바이트 수. 넘으면 앞부분만 디코딩한다.
        /// </summary>
        private const int MaxLoggedPayloadBytes = 4096;

        private MqttServer? server;
        private X509Certificate2? certificate;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            AppConfiguration c = configuration;
            MqttServerFactory factory = new MqttServerFactory();
            MqttServerOptionsBuilder optionsBuilder = factory.CreateServerOptionsBuilder()
                .WithDefaultEndpoint()
                .WithDefaultEndpointPort(c.MqttPort!.Value);

            if (c.Tls?.UseTls == true)
            {
                certificate = TlsCertificateLoader.Load(c.Tls, c.ContentRootPath);
                optionsBuilder = optionsBuilder
                    .WithEncryptedEndpoint()
                    .WithEncryptedEndpointPort(c.MqttsPort!.Value)
                    .WithEncryptionCertificate(certificate)          // 체인·cipher는 적용하지 않는다(Kestrel만)
                    .WithEncryptionSslProtocol(SslProtocols.Tls12);  // 장치 mbedTLS는 TLS 1.0~1.2
            }

            server = factory.CreateMqttServer(optionsBuilder.Build());
            server.InterceptingPublishAsync += OnPublishAsync;
            server.ValidatingConnectionAsync += OnValidatingConnectionAsync;
            if (relay is not null)
            {
                // 중계 모드: 다원 접속·구독·발행·종료를 맞춘다. 접속 검증은 OnValidatingConnectionAsync 안에서 R9 로그 뒤에 부른다.
                relay.Attach(server);
                server.InterceptingInboundPacketAsync += relay.OnInboundPacketAsync;
                server.InterceptingSubscriptionAsync += relay.OnSubscriptionAsync;
                server.InterceptingClientEnqueueAsync += relay.OnClientEnqueueAsync;
                server.InterceptingOutboundPacketAsync += relay.OnOutboundPacketAsync;
                server.ClientDisconnectedAsync += relay.OnClientDisconnectedAsync;
            }
            await server.StartAsync();
            logger.LogInformation("MQTT broker started: port={MqttPort}, tlsPort={MqttsPort}",
                c.MqttPort, certificate is null ? null : c.MqttsPort);
        }

        public async Task StopAsync(CancellationToken cancellationToken) => await DisposeAsync();

        /// <summary>
        /// StopAsync 없이 호스트가 해제될 때(테스트)도 리스너를 정리한다.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (server is not null)
            {
                MqttServer current = server;
                server = null;
                current.InterceptingPublishAsync -= OnPublishAsync;
                current.ValidatingConnectionAsync -= OnValidatingConnectionAsync;
                if (relay is not null)
                {
                    current.InterceptingInboundPacketAsync -= relay.OnInboundPacketAsync;
                    current.InterceptingSubscriptionAsync -= relay.OnSubscriptionAsync;
                    current.InterceptingClientEnqueueAsync -= relay.OnClientEnqueueAsync;
                    current.InterceptingOutboundPacketAsync -= relay.OnOutboundPacketAsync;
                    current.ClientDisconnectedAsync -= relay.OnClientDisconnectedAsync;
                    await relay.DisposeAsync();
                }
                await current.StopAsync();
                current.Dispose();
            }
            certificate?.Dispose();
            certificate = null;
        }

        /// <summary>
        /// 접속 자격 증명 관찰(인증 강제 전 형식 확인용). ClientId <c>DAWONDNS-{mac}</c>의 mac이 등록돼 있으면
        /// 비밀번호를 등록 키와 비교해 Match/Mismatch, 아니면 Unknown으로 남긴다. 비밀번호 값은 남기지 않고, 접속은 모두 받는다.
        /// </summary>
        private async Task OnValidatingConnectionAsync(ValidatingConnectionEventArgs e)
        {
            string? password = e.Password;
            bool hasPassword = !string.IsNullOrEmpty(password);
            string check = "Unknown";
            try
            {
                if (DeviceIdentity.TryParseClientId(e.ClientId, out string? mac)
                    && await registrations.FindMqttKeyAsync(mac, CancellationToken.None) is string key)
                {
                    check = hasPassword && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(password!), Encoding.UTF8.GetBytes(key))
                        ? "Match"
                        : "Mismatch";
                }
            }
            catch (Exception ex)
            {
                // 관찰 로그 실패로 접속을 막지 않는다.
                logger.LogError(ex, "MQTT connection credential check failed: {ClientId}", e.ClientId);
            }

            logger.LogInformation("MQTT client connecting: clientId={ClientId}, userName={UserName}, hasPassword={HasPassword}, passwordCheck={PasswordCheck}",
                e.ClientId, e.UserName, hasPassword, check);

            if (relay is not null)
                await relay.OnValidatingConnectionAsync(e);
        }

        private async Task OnPublishAsync(InterceptingPublishEventArgs e)
        {
            string topicName = e.ApplicationMessage.Topic;
            if (!DeviceTopic.TryParse(topicName, out DeviceTopic? topic))
            {
                // 서버→장치 토픽 등은 그대로 전달한다. 실장비 토픽 형태 확인용으로 ClientId·원본 페이로드와 함께 Information으로 남긴다.
                (long bytes, bool truncated, string text) = DescribePayload(e.ApplicationMessage.Payload);
                logger.LogInformation(
                    "Unparsed topic passed through: {Topic}, clientId={ClientId}, bytes={PayloadBytes}, truncated={PayloadTruncated}, payload={Payload:l}",
                    topicName, e.ClientId, bytes, truncated, text);
                return;
            }

            // 중계 모드에서 다원 → 클라이언트로 주입한 메시지는 장치 → 서버 메시지가 아니므로 핸들러에 분배하지 않는다.
            if (DawonMqttRelay.IsInjected(e.ClientId))
                return;

            try
            {
                if (topic.Kind == DeviceTopicKind.Lwt)
                {
                    await handler.OfflineAsync(topic, e.CancellationToken);
                    return;
                }

                MqttEnvelope? envelope = TryDeserialize(topicName, e.ApplicationMessage.Payload);
                if (envelope is null)
                    return;

                Task task = envelope.Msg.Operation switch
                {
                    MqttOperation.Register => handler.RegisterAsync(topic, envelope, e.CancellationToken),
                    MqttOperation.Deregister => handler.DeregisterAsync(topic, envelope, e.CancellationToken),
                    MqttOperation.TimeSync => handler.TimeSyncAsync(topic, envelope, e.CancellationToken),
                    MqttOperation.Notify or MqttOperation.NotifyShort => handler.NotifyAsync(topic, envelope, e.CancellationToken),
                    _ => LogUnknown(topic, envelope, e),
                };
                await task;
            }
            catch (OperationCanceledException) when (e.CancellationToken.IsCancellationRequested)
            {
                // 브로커 종료 중 취소는 정상 흐름이라 Error로 남기지 않는다.
            }
            catch (Exception ex)
            {
                // 핸들러 예외가 브로커 연결 처리로 번지지 않게 로그만 남긴다.
                logger.LogError(ex, "Device message handling failed: {Topic}", topicName);
            }
        }

        /// <summary>
        /// JSON 오류와 <c>msg</c>·<c>o</c>·<c>e</c> 누락은 해석 실패로 경고 로그 후 null을 반환한다(STJ는 nullable 주석을 강제하지 않는다).
        /// </summary>
        private MqttEnvelope? TryDeserialize(string topicName, ReadOnlySequence<byte> payload)
        {
            MqttEnvelope? envelope;
            try
            {
                Utf8JsonReader reader = new Utf8JsonReader(payload);
                envelope = JsonSerializer.Deserialize<MqttEnvelope>(ref reader);
            }
            catch (JsonException ex)
            {
                logger.LogWarning("Device message is not valid JSON: {Topic}, {Error}", topicName, ex.Message);
                return null;
            }

            if (envelope?.Msg?.Operation is null || envelope.Msg.Elements is null)
            {
                logger.LogWarning("Device message has no msg.o or msg.e: {Topic}", topicName);
                return null;
            }
            return envelope;
        }

        /// <summary>
        /// 알 수 없는 오퍼레이션은 실장비 메시지 형태 확인용으로 토픽·ClientId·원본 페이로드와 함께 경고로 남긴다.
        /// </summary>
        private Task LogUnknown(DeviceTopic topic, MqttEnvelope envelope, InterceptingPublishEventArgs e)
        {
            (long bytes, bool truncated, string text) = DescribePayload(e.ApplicationMessage.Payload);
            logger.LogWarning(
                "Unknown device operation {Operation}: {DeviceKey}, topic={Topic}, clientId={ClientId}, bytes={PayloadBytes}, truncated={PayloadTruncated}, payload={Payload:l}",
                envelope.Msg.Operation, topic.DeviceKey, e.ApplicationMessage.Topic, e.ClientId, bytes, truncated, text);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 원본 페이로드를 UTF-8로 디코딩한다. <see cref="MaxLoggedPayloadBytes"/>를 넘으면 앞부분만 디코딩한다
        /// (경계에서 잘린 문자는 대체 문자가 된다).
        /// </summary>
        private static (long Bytes, bool Truncated, string Text) DescribePayload(ReadOnlySequence<byte> payload)
        {
            bool truncated = payload.Length > MaxLoggedPayloadBytes;
            ReadOnlySequence<byte> logged = truncated ? payload.Slice(0, MaxLoggedPayloadBytes) : payload;
            return (payload.Length, truncated, Encoding.UTF8.GetString(logged));
        }
    }
}
