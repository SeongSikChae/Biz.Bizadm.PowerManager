using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Wifi
{
    /// <summary>
    /// 등록 Wi-Fi 중 장치 apscan 결과와 SSID가 대소문자까지 같은 것을 RSSI 높은 순으로 고른다.
    /// 같은 SSID가 여럿(메시·다중 AP)이면 묶지 않고 모두 후보로 두며, 선택 화면은 RSSI로 구분한다.
    /// </summary>
    public static class WifiProfileMatcher
    {
        public static IReadOnlyList<WifiProfileCandidate> Match(IReadOnlyList<WifiProfile> profiles, IReadOnlyList<WifiNetwork> scanned) =>
            profiles.Join(scanned, p => p.Ssid, n => n.Ssid, (p, n) => new WifiProfileCandidate(p, n.Signal), StringComparer.Ordinal)
                .OrderByDescending(c => c.Signal)
                .ToList();
    }
}
