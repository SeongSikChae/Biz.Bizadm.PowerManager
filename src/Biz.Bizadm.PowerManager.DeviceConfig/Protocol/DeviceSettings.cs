using System.Globalization;
using System.Text.Encodings.Web;
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

        // 장치 파서가 \uXXXX를 처리한다는 근거가 없으므로 + & < > ' 와 비ASCII를 그대로 보낸다(#2와 같음).
        private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>설정 JSON 한 줄 + '\n'. masked면 pass·mqtt_key를 "***"로 바꾼다(로그용).</summary>
        public string ToJson(string mac, string ssid, string pass, string mqttKey, bool masked = false)
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
                ["mqtt_key"] = masked ? "***" : mqttKey,
            };
            return JsonSerializer.Serialize(values, JsonOptions) + "\n";
        }
    }
}
