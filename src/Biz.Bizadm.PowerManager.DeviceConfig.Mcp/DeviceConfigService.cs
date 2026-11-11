using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Protocol;
using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

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

        /// <summary>
        /// run_device_commands settings 인자(#9). null 필드는 설정 파일 Settings 값(없으면 SettingsSection 기본값)을 쓴다.
        /// 장치 JSON의 snake_case(user_id 등)로 보내면 덮어쓰기가 빠진 채 실행되므로 모르는 키는 거부한다.
        /// 바인딩 단계에서 거부하면 SDK가 원인을 가리므로(실측) UnknownKeys로 받아 RunAsync에서 error로 돌려준다.
        /// </summary>
        public sealed record SettingsOverride
        {
            /// <summary>정의되지 않은 키. 입력 스키마에는 나오지 않는다.</summary>
            [JsonExtensionData]
            public Dictionary<string, JsonElement>? UnknownKeys { get; init; }

            [Description("API server address (string)")]
            public string? ApiServerAddr { get; init; }

            [Description("API server port (integer 1-65535)")]
            public int? ApiServerPort { get; init; }

            [Description("MQTT server address (string)")]
            public string? ServerAddr { get; init; }

            [Description("MQTT server port (integer 1-65535)")]
            public int? ServerPort { get; init; }

            [Description("Use SSL (boolean; sent as yes/no)")]
            public bool? SslSupport { get; init; }

            [Description("User id (string)")]
            public string? UserId { get; init; }

            [Description("Company (string)")]
            public string? Company { get; init; }

            [Description("Model (string)")]
            public string? Model { get; init; }

            [Description("Latitude (string, e.g. \"37.5\")")]
            public string? Lati { get; init; }

            [Description("Longitude (string, e.g. \"127.0\")")]
            public string? Long { get; init; }

            [Description("MQTT topic (string)")]
            public string? Topic { get; init; }
        }

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
            SettingsOverride? settingsOverride, IProgress<ProgressNotificationValue>? progress, CancellationToken ct) => LockedAsync(async () =>
        {
            List<CommandResult> results = [];
            try
            {
                IReadOnlyList<DeviceCommand> parsed = ParseCommands(commands);
                if (settingsOverride?.UnknownKeys is { Count: > 0 } unknown)
                    throw new ArgumentException($"Unknown settings keys: {string.Join(", ", unknown.Keys)} (use camelCase names such as userId, apiServerPort)");
                DeviceSettings? deviceSettings = settings.BaseOptions.Settings;
                if (parsed.Contains(DeviceCommand.ConnectAp))
                {
                    // Wi-Fi 접속 전에 검사한다(설계 5.3절). settings 인자는 ConnectAp에서만 쓴다(#9)
                    if ((settings.BaseOptions.Settings is null && settingsOverride is null) || settings.WifiProfiles.Count == 0)
                        throw new ArgumentException("ConnectAp requires Settings and WifiProfiles in the config file");
                    deviceSettings = ResolveSettings(settings.BaseOptions.Settings, settingsOverride);
                    if (wifiSsid is null || !settings.WifiProfiles.Any(p => p.Ssid == wifiSsid))
                        throw new ArgumentException($"wifiSsid must be one of list_wifi_profiles for ConnectAp: {wifiSsid}");
                }

                progress?.Report(new ProgressNotificationValue { Progress = 0, Total = parsed.Count, Message = "Connecting to device AP" });
                DeviceConfigRunner runner = new(hostWifi, new ParameterDeviceConfigSelector(adapterId, deviceApSsid, wifiSsid),
                    settings.BaseOptions with { Commands = parsed, Settings = deviceSettings }, logger);
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

        /// <summary>필드 단위 병합(#9): 인자 → 설정 파일 Settings → SettingsSection 기본값(선택 필드만). 둘 다 null인 경우는 호출 전에 거른다.</summary>
        internal static DeviceSettings ResolveSettings(DeviceSettings? file, SettingsOverride? o)
        {
            if (o is null)
                return file!;
            SettingsSection defaults = new();
            string? apiServerAddr = o.ApiServerAddr ?? file?.ApiServerAddr;
            int? apiServerPort = o.ApiServerPort ?? file?.ApiServerPort;
            string? serverAddr = o.ServerAddr ?? file?.ServerAddr;
            int? serverPort = o.ServerPort ?? file?.ServerPort;
            string? userId = o.UserId ?? file?.UserId;

            if (apiServerAddr is null || apiServerPort is not int apiPort || serverAddr is null || serverPort is not int serverPortValue || userId is null)
            {
                List<string> missing = [];
                if (apiServerAddr is null) missing.Add("apiServerAddr");
                if (apiServerPort is null) missing.Add("apiServerPort");
                if (serverAddr is null) missing.Add("serverAddr");
                if (serverPort is null) missing.Add("serverPort");
                if (userId is null) missing.Add("userId");
                throw new ArgumentException($"ConnectAp requires settings: missing {string.Join(", ", missing)} (set Settings in the config file or the settings argument)");
            }
            ValidatePort("settings.apiServerPort", apiPort);
            ValidatePort("settings.serverPort", serverPortValue);

            return new DeviceSettings
            {
                ApiServerAddr = apiServerAddr,
                ApiServerPort = apiPort,
                ServerAddr = serverAddr,
                ServerPort = serverPortValue,
                UserId = userId,
                SslSupport = o.SslSupport ?? file?.SslSupport ?? defaults.SslSupport,
                Company = o.Company ?? file?.Company ?? defaults.Company,
                Model = o.Model ?? file?.Model ?? defaults.Model,
                Lati = o.Lati ?? file?.Lati ?? defaults.Lati,
                Long = o.Long ?? file?.Long ?? defaults.Long,
                Topic = o.Topic ?? file?.Topic ?? defaults.Topic,
            };
        }

        /// <summary>McpConfiguration과 같은 메시지 형식.</summary>
        private static void ValidatePort(string name, int port)
        {
            if (port is < 1 or > 65535)
                throw new ArgumentException($"{name} out of range: {port}");
        }

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
