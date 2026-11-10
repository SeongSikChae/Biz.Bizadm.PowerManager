namespace Biz.Bizadm.PowerManager.DeviceConfig.Wifi
{
    /// <summary>
    /// 등록 Wi‑Fi 중 장치가 스캔한 AP.
    /// </summary>
    public sealed record WifiCandidate(WifiProfile Profile, int Rssi);

    /// <summary>
    /// 장치 apscan 결과에서 고른 Wi‑Fi를 돌려준다. 콘솔·MCP 등 구현을 바꿔 끼우는 지점이다.
    /// </summary>
    public interface IWifiProfileSelector
    {
        /// <param name="candidates">RSSI 내림차순, 1개 이상.</param>
        Task<WifiProfile> SelectAsync(IReadOnlyList<WifiCandidate> candidates, CancellationToken cancellationToken);
    }

    public static class WifiProfileMatcher
    {
        /// <summary>
        /// SSID가 정확히 같은 것만 남기고, 같은 SSID는 가장 강한 RSSI로 합쳐 RSSI 내림차순으로 정렬한다.
        /// </summary>
        public static IReadOnlyList<WifiCandidate> Match(IEnumerable<ScannedAp> scanned, IEnumerable<WifiProfile> profiles)
        {
            Dictionary<string, WifiProfile> bySsid = new(StringComparer.Ordinal);
            foreach (WifiProfile profile in profiles)
                bySsid.TryAdd(profile.Ssid, profile);

            return scanned
                .Where(ap => bySsid.ContainsKey(ap.Ssid))
                .GroupBy(ap => ap.Ssid, StringComparer.Ordinal)
                .Select(group => new WifiCandidate(bySsid[group.Key], group.Max(ap => ap.Rssi)))
                .OrderByDescending(candidate => candidate.Rssi)
                .ToList();
        }
    }
}
