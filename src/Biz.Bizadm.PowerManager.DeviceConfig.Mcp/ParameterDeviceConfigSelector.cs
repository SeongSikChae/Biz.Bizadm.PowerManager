using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Mcp
{
    /// <summary>
    /// tool 인자로 선택한다(설계 5.2절). 호출마다 새로 만든다.
    /// 어댑터 ID는 대소문자를 무시하고, SSID는 대소문자를 구분한다.
    /// </summary>
    internal sealed class ParameterDeviceConfigSelector(string adapterId, string deviceApSsid, string? wifiSsid) : IDeviceConfigSelector
    {
        public Task<WifiAdapter> SelectAdapterAsync(IReadOnlyList<WifiAdapter> adapters, CancellationToken cancellationToken) =>
            Task.FromResult(adapters.FirstOrDefault(a => string.Equals(a.Id, adapterId, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Adapter not found: {adapterId}"));

        public Task<WifiNetwork> SelectDeviceApAsync(IReadOnlyList<WifiNetwork> deviceAps, CancellationToken cancellationToken) =>
            Task.FromResult(deviceAps.FirstOrDefault(n => n.Ssid == deviceApSsid)
                ?? throw new ArgumentException($"Device AP not found in scan: {deviceApSsid}"));

        public Task<WifiProfile> SelectWifiProfileAsync(IReadOnlyList<WifiProfileCandidate> candidates, CancellationToken cancellationToken)
        {
            if (wifiSsid is null)
                throw new ArgumentException("wifiSsid is required for ConnectAp");
            return Task.FromResult(candidates.FirstOrDefault(c => c.Profile.Ssid == wifiSsid)?.Profile
                ?? throw new ArgumentException($"Wi-Fi not in device apscan result: {wifiSsid}"));
        }
    }
}
