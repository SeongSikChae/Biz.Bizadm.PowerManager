using Microsoft.Extensions.Logging;

namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi
{
    /// <summary>
    /// NetworkManager(<c>nmcli</c>) 기반 Linux 구현. 외부 명령만 호출하므로 OS 특성은 두지 않고 <see cref="HostWifiFactory"/>가 Linux에서만 고른다.
    /// 이 저장소의 개발 PC(Windows)에서는 실행 검증하지 않았다.
    /// </summary>
    public sealed class LinuxHostWifi(IProcessRunner runner, ILogger logger) : IHostWifi
    {
        internal const string Nmcli = "nmcli";
        internal const string TemporaryConnectionName = "b540-config-temp";

        public async Task<IReadOnlyList<HostWifiNetwork>> ScanAsync(CancellationToken cancellationToken)
        {
            string output = await runner.RunCheckedAsync(Nmcli,
                ["-t", "-f", "SSID,SIGNAL,IN-USE", "dev", "wifi", "list", "--rescan", "yes"], cancellationToken);
            return NmcliParser.ParseWifiList(output).Select(entry => new HostWifiNetwork(entry.Ssid, entry.Signal)).ToList();
        }

        public async Task<HostWifiConnection?> GetCurrentAsync(CancellationToken cancellationToken)
        {
            NmcliParser.DeviceStatus? device = await FindConnectedWifiDeviceAsync(cancellationToken);
            if (device is null)
                return null;

            string output = await runner.RunCheckedAsync(Nmcli,
                ["-t", "-f", "SSID,SIGNAL,IN-USE", "dev", "wifi", "list", "ifname", device.Device, "--rescan", "no"], cancellationToken);
            NmcliParser.WifiEntry? inUse = NmcliParser.ParseWifiList(output).FirstOrDefault(entry => entry.InUse);
            return inUse is null ? null : new HostWifiConnection(device.Device, inUse.Ssid, device.Connection);
        }

        public async Task<HostWifiConnection> ConnectOpenAsync(string ssid, CancellationToken cancellationToken)
        {
            string device = (await GetWifiDevicesAsync(cancellationToken)).FirstOrDefault()?.Device
                ?? throw new HostWifiException("No Wi-Fi device found by nmcli");

            // 이전 실행이 남긴 같은 이름의 임시 연결을 지운다(없으면 실패해도 무시)
            ProcessResult cleanup = await runner.RunAsync(Nmcli, ["connection", "delete", "id", TemporaryConnectionName], cancellationToken);
            if (cleanup.ExitCode == 0)
                logger.LogInformation("Removed leftover temporary connection {Name}", TemporaryConnectionName);

            await runner.RunCheckedAsync(Nmcli,
                ["dev", "wifi", "connect", ssid, "ifname", device, "name", TemporaryConnectionName], cancellationToken);
            return new HostWifiConnection(device, ssid, TemporaryConnectionName);
        }

        public Task RestoreAsync(HostWifiConnection previous, CancellationToken cancellationToken) =>
            string.IsNullOrEmpty(previous.ProfileName)
                ? Task.CompletedTask
                : runner.RunCheckedAsync(Nmcli, ["connection", "up", "id", previous.ProfileName], cancellationToken);

        public Task RemoveTemporaryAsync(HostWifiConnection temporary, CancellationToken cancellationToken) =>
            string.IsNullOrEmpty(temporary.ProfileName)
                ? Task.CompletedTask
                : runner.RunCheckedAsync(Nmcli, ["connection", "delete", "id", temporary.ProfileName], cancellationToken);

        private async Task<IReadOnlyList<NmcliParser.DeviceStatus>> GetWifiDevicesAsync(CancellationToken cancellationToken)
        {
            string output = await runner.RunCheckedAsync(Nmcli,
                ["-t", "-f", "DEVICE,TYPE,STATE,CONNECTION", "device", "status"], cancellationToken);
            return NmcliParser.ParseDeviceStatus(output).Where(status => status.Type == "wifi").ToList();
        }

        private async Task<NmcliParser.DeviceStatus?> FindConnectedWifiDeviceAsync(CancellationToken cancellationToken) =>
            (await GetWifiDevicesAsync(cancellationToken))
                .FirstOrDefault(status => status.State.StartsWith("connected", StringComparison.Ordinal) && status.Connection.Length > 0);
    }
}
