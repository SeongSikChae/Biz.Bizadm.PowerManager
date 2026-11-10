using System.Net;

namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi
{
    /// <summary>
    /// PC Wi-Fi. 어댑터를 인자로 받는 상태 없는 API다.
    /// OS별 구현은 HostWifi.Runtime.win/linux/osx에 있고 <see cref="HostWifiLoader"/>가 불러온다.
    /// 미구현 OS(linux·osx)는 모든 메서드가 NotImplementedException을 던진다.
    /// </summary>
    public interface IHostWifi
    {
        Task<IReadOnlyList<WifiAdapter>> ListAdaptersAsync(CancellationToken cancellationToken);

        Task<IReadOnlyList<WifiNetwork>> ScanAsync(WifiAdapter adapter, CancellationToken cancellationToken);

        /// <summary>연결된(connected) 상태일 때만 SSID, 아니면 null.</summary>
        Task<string?> GetCurrentSsidAsync(WifiAdapter adapter, CancellationToken cancellationToken);

        /// <summary>임시 프로필로 개방형 AP에 연결하고, 현재 SSID가 같고 IPv4 게이트웨이가 생길 때까지 timeout 안에서 기다린다.</summary>
        Task ConnectOpenAsync(WifiAdapter adapter, string ssid, TimeSpan timeout, CancellationToken cancellationToken);

        Task<IPAddress?> GetGatewayAsync(WifiAdapter adapter, CancellationToken cancellationToken);

        /// <summary>previousSsid로 다시 연결하고(null이면 연결 해제) 임시 프로필을 삭제한다.</summary>
        Task RestoreAsync(WifiAdapter adapter, string? previousSsid, CancellationToken cancellationToken);
    }
}
