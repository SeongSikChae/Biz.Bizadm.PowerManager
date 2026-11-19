using MQTTnet.Packets;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System.Reflection;

namespace Biz.Bizadm.PowerManager.Server.Spa.Relay
{
    /// <summary>
    /// MQTTnet 5.2 공개 API에는 CONNECT의 Will(토픽·페이로드·QoS·retain)이 없어, <see cref="ValidatingConnectionEventArgs"/>의
    /// 비공개 필드 <c>_connectPacket</c>을 리플렉션으로 읽는다. MQTTnet 버전이 바뀌어 필드가 없으면 실패를 돌려준다.
    /// </summary>
    public static class MqttConnectPacketReader
    {
        public sealed record Will(string Topic, byte[] Payload, MqttQualityOfServiceLevel QoS, bool Retain);

        private static readonly FieldInfo? ConnectPacketField =
            typeof(ValidatingConnectionEventArgs).GetField("_connectPacket", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// CONNECT 패킷을 읽을 수 있으면 true. <paramref name="will"/>은 Will 플래그가 없으면 null.
        /// </summary>
        public static bool TryReadWill(ValidatingConnectionEventArgs e, out Will? will)
        {
            will = null;
            if (ConnectPacketField?.GetValue(e) is not MqttConnectPacket packet)
                return false;
            if (packet.WillFlag)
                will = new Will(packet.WillTopic, packet.WillMessage ?? [], packet.WillQoS, packet.WillRetain);
            return true;
        }
    }
}
