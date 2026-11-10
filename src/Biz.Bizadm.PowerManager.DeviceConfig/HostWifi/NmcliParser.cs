using System.Text;

namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi
{
    /// <summary>
    /// <c>nmcli -t</c>(terse) 출력 해석. 필드 구분자는 ':'이고 값 안의 ':'·'\'는 '\'로 이스케이프된다.
    /// </summary>
    internal static class NmcliParser
    {
        public sealed record DeviceStatus(string Device, string Type, string State, string Connection);

        public sealed record WifiEntry(string Ssid, int Signal, bool InUse);

        public static IReadOnlyList<string> SplitTerse(string line)
        {
            List<string> fields = [];
            StringBuilder current = new();
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '\\' && i + 1 < line.Length)
                {
                    current.Append(line[++i]);
                    continue;
                }
                if (line[i] == ':')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                    continue;
                }
                current.Append(line[i]);
            }
            fields.Add(current.ToString());
            return fields;
        }

        /// <summary>
        /// <c>nmcli -t -f DEVICE,TYPE,STATE,CONNECTION device status</c>
        /// </summary>
        public static IReadOnlyList<DeviceStatus> ParseDeviceStatus(string output) => Lines(output)
            .Select(SplitTerse)
            .Where(fields => fields.Count >= 4)
            .Select(fields => new DeviceStatus(fields[0], fields[1], fields[2], fields[3]))
            .ToList();

        /// <summary>
        /// <c>nmcli -t -f SSID,SIGNAL,IN-USE dev wifi list</c>. SSID가 빈(숨김) 항목은 뺀다.
        /// </summary>
        public static IReadOnlyList<WifiEntry> ParseWifiList(string output) => Lines(output)
            .Select(SplitTerse)
            .Where(fields => fields.Count >= 3 && fields[0].Length > 0)
            .Select(fields => new WifiEntry(fields[0], int.TryParse(fields[1], out int signal) ? signal : 0, fields[2] == "*"))
            .ToList();

        private static IEnumerable<string> Lines(string output) =>
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0);
    }
}
