using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.Device
{
    /// <summary>
    /// 실장비 테스트용 선택기. 어댑터는 첫 번째, 장치 AP는 DEVICE_AP_SSID, 공유기 Wi-Fi는 DEVICE_WIFI_SSID와 같은 후보(RSSI 가장 높은 것).
    /// </summary>
    internal sealed class EnvironmentSelector(string deviceApSsid, string? wifiSsid) : IDeviceConfigSelector
    {
        public Task<WifiAdapter> SelectAdapterAsync(IReadOnlyList<WifiAdapter> adapters, CancellationToken cancellationToken) =>
            Task.FromResult(adapters.Count > 0 ? adapters[0] : throw new InvalidOperationException("No wireless adapter"));

        public Task<WifiNetwork> SelectDeviceApAsync(IReadOnlyList<WifiNetwork> deviceAps, CancellationToken cancellationToken) =>
            Task.FromResult(deviceAps.FirstOrDefault(n => n.Ssid == deviceApSsid)
                ?? throw new InvalidOperationException($"Device AP {deviceApSsid} not found by PC scan"));

        public Task<WifiProfile> SelectWifiProfileAsync(IReadOnlyList<WifiProfileCandidate> candidates, CancellationToken cancellationToken) =>
            Task.FromResult(candidates.FirstOrDefault(c => c.Profile.Ssid == wifiSsid)?.Profile
                ?? throw new InvalidOperationException($"Router Wi-Fi {wifiSsid} not in candidates"));
    }
}
