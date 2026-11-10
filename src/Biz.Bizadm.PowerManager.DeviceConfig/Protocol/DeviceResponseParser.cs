using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Protocol
{
    /// <summary>장치 → PC 응답 해석.</summary>
    public static partial class DeviceResponseParser
    {
        /// <summary>"[DUT->PC] START_OK:&lt;mac 12hex&gt;#"에서 MAC을 꺼낸다.</summary>
        public static string ParseStartOk(string frame)
        {
            Match match = StartOkRegex().Match(frame);
            return match.Success
                ? match.Groups["mac"].Value
                : throw new DeviceProtocolException($"Unexpected handshake response: {frame}");
        }

        /// <summary>{"mac":"…","respone":"OK"}(펌웨어 오타 키)이면 frame을 그대로 돌려준다.</summary>
        public static string EnsureOk(string frame)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(frame);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("respone", out JsonElement value)
                    && value.ValueKind == JsonValueKind.String && value.GetString() == "OK")
                    return frame;
            }
            catch (JsonException)
            {
                // '#' 프레임 등 JSON 아님
            }
            throw new DeviceProtocolException($"Device response is not OK: {frame}");
        }

        /// <summary>{"SCAN_AP_LIST":{"ssid:-51","ssid2:-70"}} — 중괄호 안이 키 없는 문자열 나열이라 정식 JSON이 아니다.</summary>
        public static IReadOnlyList<WifiNetwork> ParseScanApList(string frame)
        {
            int key = frame.IndexOf("SCAN_AP_LIST", StringComparison.Ordinal);
            if (key < 0)
                throw new DeviceProtocolException($"SCAN_AP_LIST not found: {frame}");
            int open = frame.IndexOf('{', key);
            if (open < 0 || !frame.EndsWith("}}", StringComparison.Ordinal))
                throw new DeviceProtocolException($"Invalid SCAN_AP_LIST frame: {frame}");
            string body = frame[(open + 1)..^2];
            return ScanEntryRegex().Matches(body)
                .Select(m => new WifiNetwork(m.Groups["ssid"].Value, int.Parse(m.Groups["rssi"].Value)))
                .ToList();
        }

        [GeneratedRegex(@"^\[DUT->PC\] START_OK:(?<mac>[0-9A-Fa-f]{12})#$")]
        private static partial Regex StartOkRegex();

        [GeneratedRegex(@"""(?<ssid>[^""]*):(?<rssi>-?\d+)""")]
        private static partial Regex ScanEntryRegex();
    }
}
