namespace Biz.Bizadm.PowerManager.Server.Spa.Data
{
    /// <summary>
    /// 등록 요청으로 발급한 장치별 MQTT 키. mac(소문자 12자리 16진수)이 키다.
    /// </summary>
    public sealed class DeviceRegistration
    {
        public string Mac { get; set; } = null!;

        public string UserId { get; set; } = string.Empty;

        public string ModelId { get; set; } = string.Empty;

        public string Company { get; set; } = string.Empty;

        public string MqttKey { get; set; } = null!;

        public DateTimeOffset RegisteredAt { get; set; }

        public DateTimeOffset LastRequestedAt { get; set; }
    }
}
