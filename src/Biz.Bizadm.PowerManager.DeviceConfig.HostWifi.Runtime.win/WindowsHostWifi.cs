using System.Net;

namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi
{
    /// <summary>Windows PC Wi-Fi. U4에서는 스텁이고 U6에서 WLAN API 구현으로 교체한다.</summary>
    internal sealed class WindowsHostWifi : IHostWifi
    {
        public Task<IReadOnlyList<WifiAdapter>> ListAdaptersAsync(CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<IReadOnlyList<WifiNetwork>> ScanAsync(WifiAdapter adapter, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<string?> GetCurrentSsidAsync(WifiAdapter adapter, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task ConnectOpenAsync(WifiAdapter adapter, string ssid, TimeSpan timeout, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<IPAddress?> GetGatewayAsync(WifiAdapter adapter, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task RestoreAsync(WifiAdapter adapter, string? previousSsid, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
