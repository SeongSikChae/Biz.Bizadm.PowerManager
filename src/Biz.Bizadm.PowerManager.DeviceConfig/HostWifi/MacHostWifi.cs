using Microsoft.Extensions.Logging;

namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi
{
    /// <summary>
    /// macOS 구현(<c>system_profiler</c>, <c>networksetup</c>). 외부 명령만 호출하므로 OS 특성은 두지 않고 <see cref="HostWifiFactory"/>가 macOS에서만 고른다.
    /// 이 저장소의 개발 PC(Windows)에서는 실행 검증하지 않았다. macOS 14 이후는 위치 권한이 없으면 SSID가 가려질 수 있다.
    /// </summary>
    public sealed class MacHostWifi(IProcessRunner runner, ILogger logger) : IHostWifi
    {
        internal const string SystemProfiler = "system_profiler";
        internal const string NetworkSetup = "networksetup";

        public async Task<IReadOnlyList<HostWifiNetwork>> ScanAsync(CancellationToken cancellationToken)
        {
            SystemProfilerParser.AirPortInterface airPort = await GetWifiAirPortAsync(cancellationToken);
            return airPort.Current is null ? airPort.Others : [airPort.Current, .. airPort.Others];
        }

        public async Task<HostWifiConnection?> GetCurrentAsync(CancellationToken cancellationToken)
        {
            SystemProfilerParser.AirPortInterface airPort = await GetWifiAirPortAsync(cancellationToken);
            return airPort.Current is null ? null : new HostWifiConnection(airPort.Name, airPort.Current.Ssid, airPort.Current.Ssid);
        }

        public async Task<HostWifiConnection> ConnectOpenAsync(string ssid, CancellationToken cancellationToken)
        {
            string device = await GetWifiDeviceAsync(cancellationToken);
            await SetAirPortNetworkAsync(device, ssid, cancellationToken);
            return new HostWifiConnection(device, ssid, ssid);
        }

        public Task RestoreAsync(HostWifiConnection previous, CancellationToken cancellationToken) =>
            // 비밀번호는 키체인에 저장된 것을 networksetup이 사용한다
            SetAirPortNetworkAsync(previous.InterfaceId, previous.Ssid, cancellationToken);

        public Task RemoveTemporaryAsync(HostWifiConnection temporary, CancellationToken cancellationToken) =>
            runner.RunCheckedAsync(NetworkSetup,
                ["-removepreferredwirelessnetwork", temporary.InterfaceId, temporary.Ssid], cancellationToken);

        private async Task SetAirPortNetworkAsync(string device, string ssid, CancellationToken cancellationToken)
        {
            // networksetup은 실패해도 종료 코드 0으로 메시지만 출력할 수 있다
            string output = await runner.RunCheckedAsync(NetworkSetup, ["-setairportnetwork", device, ssid], cancellationToken);
            if (output.Contains("Error", StringComparison.OrdinalIgnoreCase) || output.Contains("Could not", StringComparison.OrdinalIgnoreCase))
                throw new HostWifiException($"networksetup -setairportnetwork {device} {ssid}: {output.Trim()}");
            logger.LogInformation("networksetup joined {Ssid} on {Device}", ssid, device);
        }

        private async Task<string> GetWifiDeviceAsync(CancellationToken cancellationToken)
        {
            string output = await runner.RunCheckedAsync(NetworkSetup, ["-listallhardwareports"], cancellationToken);
            return SystemProfilerParser.FindWifiDevice(output) ?? throw new HostWifiException("No Wi-Fi hardware port found by networksetup");
        }

        private async Task<SystemProfilerParser.AirPortInterface> GetWifiAirPortAsync(CancellationToken cancellationToken)
        {
            string device = await GetWifiDeviceAsync(cancellationToken);
            string json = await runner.RunCheckedAsync(SystemProfiler, ["SPAirPortDataType", "-json"], cancellationToken);
            return SystemProfilerParser.ParseAirPort(json).FirstOrDefault(airPort => airPort.Name == device)
                ?? throw new HostWifiException($"Wi-Fi interface {device} not found in system_profiler output");
        }
    }
}
