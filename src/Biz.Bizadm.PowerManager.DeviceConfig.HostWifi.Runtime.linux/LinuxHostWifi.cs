using System.Net;

namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi
{
    /// <summary>Linux PC Wi-Fi는 구현하지 않는다(실행 검증 불가). 호출하면 앱이 경고 후 Device.Host로 접속한다.</summary>
    internal sealed class LinuxHostWifi : IHostWifi
    {
        public Task<IReadOnlyList<WifiAdapter>> ListAdaptersAsync(CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<IReadOnlyList<WifiNetwork>> ScanAsync(WifiAdapter adapter, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<string?> GetCurrentSsidAsync(WifiAdapter adapter, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task ConnectOpenAsync(WifiAdapter adapter, string ssid, TimeSpan timeout, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<IPAddress?> GetGatewayAsync(WifiAdapter adapter, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task RestoreAsync(WifiAdapter adapter, string? previousSsid, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
