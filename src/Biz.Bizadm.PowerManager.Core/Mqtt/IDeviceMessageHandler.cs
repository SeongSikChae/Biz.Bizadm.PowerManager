namespace Biz.Bizadm.PowerManager.Core.Mqtt
{
    /// <summary>
    /// 장치 → 서버 MQTT 메시지 처리.
    /// </summary>
    public interface IDeviceMessageHandler
    {
        Task RegisterAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct);

        Task DeregisterAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct);

        Task TimeSyncAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct);

        Task NotifyAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct);

        Task OfflineAsync(DeviceTopic topic, CancellationToken ct);
    }
}
