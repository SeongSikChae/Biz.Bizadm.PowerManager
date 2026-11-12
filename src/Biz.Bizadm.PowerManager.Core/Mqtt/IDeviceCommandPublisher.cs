namespace Biz.Bizadm.PowerManager.Core.Mqtt
{
    /// <summary>
    /// 서버 → 장치 MQTT 명령 발행.
    /// </summary>
    public interface IDeviceCommandPublisher
    {
        Task WriteAsync(string prefix, string deviceKey, IReadOnlyList<MqttElement> elements, CancellationToken ct);

        Task ChangeAsync(string prefix, string deviceKey, IReadOnlyList<MqttElement> elements, CancellationToken ct);

        Task RebootAsync(string prefix, string deviceKey, CancellationToken ct);
    }
}
