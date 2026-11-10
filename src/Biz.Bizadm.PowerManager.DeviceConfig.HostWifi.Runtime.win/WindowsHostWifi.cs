using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Security;

namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi
{
    /// <summary>Windows PC Wi-Fi(Native Wifi API). 어댑터 Id는 인터페이스 GUID "{...}"(NetworkInterface.Id와 같음).</summary>
    internal sealed class WindowsHostWifi(ILogger logger) : IHostWifi
    {
        internal const string TemporaryProfileName = "B540-Config-Temp";
        private static readonly TimeSpan ScanWait = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan ConnectPollInterval = TimeSpan.FromMilliseconds(500);

        public Task<IReadOnlyList<WifiAdapter>> ListAdaptersAsync(CancellationToken cancellationToken)
        {
            using WlanClient client = WlanClient.Open();
            return Task.FromResult<IReadOnlyList<WifiAdapter>>(client.GetInterfaces()
                .Select(i => new WifiAdapter(ToId(i.Guid), i.Description))
                .ToList());
        }

        public async Task<IReadOnlyList<WifiNetwork>> ScanAsync(WifiAdapter adapter, CancellationToken cancellationToken)
        {
            Guid id = Guid.Parse(adapter.Id);
            using (WlanClient client = WlanClient.Open())
                client.Scan(id);
            await Task.Delay(ScanWait, cancellationToken);
            using (WlanClient client = WlanClient.Open())
            {
                // 프로필별로 항목이 나뉘므로 SSID별 최대 신호로 묶는다
                return client.GetAvailableNetworks(id)
                    .GroupBy(n => n.Ssid, StringComparer.Ordinal)
                    .Select(g => new WifiNetwork(g.Key, g.Max(n => n.Signal)))
                    .OrderByDescending(n => n.Signal)
                    .ToList();
            }
        }

        public Task<string?> GetCurrentSsidAsync(WifiAdapter adapter, CancellationToken cancellationToken)
        {
            using WlanClient client = WlanClient.Open();
            return Task.FromResult(client.GetConnectedSsid(Guid.Parse(adapter.Id)));
        }

        public async Task ConnectOpenAsync(WifiAdapter adapter, string ssid, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Guid id = Guid.Parse(adapter.Id);
            using (WlanClient client = WlanClient.Open())
            {
                client.SetProfile(id, OpenProfileXml(TemporaryProfileName, ssid));
                client.Connect(id, TemporaryProfileName);
            }
            logger.LogInformation("WlanConnect requested {Ssid} with profile {Profile}", ssid, TemporaryProfileName);

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            try
            {
                // SSID가 같고 IPv4 게이트웨이가 생길 때까지 기다린다(APIPA 169.254.x.x에서 끝나지 않도록 게이트웨이 기준)
                while (!(await GetCurrentSsidAsync(adapter, cts.Token) == ssid && await GetGatewayAsync(adapter, cts.Token) is not null))
                    await Task.Delay(ConnectPollInterval, cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Device AP connection timed out after {timeout.TotalSeconds}s: {ssid}");
            }
        }

        public Task<IPAddress?> GetGatewayAsync(WifiAdapter adapter, CancellationToken cancellationToken)
        {
            NetworkInterface? nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Id, adapter.Id, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(nic?.GetIPProperties().GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any)));
        }

        public Task RestoreAsync(WifiAdapter adapter, string? previousSsid, CancellationToken cancellationToken)
        {
            Guid id = Guid.Parse(adapter.Id);
            using WlanClient client = WlanClient.Open();
            // WLAN 연결에는 프로필 이름이 필요하므로 사용 가능 목록에서 SSID가 같은 프로필을 찾는다
            string? profile = previousSsid is null
                ? null
                : client.GetAvailableNetworks(id)
                    .FirstOrDefault(n => n.Ssid == previousSsid && n.ProfileName.Length > 0)?.ProfileName;
            if (previousSsid is not null && profile is null)
                logger.LogWarning("Profile for previous Wi-Fi {Ssid} not found; disconnecting", previousSsid);

            // 임시 프로필을 먼저 지운다. 연결 중인 임시 프로필을 재연결 요청 뒤에 지우면 Windows가 연결을 끊어
            // 진행 중이던 이전 Wi-Fi 재연결이 취소된다(U8 실측: 공유기 연결 상태에서 시작했을 때 연결 해제로 끝남).
            Exception? deleteError = null;
            try
            {
                client.DeleteProfile(id, TemporaryProfileName);
            }
            catch (Exception ex)
            {
                deleteError = ex;
                logger.LogError(ex, "Failed to delete temporary profile {Profile}", TemporaryProfileName);
            }

            // 재연결은 삭제가 실패해도 시도하며, 재연결 예외가 삭제 예외보다 우선한다
            if (profile is null)
                client.Disconnect(id);
            else
                client.Connect(id, profile);
            if (deleteError is not null)
                ExceptionDispatchInfo.Throw(deleteError);
            return Task.CompletedTask;
        }

        internal static string OpenProfileXml(string name, string ssid) => $"""
            <?xml version="1.0"?>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <name>{SecurityElement.Escape(name)}</name>
              <SSIDConfig><SSID><name>{SecurityElement.Escape(ssid)}</name></SSID></SSIDConfig>
              <connectionType>ESS</connectionType>
              <connectionMode>manual</connectionMode>
              <MSM><security><authEncryption>
                <authentication>open</authentication><encryption>none</encryption><useOneX>false</useOneX>
              </authEncryption></security></MSM>
            </WLANProfile>
            """;

        private static string ToId(Guid guid) => guid.ToString("B").ToUpperInvariant();
    }
}
