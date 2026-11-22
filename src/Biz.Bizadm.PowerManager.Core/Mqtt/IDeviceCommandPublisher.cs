namespace Biz.Bizadm.PowerManager.Core.Mqtt
{
    /// <summary>
    /// 서버 → 장치 MQTT 명령 발행.
    /// </summary>
    public interface IDeviceCommandPublisher
    {
        /// <summary>
        /// <paramref name="target"/>의 서버 → 장치 토픽에 retain 없이 제어 메시지를 보내고, 대상 장비 클라이언트의 전송 큐에 들어갔는지 돌려준다.
        /// </summary>
        Task<DevicePublishResult> PublishAsync(DeviceCommandTarget target, string operation, IReadOnlyList<MqttElement> elements, CancellationToken ct);
    }
}
