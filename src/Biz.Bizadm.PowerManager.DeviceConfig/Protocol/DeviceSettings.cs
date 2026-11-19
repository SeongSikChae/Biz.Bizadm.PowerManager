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

        /// <summary>ConnectAp 흐름. 기본 V3(#13).</summary>
        public DeviceProtocol Protocol { get; init; } = DeviceProtocol.V3;

        /// <summary>V1 설정 JSON의 mqtt_key(복호화된 값). V1일 때 필수이며 로그·예외에 넣지 않는다.</summary>
        public string? MqttKey { get; init; }

        // record 기본 ToString이 MqttKey 값을 내지 않도록 가린다(kms-config-secrets: 민감값을 로그·예외에 남기지 않음)
        private bool PrintMembers(System.Text.StringBuilder builder)
        {
            builder.Append($"ApiServerAddr = {ApiServerAddr}, ApiServerPort = {ApiServerPort}, ServerAddr = {ServerAddr}, ServerPort = {ServerPort}, ");
            builder.Append($"SslSupport = {SslSupport}, UserId = {UserId}, Company = {Company}, Model = {Model}, Lati = {Lati}, Long = {Long}, ");
            builder.Append($"Topic = {Topic}, Protocol = {Protocol}, MqttKey = {(MqttKey is null ? "null" : "***")}");
            return true;
        }

        /// <summary>
        /// 설정 JSON 한 줄 + '\n'. masked면 pass·mqtt_key를 "***"로 바꾼다(로그용).
        /// 키·순서와 직렬화(System.Text.Json 기본 인코더: + &amp; &lt; &gt; ' 와 비ASCII를 \uXXXX로)는
        /// 참고 구현 PowerManagerConfig와 같다. V1은 MqttConfiguration(ConfigratorV1), 그 밖은 MqttConfigurationV2(V2~V4)다.
        /// </summary>
        public string ToJson(string mac, string ssid, string pass, bool masked = false)
        {
            if (Protocol == DeviceProtocol.V1)
            {
                Dictionary<string, string> v1 = new()
                {
                    ["server_addr"] = ServerAddr,
                    ["server_port"] = ServerPort.ToString(CultureInfo.InvariantCulture),
                    ["ssl_support"] = SslSupport ? "yes" : "no",
                    ["ssid"] = ssid,
                    ["pass"] = masked ? "***" : pass,
                    ["mqtt_key"] = masked ? "***" : MqttKey ?? "",
                    ["company"] = Company,
                    ["model"] = Model,
                    ["topic"] = Topic,
                };
                return JsonSerializer.Serialize(v1) + "\n";
            }

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
