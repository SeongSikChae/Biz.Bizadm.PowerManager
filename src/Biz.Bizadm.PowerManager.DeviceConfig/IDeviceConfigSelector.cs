using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;

namespace Biz.Bizadm.PowerManager.DeviceConfig
{
    /// <summary>사용자 선택. App은 콘솔 번호 선택으로 구현한다(이후 MCP 등으로 교체 가능).</summary>
    public interface IDeviceConfigSelector
    {
        Task<WifiAdapter> SelectAdapterAsync(IReadOnlyList<WifiAdapter> adapters, CancellationToken cancellationToken);

        /// <summary>SSID가 Device.ApPrefix로 시작하는 AP만 넘긴다.</summary>
        Task<WifiNetwork> SelectDeviceApAsync(IReadOnlyList<WifiNetwork> deviceAps, CancellationToken cancellationToken);

        /// <summary>등록 목록 중 장치 apscan 결과에 있는 것만 RSSI 높은 순으로 넘긴다.</summary>
        Task<WifiProfile> SelectWifiProfileAsync(IReadOnlyList<WifiProfileCandidate> candidates, CancellationToken cancellationToken);
    }
}
