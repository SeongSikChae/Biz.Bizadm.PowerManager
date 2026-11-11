using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Mcp
{
    /// <summary>
    /// tool 로직(싱글턴). PC Wi-Fi를 쓰는 호출은 하나의 세마포어로 직렬화하고, 앞 호출이 끝날 때까지 기다린다(설계 5절).
    /// list_wifi_profiles는 메모리 값만 읽으므로 잠그지 않는다.
    /// </summary>
    internal sealed class DeviceConfigService(IHostWifi hostWifi, DeviceConfigService.ServiceSettings settings, ILogger<DeviceConfigRunner> logger)
    {
        /// <summary>설정 파일에서 만든 기본 옵션(Commands는 비어 있음)과 복호화한 Wi-Fi 프로필.</summary>
        public sealed record ServiceSettings(DeviceConfigOptions BaseOptions, IReadOnlyList<WifiProfile> WifiProfiles);

        /// <summary>CurrentSsid는 미연결이면 null이다. 기본값으로 출력 schema의 required에서 뺀다(#8).</summary>
        public sealed record AdapterInfo(string Id, string Name, string? CurrentSsid = null);

        public sealed record AdaptersResult(IReadOnlyList<AdapterInfo> Adapters);

        public sealed record DeviceApsResult(IReadOnlyList<WifiNetwork> DeviceAps);

        public sealed record WifiProfilesResult(IReadOnlyList<string> Ssids);

        public sealed record CommandResult(string Command, string Response);

        /// <summary>Error는 성공이면 null이다. 기본값으로 출력 schema의 required에서 뺀다(#8).</summary>
        public sealed record RunResult(bool Success, IReadOnlyList<CommandResult> Results, string? Error = null);

        private readonly SemaphoreSlim gate = new(1, 1);

        public Task<AdaptersResult> ListAdaptersAsync(CancellationToken ct) => LockedAsync(async () =>
        {
            List<AdapterInfo> list = [];
            foreach (WifiAdapter a in await hostWifi.ListAdaptersAsync(ct))
                list.Add(new AdapterInfo(a.Id, a.Name, await hostWifi.GetCurrentSsidAsync(a, ct)));
            return new AdaptersResult(list);
        }, ct);

        public Task<DeviceApsResult> ScanDeviceApsAsync(string adapterId, CancellationToken ct) => LockedAsync(async () =>
        {
            WifiAdapter adapter = await new ParameterDeviceConfigSelector(adapterId, "", null)
                .SelectAdapterAsync(await hostWifi.ListAdaptersAsync(ct), ct);
            return new DeviceApsResult((await hostWifi.ScanAsync(adapter, ct))
                .Where(n => n.Ssid.StartsWith(settings.BaseOptions.ApPrefix, StringComparison.Ordinal))
                .OrderByDescending(n => n.Signal)
                .ToList());
        }, ct);

        public WifiProfilesResult ListWifiProfiles() => new(settings.WifiProfiles.Select(p => p.Ssid).ToList());

        /// <summary>
        /// 장치 AP 접속 → 명령 실행 → PC Wi-Fi 복구(Runner). 명령·Wi-Fi·인자 오류는 예외 대신 Success=false와 완료 결과로 돌려준다(설계 5.4절).
        /// 취소(OperationCanceledException)는 그대로 전파한다.
        /// </summary>
        public Task<RunResult> RunAsync(string adapterId, string deviceApSsid, IReadOnlyList<string> commands, string? wifiSsid,
            IProgress<ProgressNotificationValue>? progress, CancellationToken ct) => LockedAsync(async () =>
        {
            List<CommandResult> results = [];
            try
            {
                IReadOnlyList<DeviceCommand> parsed = ParseCommands(commands);
                if (parsed.Contains(DeviceCommand.ConnectAp))
                {
                    // Wi-Fi 접속 전에 검사한다(설계 5.3절)
                    if (settings.BaseOptions.Settings is null || settings.WifiProfiles.Count == 0)
                        throw new ArgumentException("ConnectAp requires Settings and WifiProfiles in the config file");
                    if (wifiSsid is null || !settings.WifiProfiles.Any(p => p.Ssid == wifiSsid))
                        throw new ArgumentException($"wifiSsid must be one of list_wifi_profiles for ConnectAp: {wifiSsid}");
                }

                progress?.Report(new ProgressNotificationValue { Progress = 0, Total = parsed.Count, Message = "Connecting to device AP" });
                DeviceConfigRunner runner = new(hostWifi, new ParameterDeviceConfigSelector(adapterId, deviceApSsid, wifiSsid),
                    settings.BaseOptions with { Commands = parsed }, logger);
                await runner.RunAsync(r =>
                {
                    results.Add(new CommandResult(r.Command.ToString(), r.Response));
                    progress?.Report(new ProgressNotificationValue { Progress = results.Count, Total = parsed.Count, Message = r.Command.ToString() });
                }, ct);
                return new RunResult(true, results, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "run_device_commands failed");
                return new RunResult(false, results, $"{ex.GetType().Name}: {ex.Message}");
            }
        }, ct);

        /// <summary>열거형 이름과 정확히 일치만 허용한다(Enum.TryParse는 "1"·"OnCmd,OffCmd"를 통과시킴).</summary>
        private static IReadOnlyList<DeviceCommand> ParseCommands(IReadOnlyList<string> commands)
        {
            if (commands.Count == 0)
                throw new ArgumentException("commands must have at least one item");
            string[] names = Enum.GetNames<DeviceCommand>();
            return commands.Select(c => names.Contains(c, StringComparer.Ordinal)
                ? Enum.Parse<DeviceCommand>(c)
                : throw new ArgumentException($"Unknown command: {c}")).ToList();
        }

        private async Task<T> LockedAsync<T>(Func<Task<T>> action, CancellationToken ct)
        {
            await gate.WaitAsync(ct);
            try
            {
                return await action();
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
