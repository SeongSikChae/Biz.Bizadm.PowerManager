using Biz.Bizadm.PowerManager.Core.Devices;
using Biz.Bizadm.PowerManager.Core.Mqtt;
using MQTTnet;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Mqtt
{
    /// <summary>
    /// 발행한 제어 메시지를 <c>sid</c>로 기억해, 브로커 전달 이벤트로 받은 결과(대상 장비 클라이언트의 큐에 넣음 / 구독자 없음)와 짝짓는다.
    /// </summary>
    public sealed class DownlinkDeliveryTracker
    {
        private sealed record Pending(string Mac, TaskCompletionSource<bool> Result);

        private readonly ConcurrentDictionary<string, Pending> pending = new(StringComparer.Ordinal);

        /// <summary>
        /// <paramref name="sid"/> 메시지의 결과를 기다리기 시작한다. 대상 클라이언트 큐에 들어가면 true, 구독자가 없으면 false로 끝난다.
        /// 다 기다렸으면 <see cref="Forget"/>를 부른다.
        /// </summary>
        public Task<bool> Expect(string sid, string mac)
        {
            TaskCompletionSource<bool> result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[sid] = new Pending(mac, result);
            return result.Task;
        }

        public void Forget(string sid) => pending.TryRemove(sid, out _);

        /// <summary>
        /// 메시지가 <paramref name="receiverClientId"/>의 전송 큐에 들어갔다. 대상 장비의 클라이언트일 때만 결과로 본다.
        /// </summary>
        public void Enqueued(string? senderClientId, string? receiverClientId, MqttApplicationMessage message)
        {
            if (Find(senderClientId, message) is Pending found
                && DeviceIdentity.TryParseClientId(receiverClientId, out string? mac) && mac == found.Mac)
                found.Result.TrySetResult(true);
        }

        /// <summary>
        /// 메시지에 맞는 구독이 없었다.
        /// </summary>
        public void NotConsumed(string? senderClientId, MqttApplicationMessage message)
            => Find(senderClientId, message)?.Result.TrySetResult(false);

        private Pending? Find(string? senderClientId, MqttApplicationMessage message)
        {
            if (pending.IsEmpty || senderClientId != LoggingDeviceMessageHandler.SenderClientId || !DeviceTopic.IsToDevice(message.Topic))
                return null;
            try
            {
                Utf8JsonReader reader = new Utf8JsonReader(message.Payload);
                string? sid = JsonSerializer.Deserialize<MqttEnvelope>(ref reader)?.Sid;
                return sid is not null && pending.TryGetValue(sid, out Pending? found) ? found : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
