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
    /// 제어 메시지를 브로커에 주입하고(retain 없음, QoS 0), 대상 장비 클라이언트의 전송 큐에 들어갔는지 <see cref="DownlinkDeliveryTracker"/>로 판정한다.
    /// <c>sid</c>는 서버 실행마다 100부터 1씩 늘린 10진 문자열이다(장치가 보고에 쓰는 0·1·2와 겹치지 않게).
    /// </summary>
    public sealed class MqttDeviceCommandPublisher(MqttServerAccessor accessor, DownlinkDeliveryTracker tracker,
        ILogger<MqttDeviceCommandPublisher> logger) : IDeviceCommandPublisher
    {
        /// <summary>
        /// 주입 뒤 전달 이벤트를 기다리는 최대 시간. 그동안 대상 큐에 넣었다는 이벤트가 없으면 넣지 못한 것으로 본다.
        /// </summary>
        private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(3);

        private long lastSid = 99;

        public async Task<DevicePublishResult> PublishAsync(DeviceCommandTarget target, string operation, IReadOnlyList<MqttElement> elements, CancellationToken ct)
        {
            string topic = DeviceTopic.ToDevice(target.Prefix, target.DeviceKey);
            string sid = Interlocked.Increment(ref lastSid).ToString(CultureInfo.InvariantCulture);
            string elementsText = string.Join(", ", elements.Select(e => $"{e.Path}={e.StringValue}"));
            MqttServer? server = accessor.Server;
            if (server is null)
            {
                logger.LogError("Device command failed: mac={Mac}, topic={Topic}, sid={Sid}, operation={Operation}, elements={Elements}",
                    target.Mac, topic, sid, operation, elementsText);
                return new DevicePublishResult(false, topic, sid);
            }

            string payload = JsonSerializer.Serialize(new MqttEnvelope(sid, new MqttMessage(operation, elements)));
            bool enqueued;
            Task<bool> delivery = tracker.Expect(sid, target.Mac);
            try
            {
                await server.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessage
                {
                    Topic = topic,
                    Payload = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(payload)),
                    QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce,
                    Retain = false,
                })
                { SenderClientId = LoggingDeviceMessageHandler.SenderClientId }, ct);
                enqueued = await delivery.WaitAsync(DeliveryTimeout, ct);
            }
            catch (TimeoutException)
            {
                enqueued = false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Device command failed: mac={Mac}, topic={Topic}, sid={Sid}, operation={Operation}, elements={Elements}",
                    target.Mac, topic, sid, operation, elementsText);
                return new DevicePublishResult(false, topic, sid);
            }
            finally
            {
                tracker.Forget(sid);
            }

            if (enqueued)
            {
                logger.LogInformation("Device command sent: mac={Mac}, topic={Topic}, sid={Sid}, operation={Operation}, elements={Elements}",
                    target.Mac, topic, sid, operation, elementsText);
            }
            else
            {
                logger.LogWarning("Device command not enqueued: mac={Mac}, topic={Topic}, sid={Sid}, operation={Operation}, elements={Elements}",
                    target.Mac, topic, sid, operation, elementsText);
            }
            return new DevicePublishResult(enqueued, topic, sid);
        }
    }
}
