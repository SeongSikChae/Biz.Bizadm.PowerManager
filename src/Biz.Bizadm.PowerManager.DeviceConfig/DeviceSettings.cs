namespace Biz.Bizadm.PowerManager.DeviceConfig
{
    /// <summary>
    /// Wi‑Fi 프로필(복호화된 값).
    /// </summary>
    public sealed record WifiProfile(string Ssid, string Pass);

    /// <summary>
    /// 로컬 FOTA 지시(LFota_*). 값이 없는 항목은 전송하지 않는다.
    /// </summary>
    public sealed record DeviceFotaSettings(string? Url, string? Addr, string? Port, string? Version);

    /// <summary>
    /// ConnectAp에서 장치로 보내는 설정 값(라이브러리 입력 모델, YAML과 무관). 민감 값은 복호화된 상태다.
    /// </summary>
    public sealed class DeviceSettings
    {
        /// <summary>비우면 핸드셰이크 START_OK 응답의 MAC을 쓴다.</summary>
        public string? Mac { get; init; }
        public string? UserId { get; init; }
        public string? Company { get; init; }
        public string? Model { get; init; }
        public string? Latitude { get; init; }
        public string? Longitude { get; init; }
        public string? ApiServerAddr { get; init; }
        public string? ApiServerPort { get; init; }
        public string? MqttServerAddr { get; init; }
        public string? MqttServerPort { get; init; }
        public string? SslSupport { get; init; }
        public string? Topic { get; init; }
        public string? MqttKey { get; init; }
        public DeviceFotaSettings? Fota { get; init; }
        public IReadOnlyList<WifiProfile> WifiProfiles { get; init; } = [];
    }
}
