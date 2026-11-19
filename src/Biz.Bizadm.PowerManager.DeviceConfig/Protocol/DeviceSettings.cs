using System.Globalization;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Protocol
{
    /// <summary>ConnectAp 설정 JSON 값. 전송할 때 모든 값을 문자열로 바꾼다.</summary>
    public sealed record DeviceSettings
    {
        public required string ApiServerAddr { get; init; }
        public required int ApiServerPort { get; init; }
        public required string ServerAddr { get; init; }
        public required int ServerPort { get; init; }
        public required bool SslSupport { get; init; }
        public required string UserId { get; init; }
        public required string Company { get; init; }
        public required string Model { get; init; }
        public required string Lati { get; init; }
        public required string Long { get; init; }
        public required string Topic { get; init; }

        /// <summary>
        /// 설정 JSON 한 줄 + '\n'. masked면 pass를 "***"로 바꾼다(로그용).
        /// 키·순서와 직렬화(System.Text.Json 기본 인코더: + &amp; &lt; &gt; ' 와 비ASCII를 \uXXXX로)는
        /// 참고 구현 PowerManagerConfig ConfigratorV3(MqttConfigurationV2)와 같다. mqtt_key는 보내지 않는다(#11).
        /// </summary>
        public string ToJson(string mac, string ssid, string pass, bool masked = false)
        {
            Dictionary<string, string> values = new()
            {
                ["mac"] = mac,
                ["api_server_addr"] = ApiServerAddr,
                ["api_server_port"] = ApiServerPort.ToString(CultureInfo.InvariantCulture),
                ["server_addr"] = ServerAddr,
                ["server_port"] = ServerPort.ToString(CultureInfo.InvariantCulture),
                ["ssl_support"] = SslSupport ? "yes" : "no",
                ["ssid"] = ssid,
                ["pass"] = masked ? "***" : pass,
                ["user_id"] = UserId,
                ["company"] = Company,
                ["model"] = Model,
                ["lati"] = Lati,
                ["long"] = Long,
                ["topic"] = Topic,
            };
            return JsonSerializer.Serialize(values) + "\n";
        }
    }
}
