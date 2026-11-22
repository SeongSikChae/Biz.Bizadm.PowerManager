using Biz.Bizadm.PowerManager.Core.Devices;
using Biz.Bizadm.PowerManager.Core.Mqtt;
using Biz.Bizadm.PowerManager.Server.Core;
using Biz.Bizadm.PowerManager.Server.Spa.Devices;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System.Buffers;
using System.Collections;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Mqtt
{
    /// <summary>
    /// 내장 MQTT 브로커. MQTT 포트와 (TLS 사용 시) MQTTS 포트를 열고, 장치 → 서버 메시지를 <see cref="IDeviceMessageHandler"/>로 분배한다.
    /// 등록 장치가 등록 키로 접속할 때만 받고(<see cref="OnValidatingConnectionAsync"/>), 접속마다 자격 증명 판정을 로그로 남긴다.
    /// 전달 문제를 가리려고 클라이언트의 구독·구독 해제와, 서버 → 장치 메시지가 어느 클라이언트 큐에 들어갔는지·구독자가 없었는지를 로그로 남긴다
    /// (구독·전달 동작은 바꾸지 않는다).
    /// 장비가 나뉜 패킷을 처리하지 못하므로 두 엔드포인트 모두 MQTT 패킷을 한 번에 쓴다(<see cref="BuildServerOptions"/>).
    /// 접속·끊김과 장치 → 서버 메시지는 <see cref="DeviceStateStore"/>에, 서버 → 장치 메시지의 전달 결과는 <see cref="DownlinkDeliveryTracker"/>에도 알린다.
    /// 서버가 주입한 메시지(<c>CustomSessionItems</c> 없이 주입한 것, 예: tisyn 응답)는 해석 불가 토픽 로그를 남기지 않는다(<see cref="IsInjected"/>).
    /// </summary>
    public sealed class MqttBrokerHostedService(AppConfiguration configuration, IDeviceMessageHandler handler,
        DeviceRegistrationService registrations, MqttServerAccessor accessor, DeviceStateStore states, DeviceRelayReader relayReader,
        DownlinkDeliveryTracker deliveries, ILogger<MqttBrokerHostedService> logger)
        : IHostedService, IAsyncDisposable
    {
        /// <summary>
        /// 로그에 남길 원본 페이로드의 최대 바이트 수. 넘으면 앞부분만 디코딩한다.
        /// </summary>
        private const int MaxLoggedPayloadBytes = 4096;

        private MqttServer? server;
        private X509Certificate2? certificate;

        /// <summary>
        /// 브로커의 <see cref="MqttServer.ServerSessionItems"/>. 주입 메시지 판정(<see cref="IsInjected"/>)에 쓴다.
        /// 종료 중에 주입된 메시지도 가리도록 <see cref="server"/>와 달리 브로커를 멈춘 뒤에 비운다.
        /// </summary>
        private IDictionary? serverSessionItems;

        /// <summary>
        /// 브로커 서버 옵션을 만든다. 비TLS 엔드포인트를 늘 열고, <paramref name="certificate"/>가 있으면 TLS 1.2 엔드포인트도 연다.
        /// 두 엔드포인트 모두 패킷 분할을 끈다. 분할을 허용하면 MQTTnet이 페이로드 있는 패킷을 머리와 페이로드로 나눠 두 번 쓰고
        /// (TLS에서는 레코드 두 개), 장비 MQTT 라이브러리는 여러 수신에 걸친 패킷을 합치지 않는 것으로 보여 서버 → 장치 메시지가 버려진다.
        /// </summary>
        public static MqttServerOptions BuildServerOptions(AppConfiguration configuration, X509Certificate2? certificate)
        {
            MqttServerOptionsBuilder optionsBuilder = new MqttServerFactory().CreateServerOptionsBuilder()
                .WithDefaultEndpoint()
                .WithDefaultEndpointPort(configuration.MqttPort!.Value);

            if (certificate is not null)
            {
                optionsBuilder = optionsBuilder
                    .WithEncryptedEndpoint()
                    .WithEncryptedEndpointPort(configuration.MqttsPort!.Value)
                    .WithEncryptionCertificate(certificate)          // 체인·cipher는 적용하지 않는다(Kestrel만)
                    .WithEncryptionSslProtocol(SslProtocols.Tls12);  // 장치 mbedTLS는 TLS 1.0~1.2
            }

            return optionsBuilder.WithoutPacketFragmentation().Build();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            AppConfiguration c = configuration;
            if (c.Tls?.UseTls == true)
                certificate = TlsCertificateLoader.Load(c.Tls, c.ContentRootPath);

            server = new MqttServerFactory().CreateMqttServer(BuildServerOptions(c, certificate));
            serverSessionItems = server.ServerSessionItems;
            server.InterceptingPublishAsync += OnPublishAsync;
            server.ClientConnectedAsync += OnClientConnectedAsync;
            server.ClientDisconnectedAsync += OnClientDisconnectedAsync;
            server.ValidatingConnectionAsync += OnValidatingConnectionAsync;
            server.InterceptingSubscriptionAsync += OnSubscriptionAsync;
            server.InterceptingUnsubscriptionAsync += OnUnsubscriptionAsync;
            server.ApplicationMessageEnqueuedOrDroppedAsync += OnEnqueuedOrDroppedAsync;
            server.ApplicationMessageNotConsumedAsync += OnNotConsumedAsync;
            await server.StartAsync();
            // 핸들러가 서버 → 장치 메시지(tisyn 응답)를 주입할 수 있게 시작한 브로커를 넘긴다.
            accessor.Server = server;
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
                accessor.Server = null;
                current.InterceptingPublishAsync -= OnPublishAsync;
                current.ClientConnectedAsync -= OnClientConnectedAsync;
                current.ClientDisconnectedAsync -= OnClientDisconnectedAsync;
                current.ValidatingConnectionAsync -= OnValidatingConnectionAsync;
                current.InterceptingSubscriptionAsync -= OnSubscriptionAsync;
                current.InterceptingUnsubscriptionAsync -= OnUnsubscriptionAsync;
                current.ApplicationMessageEnqueuedOrDroppedAsync -= OnEnqueuedOrDroppedAsync;
                current.ApplicationMessageNotConsumedAsync -= OnNotConsumedAsync;
                await current.StopAsync();
                current.Dispose();
                serverSessionItems = null;
            }
            certificate?.Dispose();
            certificate = null;
        }

        /// <summary>
        /// 접속 인증. ClientId <c>DAWONDNS-{mac}</c>의 mac이 등록돼 있으면 비밀번호를 등록 키와 비교해 Match/Mismatch, 아니면 Unknown으로 판정한다.
        /// Match만 받고, Mismatch·Unknown은 <see cref="MqttConnectReasonCode.BadUserNameOrPassword"/>(MQTT 3.1·3.1.1 반환 코드 4),
        /// 확인 중 예외는 판정을 Unknown으로 둔 채 <see cref="MqttConnectReasonCode.ServerUnavailable"/>(반환 코드 3)로 거부한다.
        /// 판정은 접속마다(거부 포함) 로그 하나로 남기며, 비밀번호 값은 남기지 않는다.
        /// </summary>
        private async Task OnValidatingConnectionAsync(ValidatingConnectionEventArgs e)
        {
            string? password = e.Password;
            bool hasPassword = !string.IsNullOrEmpty(password);
            string check = "Unknown";
            MqttConnectReasonCode reasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            try
            {
                if (DeviceIdentity.TryParseClientId(e.ClientId, out string? mac)
                    && await registrations.FindMqttKeyAsync(mac, CancellationToken.None) is string key)
                {
                    bool match = hasPassword && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(password!), Encoding.UTF8.GetBytes(key));
                    check = match ? "Match" : "Mismatch";
                    if (match)
                        reasonCode = MqttConnectReasonCode.Success;
                }
            }
            catch (Exception ex)
            {
                // 확인하지 못한 접속은 받지 않는다(fail-closed).
                logger.LogError(ex, "MQTT connection credential check failed: {ClientId}", e.ClientId);
                reasonCode = MqttConnectReasonCode.ServerUnavailable;
            }

            logger.LogInformation("MQTT client connecting: clientId={ClientId}, userName={UserName}, hasPassword={HasPassword}, passwordCheck={PasswordCheck}",
                e.ClientId, e.UserName, hasPassword, check);
            e.ReasonCode = reasonCode;
        }

        // 아래 여섯 처리기는 MQTTnet이 예외를 잡지 않고 부르므로(전달 이벤트의 예외는 남은 구독자 전달을, 구독 이벤트의 예외는
        // SUBSCRIBE·UNSUBSCRIBE 처리를 막는다) 예외를 밖으로 내보내지 않는다. 구독 해제 이벤트는 브로커의 잠금 안에서 불리므로
        // 모두 await 없이 동기로 끝낸다. 이벤트 인자의 처리 여부 속성은 바꾸지 않는다.

        private Task OnClientConnectedAsync(ClientConnectedEventArgs e)
        {
            try
            {
                states.Connected(e.ClientId, e.RemoteEndPoint?.ToString());
            }
            catch (Exception ex)
            {
                TryLogHandlerError(ex, nameof(OnClientConnectedAsync));
            }
            return Task.CompletedTask;
        }

        private Task OnClientDisconnectedAsync(ClientDisconnectedEventArgs e)
        {
            try
            {
                states.Disconnected(e.ClientId, e.RemoteEndPoint?.ToString(), e.DisconnectType);
            }
            catch (Exception ex)
            {
                TryLogHandlerError(ex, nameof(OnClientDisconnectedAsync));
            }
            return Task.CompletedTask;
        }

        private Task OnSubscriptionAsync(InterceptingSubscriptionEventArgs e)
        {
            try
            {
                logger.LogInformation("MQTT client subscribe: clientId={ClientId}, topicFilter={TopicFilter}, qos={Qos}",
                    e.ClientId, e.TopicFilter.Topic, (int)e.TopicFilter.QualityOfServiceLevel);
            }
            catch (Exception ex)
            {
                TryLogHandlerError(ex, nameof(OnSubscriptionAsync));
            }
            return Task.CompletedTask;
        }

        private Task OnUnsubscriptionAsync(InterceptingUnsubscriptionEventArgs e)
        {
            try
            {
                logger.LogInformation("MQTT client unsubscribe: clientId={ClientId}, topicFilter={TopicFilter}", e.ClientId, e.Topic);
            }
            catch (Exception ex)
            {
                TryLogHandlerError(ex, nameof(OnUnsubscriptionAsync));
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 서버 → 장치 메시지를 구독 클라이언트 큐에 넣었으면 받는 클라이언트마다 남긴다. 버림(<c>IsDropped</c>)은 남기지 않는다
        /// (기본 넘침 정책 <c>DropOldestQueuedMessage</c>에서는 생기지 않는다).
        /// </summary>
        private Task OnEnqueuedOrDroppedAsync(ApplicationMessageEnqueuedEventArgs e)
        {
            try
            {
                string topic = e.ApplicationMessage.Topic;
                if (!e.IsDropped && DeviceTopic.IsToDevice(topic))
                {
                    deliveries.Enqueued(e.SenderClientId, e.ReceiverClientId, e.ApplicationMessage);
                    logger.LogInformation("Downlink message enqueued: sender={SenderClientId}, receiver={ReceiverClientId}, topic={Topic}",
                        e.SenderClientId, e.ReceiverClientId, topic);
                }
            }
            catch (Exception ex)
            {
                TryLogHandlerError(ex, nameof(OnEnqueuedOrDroppedAsync));
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 서버 → 장치 메시지에 맞는 구독이 없어 아무 클라이언트에게도 가지 않았으면 남긴다.
        /// 장치 → 서버 메시지도 구독자가 없어 매번 이 이벤트가 오지만 토픽으로 걸러 남기지 않는다.
        /// </summary>
        private Task OnNotConsumedAsync(ApplicationMessageNotConsumedEventArgs e)
        {
            try
            {
                string topic = e.ApplicationMessage.Topic;
                if (DeviceTopic.IsToDevice(topic))
                {
                    deliveries.NotConsumed(e.SenderId, e.ApplicationMessage);
                    logger.LogWarning("Downlink message not consumed: sender={SenderClientId}, topic={Topic}", e.SenderId, topic);
                }
            }
            catch (Exception ex)
            {
                TryLogHandlerError(ex, nameof(OnNotConsumedAsync));
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 기록 처리기의 예외를 Error 로그로 한 번 남기려 하고, 그것도 실패하면 무시한다(브로커 처리로 번지지 않게).
        /// </summary>
        private void TryLogHandlerError(Exception exception, string handlerName)
        {
            try
            {
                logger.LogError(exception, "MQTT broker log handler failed: {Handler}", handlerName);
            }
            catch
            {
                // 로그 자체가 실패하면 할 수 있는 일이 없다.
            }
        }

        private async Task OnPublishAsync(InterceptingPublishEventArgs e)
        {
            string topicName = e.ApplicationMessage.Topic;
            if (!DeviceTopic.TryParse(topicName, out DeviceTopic? topic))
            {
                // 서버가 주입한 메시지는 토픽·페이로드를 서버가 정했고 응답·전달은 R5·R12·R13 로그에 남으므로 기록하지 않고 그대로 전달한다.
                if (IsInjected(e))
                    return;

                // 서버→장치 토픽 등은 그대로 전달한다. 실장비 토픽 형태 확인용으로 ClientId·원본 페이로드와 함께 Information으로 남긴다.
                (long bytes, bool truncated, string text) = DescribePayload(e.ApplicationMessage.Payload);
                logger.LogInformation(
                    "Unparsed topic passed through: {Topic}, clientId={ClientId}, bytes={PayloadBytes}, truncated={PayloadTruncated}, payload={Payload:l}",
                    topicName, e.ClientId, bytes, truncated, text);
                return;
            }

            try
            {
                if (topic.Kind == DeviceTopicKind.Lwt)
                {
                    ApplyToState(e.ClientId, topic, null);
                    await handler.OfflineAsync(topic, e.CancellationToken);
                    return;
                }

                MqttEnvelope? envelope = TryDeserialize(topicName, e.ApplicationMessage.Payload);
                ApplyToState(e.ClientId, topic, envelope);
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
        /// 장비의 메시지를 상태 저장소에 반영하고, 필요하면 릴레이 읽기를 시작한다. 실패해도 메시지 분배를 막지 않는다.
        /// </summary>
        private void ApplyToState(string? clientId, DeviceTopic topic, MqttEnvelope? envelope)
        {
            try
            {
                if (states.ApplyMessage(clientId, topic, envelope) is RelayReadRequest request)
                    relayReader.Start(request);
            }
            catch (Exception ex)
            {
                TryLogHandlerError(ex, nameof(ApplyToState));
            }
        }

        /// <summary>
        /// 서버가 브로커에 주입한 메시지인지 가린다. MQTTnet은 <c>CustomSessionItems</c> 없이 주입한 메시지에 <see cref="MqttServer.ServerSessionItems"/>를,
        /// 클라이언트 PUBLISH와 Will에는 접속마다 새로 만든 세션 항목을 넘긴다. 그래서 ClientId와 상관없이 인스턴스 비교로 가린다.
        /// <c>CustomSessionItems</c>를 주고 주입한 메시지는 주입으로 보지 않는다(해석 불가 토픽이면 로그를 남긴다).
        /// </summary>
        private bool IsInjected(InterceptingPublishEventArgs e)
        {
            IDictionary? items = serverSessionItems;
            return items is not null && ReferenceEquals(e.SessionItems, items);
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
