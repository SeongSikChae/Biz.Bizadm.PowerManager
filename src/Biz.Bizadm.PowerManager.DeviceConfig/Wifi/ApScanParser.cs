using System.Text.RegularExpressions;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Wifi
{
    /// <summary>
    /// 장치 apscan으로 찾은 AP.
    /// </summary>
    public sealed record ScannedAp(string Ssid, int Rssi);

    /// <summary>
    /// apscan 응답 <c>{"SCAN_AP_LIST":{"&lt;ssid&gt;:&lt;rssi&gt;",...}}</c>을 해석한다.
    /// 객체 안에 키 없이 문자열을 나열하므로 엄격한 JSON이 아니다(정적 분석 문자열 기준, 실장비 미확인).
    /// </summary>
    public static partial class ApScanParser
    {
        private const string ListKey = "\"SCAN_AP_LIST\"";

        /// <exception cref="DeviceProtocolException">SCAN_AP_LIST 키가 없다.</exception>
        public static IReadOnlyList<ScannedAp> Parse(string response)
        {
            int start = response.IndexOf(ListKey, StringComparison.Ordinal);
            if (start < 0)
                throw new DeviceProtocolException($"Unexpected apscan response: {response}");

            List<ScannedAp> result = [];
            foreach (Match match in QuotedRegex().Matches(response, start + ListKey.Length))
            {
                // SSID에 ':'가 있을 수 있으므로 마지막 ':' 기준으로 나눈다
                string entry = match.Groups[1].Value;
                int separator = entry.LastIndexOf(':');
                if (separator <= 0 || !int.TryParse(entry.AsSpan(separator + 1), out int rssi))
                    continue;
                result.Add(new ScannedAp(entry[..separator], rssi));
            }
            return result;
        }

        [GeneratedRegex("\"([^\"]*)\"")]
        private static partial Regex QuotedRegex();
    }
}
