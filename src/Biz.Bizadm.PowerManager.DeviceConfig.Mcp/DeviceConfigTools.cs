using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Mcp
{
    /// <summary>MCP tool 정의. 로직·상태는 싱글턴 <see cref="DeviceConfigService"/>에 두고 여기서는 얇게 감싼다(설계 5절).</summary>
    [McpServerToolType]
    internal sealed class DeviceConfigTools(DeviceConfigService service)
    {
        [McpServerTool(Name = "list_wifi_adapters", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Lists this PC's wireless adapters and their currently connected SSID. Does not change the connection.")]
        public Task<DeviceConfigService.AdaptersResult> ListWifiAdapters(CancellationToken cancellationToken) =>
            ToMcpError(() => service.ListAdaptersAsync(cancellationToken));

        [McpServerTool(Name = "scan_device_aps", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Scans with the given PC wireless adapter and lists device SoftAPs (SSID starts with the configured ApPrefix), strongest signal first. Does not change the connection.")]
        public Task<DeviceConfigService.DeviceApsResult> ScanDeviceAps(
            [Description("Adapter id from list_wifi_adapters")] string adapterId,
            CancellationToken cancellationToken) =>
            ToMcpError(() => service.ScanDeviceApsAsync(adapterId, cancellationToken));

        [McpServerTool(Name = "list_wifi_profiles", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Lists the router Wi-Fi SSIDs registered in the config file (passwords are never returned). Use one as wifiSsid for ConnectAp.")]
        public DeviceConfigService.WifiProfilesResult ListWifiProfiles() => service.ListWifiProfiles();

        [McpServerTool(Name = "run_device_commands", ReadOnly = false, Destructive = true, OpenWorld = true, UseStructuredContent = true)]
        [Description("Connects the PC Wi-Fi adapter to the device AP, runs the commands in order (new TCP connection per command), then requests reconnection to the previous Wi-Fi. The adapter is offline while this runs and may stay offline briefly after the response. Commands: ConnectAp (device apscan, then send router Wi-Fi + server settings and connectap; requires wifiSsid), ApScan (list of APs scanned by the device), Reboot (no response expected; timeout is treated as success), FactoryReset (factory reset; expects OK), ScanAp (test-mode AP scan list), OnCmd (test-mode ON_CMD), OffCmd (test-mode OFF_CMD), Measurement (test-mode MEASUREMENT_W value), OverLoad (test-mode OVERLOAD frame), TestFactoryReset (test-mode factory reset; no response expected, timeout is treated as success). State-changing: ConnectAp, Reboot, FactoryReset, TestFactoryReset, OnCmd, OffCmd. Returns success, completed command results, and error.")]
        public Task<DeviceConfigService.RunResult> RunDeviceCommands(
            [Description("Adapter id from list_wifi_adapters")] string adapterId,
            [Description("Device AP SSID from scan_device_aps")] string deviceApSsid,
            [Description("Command names in execution order")] string[] commands,
            IProgress<ProgressNotificationValue> progress,
            CancellationToken cancellationToken,
            [Description("Router SSID from list_wifi_profiles; required for ConnectAp")] string? wifiSsid = null) =>
            service.RunAsync(adapterId, deviceApSsid, commands, wifiSsid, progress, cancellationToken);

        /// <summary>조회 tool 실패 원인이 클라이언트에 보이도록 McpException으로 바꾼다.</summary>
        private static async Task<T> ToMcpError<T>(Func<Task<T>> action)
        {
            try
            {
                return await action();
            }
            catch (NotImplementedException)
            {
                throw new McpException("PC Wi-Fi is not implemented on this OS");
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or McpException))
            {
                throw new McpException($"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
