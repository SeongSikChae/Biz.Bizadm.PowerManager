using Biz.Bizadm.PowerManager.Core.Mqtt;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Mqtt
{
    /// <summary>
    /// 기본 <see cref="IDeviceMessageHandler"/>. 수신 내용을 로그로 남기고, 시간 동기 요청(<c>tisyn</c>)에는
    /// 그 장치의 서버 → 장치 토픽으로 <c>w</c> <c>/99/0/1</c>(UTC Unix epoch 초)을 보낸다.
    /// </summary>
    public sealed class LoggingDeviceMessageHandler(ILogger<LoggingDeviceMessageHandler> logger, MqttServerAccessor accessor,
        TimeProvider timeProvider) : IDeviceMessageHandler
    {
        /// <summary>
        /// 서버가 주입하는 메시지의 보낸 쪽 ClientId.
        /// </summary>
        public const string SenderClientId = "power-manager";

        /// <summary>
        /// 시간 동기 응답을 쓰는 리소스 경로(근거 문서 b540-mqtt-topic.md 「시간 동기」 절).
        /// </summary>
        public const string TimeResourcePath = "/99/0/1";

        public Task RegisterAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct) => Log(topic, envelope);

        public Task DeregisterAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct) => Log(topic, envelope);

        public async Task TimeSyncAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct)
        {
            await Log(topic, envelope);
            await RespondTimeSyncAsync(topic, envelope, ct);
        }

        public Task NotifyAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct) => Log(topic, envelope);

        public Task OfflineAsync(DeviceTopic topic, CancellationToken ct)
        {
            logger.LogInformation("Device offline (LWT): prefix={Prefix}, device={DeviceKey}", topic.Prefix, topic.DeviceKey);
            return Task.CompletedTask;
        }

        private Task Log(DeviceTopic topic, MqttEnvelope envelope)
        {
            logger.LogInformation("Device message {Operation}: prefix={Prefix}, device={DeviceKey}, sid={Sid}, elements={Elements}",
                envelope.Msg.Operation, topic.Prefix, topic.DeviceKey, envelope.Sid,
                string.Join(", ", envelope.Msg.Elements.Select(e => $"{e.Path}={e.StringValue}")));
            return Task.CompletedTask;
        }

        /// <summary>
        /// 요청 sid(없으면 "0")와 현재 UTC epoch 초로 <c>w</c> 응답을 만들어 retain 없이 QoS 0으로 주입한다.
        /// 실패해도 Error 로그만 남기고 브로커로 예외를 넘기지 않는다(종료 중 취소는 넘긴다).
        /// </summary>
        private async Task RespondTimeSyncAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct)
        {
            string responseTopic = DeviceTopic.ToDevice(topic.Prefix, topic.DeviceKey);
            MqttServer? server = accessor.Server;
            if (server is null)
            {
                logger.LogError("Time sync response failed: device={DeviceKey}, topic={Topic}", topic.DeviceKey, responseTopic);
                return;
            }

            string sid = envelope.Sid ?? "0";
            string value = timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            string payload = JsonSerializer.Serialize(new MqttEnvelope(sid,
                new MqttMessage(MqttOperation.Write, [new MqttElement(TimeResourcePath, value)])));
            try
            {
                await server.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessage
                {
                    Topic = responseTopic,
                    Payload = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(payload)),
                    QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce,
                    Retain = false,
                })
                { SenderClientId = SenderClientId }, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Time sync response failed: device={DeviceKey}, topic={Topic}", topic.DeviceKey, responseTopic);
                return;
            }

            logger.LogInformation("Time sync response sent: device={DeviceKey}, topic={Topic}, sid={Sid}, value={Value}",
                topic.DeviceKey, responseTopic, sid, value);
        }
    }
}
