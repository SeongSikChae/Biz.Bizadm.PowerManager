using System.Text.Json;
using System.Text.RegularExpressions;

namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi
{
    /// <summary>
    /// macOS <c>system_profiler SPAirPortDataType -json</c>, <c>networksetup -listallhardwareports</c> 출력 해석.
    /// 키 이름은 OS 버전에 따라 다를 수 있다(실기 미검증).
    /// </summary>
    internal static partial class SystemProfilerParser
    {
        public sealed record AirPortInterface(string Name, HostWifiNetwork? Current, IReadOnlyList<HostWifiNetwork> Others);

        public static IReadOnlyList<AirPortInterface> ParseAirPort(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            List<AirPortInterface> result = [];
            if (!document.RootElement.TryGetProperty("SPAirPortDataType", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
                return result;

            foreach (JsonElement item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("spairport_airport_interfaces", out JsonElement interfaces) || interfaces.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (JsonElement airPort in interfaces.EnumerateArray())
                {
                    string? name = GetString(airPort, "_name");
                    if (name is null)
                        continue;
                    HostWifiNetwork? current = airPort.TryGetProperty("spairport_current_network_information", out JsonElement info)
                        ? ToNetwork(info) : null;
                    List<HostWifiNetwork> others = [];
                    if (airPort.TryGetProperty("spairport_airport_other_local_wireless_networks", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
                        others.AddRange(list.EnumerateArray().Select(ToNetwork).OfType<HostWifiNetwork>());
                    result.Add(new AirPortInterface(name, current, others));
                }
            }
            return result;
        }

        /// <summary>
        /// <c>Hardware Port: Wi-Fi</c> 다음 줄의 <c>Device: en0</c>.
        /// </summary>
        public static string? FindWifiDevice(string hardwarePorts)
        {
            Match match = WifiDeviceRegex().Match(hardwarePorts);
            return match.Success ? match.Groups["Device"].Value : null;
        }

        private static HostWifiNetwork? ToNetwork(JsonElement element)
        {
            string? ssid = GetString(element, "_name");
            if (string.IsNullOrEmpty(ssid))
                return null;
            // "-45 dBm / -90 dBm" → -45
            string? signalNoise = GetString(element, "spairport_signal_noise");
            Match match = signalNoise is null ? Match.Empty : LeadingIntRegex().Match(signalNoise);
            return new HostWifiNetwork(ssid, match.Success ? int.Parse(match.Value) : int.MinValue);
        }

        private static string? GetString(JsonElement element, string property) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;

        [GeneratedRegex(@"-?\d+")]
        private static partial Regex LeadingIntRegex();

        [GeneratedRegex(@"Hardware Port: (?:Wi-Fi|AirPort)\r?\nDevice: (?<Device>\S+)")]
        private static partial Regex WifiDeviceRegex();
    }
}
