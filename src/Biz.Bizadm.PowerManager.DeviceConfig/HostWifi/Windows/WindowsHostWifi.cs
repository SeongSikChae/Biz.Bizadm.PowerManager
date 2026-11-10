using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;
using System.Security;

namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.Windows
{
    /// <summary>
    /// Native Wifi API 구현. Windows 11 24H2 이후는 위치 서비스 권한이 없으면 스캔이 접근 거부로 실패할 수 있다.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsHostWifi(ILogger logger) : IHostWifi
    {
        internal const string TemporaryProfileName = "B540-Config-Temp";

        /// <summary>
        /// WlanScan은 비동기로 끝난다. 완료 알림 대신 고정 시간을 기다린다(드라이버는 4초 안에 끝내야 한다).
        /// </summary>
        private static readonly TimeSpan ScanWait = TimeSpan.FromSeconds(4);

        public async Task<IReadOnlyList<HostWifiNetwork>> ScanAsync(CancellationToken cancellationToken)
        {
            using WlanClient client = WlanClient.Open();
            Guid interfaceGuid = FirstInterface(client);
            client.Scan(interfaceGuid);
            await Task.Delay(ScanWait, cancellationToken);
            return client.GetAvailableNetworks(interfaceGuid);
        }

        public Task<HostWifiConnection?> GetCurrentAsync(CancellationToken cancellationToken)
        {
            using WlanClient client = WlanClient.Open();
            foreach (Guid interfaceGuid in client.GetInterfaces())
            {
                (string ProfileName, string Ssid)? current = client.GetCurrentConnection(interfaceGuid);
                if (current is not null)
                    return Task.FromResult<HostWifiConnection?>(new HostWifiConnection(ToInterfaceId(interfaceGuid), current.Value.Ssid, current.Value.ProfileName));
            }
            return Task.FromResult<HostWifiConnection?>(null);
        }

        public Task<HostWifiConnection> ConnectOpenAsync(string ssid, CancellationToken cancellationToken)
        {
            using WlanClient client = WlanClient.Open();
            Guid interfaceGuid = FirstInterface(client);
            client.SetProfile(interfaceGuid, OpenProfileXml(TemporaryProfileName, ssid));
            client.Connect(interfaceGuid, TemporaryProfileName);
            logger.LogInformation("WlanConnect requested {Ssid} with profile {Profile}", ssid, TemporaryProfileName);
            return Task.FromResult(new HostWifiConnection(ToInterfaceId(interfaceGuid), ssid, TemporaryProfileName));
        }

        public Task RestoreAsync(HostWifiConnection previous, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(previous.ProfileName))
                return Task.CompletedTask;
            using WlanClient client = WlanClient.Open();
            client.Connect(Guid.Parse(previous.InterfaceId), previous.ProfileName);
            return Task.CompletedTask;
        }

        public Task RemoveTemporaryAsync(HostWifiConnection temporary, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(temporary.ProfileName))
                return Task.CompletedTask;
            using WlanClient client = WlanClient.Open();
            client.DeleteProfile(Guid.Parse(temporary.InterfaceId), temporary.ProfileName);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 개방형(인증·암호화 없음) 임시 프로필 XML.
        /// </summary>
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

        /// <summary>
        /// <see cref="System.Net.NetworkInformation.NetworkInterface.Id"/>와 같은 형식(<c>{GUID}</c>, 대문자).
        /// </summary>
        internal static string ToInterfaceId(Guid interfaceGuid) => interfaceGuid.ToString("B").ToUpperInvariant();

        private static Guid FirstInterface(WlanClient client) =>
            client.GetInterfaces().FirstOrDefault() is { } guid && guid != Guid.Empty
                ? guid
                : throw new HostWifiException("No wireless interface found");
    }
}
