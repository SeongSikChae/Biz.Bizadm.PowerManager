using Biz.Bizadm.PowerManager.Core.Mqtt;
using Biz.Bizadm.PowerManager.Server.Core;
using MQTTnet.Server;
using System.Buffers;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Mqtt
{
    /// <summary>
    /// 내장 MQTT 브로커. MQTT 포트와 (TLS 사용 시) MQTTS 포트를 열고, 장치 → 서버 메시지를 <see cref="IDeviceMessageHandler"/>로 분배한다.
    /// 접속 인증은 하지 않는다.
    /// </summary>
    public sealed class MqttBrokerHostedService(AppConfiguration configuration, IDeviceMessageHandler handler, ILogger<MqttBrokerHostedService> logger)
        : IHostedService, IAsyncDisposable
    {
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
                await current.StopAsync();
                current.Dispose();
            }
            certificate?.Dispose();
            certificate = null;
        }

        private async Task OnPublishAsync(InterceptingPublishEventArgs e)
        {
            string topicName = e.ApplicationMessage.Topic;
            if (!DeviceTopic.TryParse(topicName, out DeviceTopic? topic))
            {
                // 서버→장치 토픽 등은 그대로 전달한다. 실장비 토픽 형태 확인용으로 Information으로 남긴다.
                logger.LogInformation("Unparsed topic passed through: {Topic}", topicName);
                return;
            }

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
                    MqttOperation.Notify => handler.NotifyAsync(topic, envelope, e.CancellationToken),
                    _ => LogUnknown(topic, envelope),
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

        private Task LogUnknown(DeviceTopic topic, MqttEnvelope envelope)
        {
            logger.LogWarning("Unknown device operation {Operation}: {DeviceKey}", envelope.Msg.Operation, topic.DeviceKey);
            return Task.CompletedTask;
        }
    }
}
