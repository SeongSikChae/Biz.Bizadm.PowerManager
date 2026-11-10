using System.Text.Json.Serialization;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Messages
{
    /// <summary>
    /// ConnectAp 설정 JSON. 키 순서는 참고 구현(PowerManagerConfig V3)과 같다.
    /// </summary>
    internal sealed class DeviceSettingsMessage
    {
        [JsonPropertyName("mac")] public string? Mac { get; init; }
        [JsonPropertyName("api_server_addr")] public string? ApiServerAddr { get; init; }
        [JsonPropertyName("api_server_port")] public string? ApiServerPort { get; init; }
        [JsonPropertyName("server_addr")] public string? ServerAddr { get; init; }
        [JsonPropertyName("server_port")] public string? ServerPort { get; init; }
        [JsonPropertyName("ssl_support")] public string? SslSupport { get; init; }
        [JsonPropertyName("ssid")] public string? Ssid { get; init; }
        [JsonPropertyName("pass")] public string? Pass { get; init; }
        [JsonPropertyName("user_id")] public string? UserId { get; init; }
        [JsonPropertyName("company")] public string? Company { get; init; }
        [JsonPropertyName("model")] public string? Model { get; init; }
        [JsonPropertyName("lati")] public string? Latitude { get; init; }
        [JsonPropertyName("long")] public string? Longitude { get; init; }
        [JsonPropertyName("topic")] public string? Topic { get; init; }
        [JsonPropertyName("mqtt_key")] public string? MqttKey { get; init; }
        [JsonPropertyName("LFota_Url")] public string? FotaUrl { get; init; }
        [JsonPropertyName("LFota_Addr")] public string? FotaAddr { get; init; }
        [JsonPropertyName("LFota_Port")] public string? FotaPort { get; init; }
        [JsonPropertyName("LFota_Version")] public string? FotaVersion { get; init; }

        public static DeviceSettingsMessage From(DeviceSettings settings, WifiProfile wifi, string mac) => new()
        {
            Mac = mac,
            ApiServerAddr = settings.ApiServerAddr,
            ApiServerPort = settings.ApiServerPort,
            ServerAddr = settings.MqttServerAddr,
            ServerPort = settings.MqttServerPort,
            SslSupport = settings.SslSupport,
            Ssid = wifi.Ssid,
            Pass = wifi.Pass,
            UserId = settings.UserId,
            Company = settings.Company,
            Model = settings.Model,
            Latitude = settings.Latitude,
            Longitude = settings.Longitude,
            Topic = settings.Topic,
            MqttKey = NullIfEmpty(settings.MqttKey),
            FotaUrl = NullIfEmpty(settings.Fota?.Url),
            FotaAddr = NullIfEmpty(settings.Fota?.Addr),
            FotaPort = NullIfEmpty(settings.Fota?.Port),
            FotaVersion = NullIfEmpty(settings.Fota?.Version),
        };

        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// command JSON. 참고 구현과 같이 mac을 먼저 둔다(connectap). mac이 없으면 생략한다.
    /// </summary>
    internal sealed class DeviceCommandMessage(string command, string? mac = null)
    {
        [JsonPropertyName("mac")] public string? Mac { get; } = mac;
        [JsonPropertyName("command")] public string Command { get; } = command;
    }
}
