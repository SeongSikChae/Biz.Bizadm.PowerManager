---
status: done
category: mcp
feature: device-mcp
phase: 개발
---

# device-mcp 개발 계획

선행: [0001-device-mcp-방안](0001-device-mcp-방안.md) · [0002-device-mcp-설계](0002-device-mcp-설계.md) · GitHub: SeongSikChae/Biz.Bizadm.PowerManager#4 · 관련: #3(Reboot), #5(OVERLOAD)

## 0. 확인 결과 (설계 10절)

| 항목 | 결과 |
|---|---|
| MCP SDK | `ModelContextProtocol` **2.4.0**(2026-11-07 공개, 최신 안정판). net10.0 대상 있음, 취약점·Deprecated 없음(nuget.org 카탈로그, 2026-11-10 조회). 의존: `ModelContextProtocol.Core 2.4.0`, `Microsoft.Extensions.Hosting.Abstractions >= 10.0.14` |
| elicitation API | `McpServer.ClientCapabilities?.Elicitation`(지원 여부), `McpServer.ElicitAsync(ElicitRequestParams, CancellationToken)` → `ElicitResult`(`Action`, `IsAccepted`, `Content`). 선택 스키마는 `ElicitRequestParams.TitledSingleSelectEnumSchema`(`OneOf` = `EnumSchemaOption{Const, Title}`) |
| tool 정의 | `[McpServerToolType]` 클래스, `[McpServerTool(Name, ReadOnly, Destructive, ...)]` 메서드. 오류는 `McpException` |
| 서버 설명 | `McpServerOptions.ServerInstructions` |
| 테스트 연결 | 메모리: `StreamServerTransport` + `ModelContextProtocol.Protocol.StreamClientTransport`(Pipe). 실장비: `StdioClientTransport` + `McpClient.CreateAsync` |
| Windows 어댑터 설명 | `WLAN_INTERFACE_INFO`(532 bytes) = GUID(16) + `strInterfaceDescription` WCHAR[256](오프셋 16) + 상태(4). 지금 `WlanClient.GetInterfaces`는 GUID만 읽는다 |

### 패키지 (추가분)

| 패키지 | 버전 | 사용 |
|---|---|---|
| ModelContextProtocol | 2.4.0 | Mcp, Mcp.Tests |
| Biz.Bizadm.KMS · KMS.Credentials · System.Configuration.Extensions · YamlDotNet | 기존 버전 | App → DeviceConfig로 이동 |

취약·Deprecated 여부는 U6에서 `dotnet package list --vulnerable --include-transitive`, `--deprecated`로 다시 확인한다.

## 작업 단위

순서대로 구현하고, 각 단위의 검증이 통과해야 다음 단위로 간다. 경로는 저장소 루트 기준이다. 모든 단위의 공통 검증은 `dotnet build` 경고 0, `dotnet test`(실장비 `Device` 카테고리 제외) 통과다.

### U1. `IHostWifi` 어댑터 지정 (OS별 구현)

- **대상**: `src/.../DeviceConfig/HostWifi/IHostWifi.cs`(`HostWifiAdapter`, `IHostWifi`), `Windows/WlanNative.cs`(`WlanClient.GetInterfaces`), `Windows/WindowsHostWifi.cs`, `LinuxHostWifi.cs`, `MacHostWifi.cs`, `SystemProfilerParser.cs`(`FindWifiDevices`), 테스트 `HostWifi/*Tests.cs`, `DeviceApLocatorTests`·`RealDeviceTests`의 가짜/호출부(컴파일 유지용 최소 수정)
- **코드 초안**:

```csharp
// IHostWifi.cs
public sealed record HostWifiAdapter(string Id, string Name, string? Description, string? ConnectedSsid);

public interface IHostWifi
{
    Task<IReadOnlyList<HostWifiAdapter>> ListAdaptersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<HostWifiNetwork>> ScanAsync(string adapterId, CancellationToken cancellationToken);
    Task<HostWifiConnection?> GetCurrentAsync(string adapterId, CancellationToken cancellationToken);
    Task<HostWifiConnection> ConnectOpenAsync(string adapterId, string ssid, CancellationToken cancellationToken);
    // RestoreAsync, RemoveTemporaryAsync 시그니처 유지
}
```

```csharp
// WlanClient: GUID와 설명을 함께 읽는다
public IReadOnlyList<(Guid Guid, string Description)> GetInterfaces()
{
    // ... WlanEnumInterfaces ...
    nint item = list + WlanNative.ListHeaderSize + i * WlanNative.InterfaceInfoSize;
    result.Add((Marshal.PtrToStructure<Guid>(item), Marshal.PtrToStringUni(item + WlanNative.InterfaceInfoDescriptionOffset) ?? ""));
}

// WindowsHostWifi
public Task<IReadOnlyList<HostWifiAdapter>> ListAdaptersAsync(CancellationToken cancellationToken)
{
    using WlanClient client = WlanClient.Open();
    Dictionary<string, string> names = NetworkInterface.GetAllNetworkInterfaces()
        .ToDictionary(nic => nic.Id, nic => nic.Name, StringComparer.OrdinalIgnoreCase);
    return Task.FromResult<IReadOnlyList<HostWifiAdapter>>(client.GetInterfaces()
        .Select(i => ToAdapter(ToInterfaceId(i.Guid), i.Description, names, client.GetCurrentConnection(i.Guid)?.Ssid))
        .ToList());
}

/// <summary>GUID → NetworkInterface.Name. 못 찾으면 설명, 그것도 없으면 Id.</summary>
internal static HostWifiAdapter ToAdapter(string id, string description, IReadOnlyDictionary<string, string> names, string? ssid) =>
    new(id, names.GetValueOrDefault(id) ?? (description.Length > 0 ? description : id), description, ssid);

public async Task<IReadOnlyList<HostWifiNetwork>> ScanAsync(string adapterId, CancellationToken cancellationToken)
{
    using WlanClient client = WlanClient.Open();
    Guid interfaceGuid = Guid.Parse(adapterId);   // FirstInterface 제거
    client.Scan(interfaceGuid);
    // ... existing ...
}
```

```csharp
// LinuxHostWifi
public async Task<IReadOnlyList<HostWifiAdapter>> ListAdaptersAsync(CancellationToken cancellationToken)
{
    List<HostWifiAdapter> adapters = [];
    foreach (NmcliParser.DeviceStatus device in await GetWifiDevicesAsync(cancellationToken))
        adapters.Add(new HostWifiAdapter(device.Device, device.Device, null, (await GetCurrentAsync(device.Device, cancellationToken))?.Ssid));
    return adapters;
}

public async Task<IReadOnlyList<HostWifiNetwork>> ScanAsync(string adapterId, CancellationToken cancellationToken) =>
    NmcliParser.ParseWifiList(await runner.RunCheckedAsync(Nmcli,
        ["-t", "-f", "SSID,SIGNAL,IN-USE", "dev", "wifi", "list", "ifname", adapterId, "--rescan", "yes"], cancellationToken))
        .Select(entry => new HostWifiNetwork(entry.Ssid, entry.Signal)).ToList();

// ConnectOpenAsync: 첫 장치 대신 adapterId → ["dev", "wifi", "connect", ssid, "ifname", adapterId, "name", TemporaryConnectionName]
// GetCurrentAsync(adapterId): device status에서 adapterId 행이 connected일 때만 IN-USE 조회

public Task RestoreAsync(HostWifiConnection previous, CancellationToken cancellationToken) =>
    string.IsNullOrEmpty(previous.ProfileName)
        ? Task.CompletedTask
        : runner.RunCheckedAsync(Nmcli, ["connection", "up", "id", previous.ProfileName, "ifname", previous.InterfaceId], cancellationToken);
```

```csharp
// SystemProfilerParser: Wi-Fi 포트 전부
public static IReadOnlyList<string> FindWifiDevices(string hardwarePorts) =>
    WifiDeviceRegex().Matches(hardwarePorts).Select(match => match.Groups["Device"].Value).ToList();

// MacHostWifi
public async Task<IReadOnlyList<HostWifiAdapter>> ListAdaptersAsync(CancellationToken cancellationToken)
{
    IReadOnlyList<string> devices = SystemProfilerParser.FindWifiDevices(
        await runner.RunCheckedAsync(NetworkSetup, ["-listallhardwareports"], cancellationToken));
    IReadOnlyList<SystemProfilerParser.AirPortInterface> airPorts = await GetAirPortsAsync(cancellationToken);
    return devices.Select(device => new HostWifiAdapter(device, device, null,
        airPorts.FirstOrDefault(airPort => airPort.Name == device)?.Current?.Ssid)).ToList();
}
// ScanAsync/GetCurrentAsync(adapterId): airPorts에서 Name == adapterId (없으면 HostWifiException)
// ConnectOpenAsync(adapterId, ssid): SetAirPortNetworkAsync(adapterId, ssid, ...)
```

- **검증**: `LinuxHostWifiTests`에 `ListAdaptersAsync`(wifi 장치만, 연결 SSID), `ScanAsync`·`ConnectOpenAsync`의 `ifname <id>`, `RestoreAsync`의 `ifname <InterfaceId>` 인자 확인 추가. `MacHostWifiTests`에 Wi‑Fi 포트 2개 출력 → 어댑터 2개, 지정 어댑터로 `-setairportnetwork <id>` 확인. `WindowsHostWifiTests`에 `ToAdapter` 매핑(이름 있음/없음·설명 없음). 기존 테스트는 인자 추가로 고친다.
- **선행 단위**: 없음

### U2. 어댑터 선택 · `DeviceApLocator` · App 콘솔 선택

- **대상**: `DeviceConfig/HostWifi/IWifiAdapterSelector.cs`(신규), `DeviceConfig/DeviceApLocator.cs`(생성자 `adapterId`, 후보 함수 `DeviceApCandidates`), `App/ConsoleSelectors.cs`(`ConsoleWifiAdapterSelector`), `App/DeviceConfigService.cs`(어댑터 목록·선택 후 Locator 생성), `App/Program.cs`(DI 등록), `DeviceApLocatorTests`
- **코드 초안**:

```csharp
public interface IWifiAdapterSelector
{
    /// <param name="adapters">1개 이상.</param>
    Task<HostWifiAdapter> SelectAsync(IReadOnlyList<HostWifiAdapter> adapters, CancellationToken cancellationToken);
}

/// <summary>장치 AP 후보 규칙. DeviceApLocator와 DeviceRunner.ScanDeviceApsAsync(U4)가 같이 쓴다.</summary>
public static class DeviceApCandidates
{
    public static IReadOnlyList<HostWifiNetwork> Filter(IEnumerable<HostWifiNetwork> found, string prefix) => found
        .Where(network => network.Ssid.StartsWith(prefix, StringComparison.Ordinal))
        .GroupBy(network => network.Ssid, StringComparer.Ordinal)
        .Select(group => group.MaxBy(network => network.Signal)!)
        .OrderByDescending(network => network.Signal)
        .ToList();
}
```

```csharp
public sealed class DeviceApLocator(
    IHostWifi? hostWifi,
    string? adapterId,           // hostWifi가 있으면 필수
    IDeviceApSelector selector,
    DeviceApOptions options,
    ILogger logger, /* ... existing ... */) : IAsyncDisposable
{
    public async Task<DeviceEndpoint> LocateAsync(CancellationToken cancellationToken)
    {
        if (hostWifi is null)
            return Fallback("host Wi-Fi control is not available");
        ArgumentException.ThrowIfNullOrEmpty(adapterId);
        // found = await hostWifi.ScanAsync(adapterId, cancellationToken);
        // candidates = DeviceApCandidates.Filter(found, options.ApSsidPrefix);
        // current = hostWifi.GetCurrentAsync(adapterId, ...)  ← 그 어댑터의 연결로만 "이미 접속" 판단
        // temporary = await hostWifi.ConnectOpenAsync(adapterId, chosen.Ssid, cancellationToken);
    }
    // TryGetCurrentAsync도 adapterId로 조회
}
```

```csharp
// App: 후보 1개여도 입력받는다
internal sealed class ConsoleWifiAdapterSelector : IWifiAdapterSelector
{
    public Task<HostWifiAdapter> SelectAsync(IReadOnlyList<HostWifiAdapter> adapters, CancellationToken cancellationToken)
    {
        int index = ConsoleSelection.Select("장치 AP에 접속할 PC 무선 어댑터",
            adapters.Select(a => $"{a.Name} ({a.Description ?? a.Id}) - 연결: {a.ConnectedSsid ?? "없음"}").ToList());
        return Task.FromResult(adapters[index]);
    }
}

// DeviceConfigService.ExecuteAsync (U4에서 DeviceRunner로 옮긴다)
IHostWifi? hostWifi = HostWifiFactory.Create(loggerFactory);
HostWifiAdapter? adapter = hostWifi is null ? null : await SelectAdapterOrNullAsync(hostWifi, stoppingToken);
await using (DeviceApLocator locator = new(adapter is null ? null : hostWifi, adapter?.Id, apSelector, apOptions, logger))
// ... existing ...

// 목록 실패·0개면 경고/정보 로그 후 null → 설정 IP
private async Task<HostWifiAdapter?> SelectAdapterOrNullAsync(IHostWifi hostWifi, CancellationToken cancellationToken)
{
    IReadOnlyList<HostWifiAdapter> adapters;
    try { adapters = await hostWifi.ListAdaptersAsync(cancellationToken); }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        logger.LogWarning(ex, "Listing Wi-Fi adapters failed; using configured host");
        return null;
    }
    if (adapters.Count == 0) { logger.LogInformation("No Wi-Fi adapter; using configured host"); return null; }
    return await adapterSelector.SelectAsync(adapters, cancellationToken);
}
```

- **검증**: `DeviceApLocatorTests`에 (1) 지정 어댑터 ID가 `ScanAsync`·`GetCurrentAsync`·`ConnectOpenAsync`에 전달됨, (2) 다른 어댑터가 장치 AP에 연결돼 있어도 지정 어댑터 기준으로 "이미 접속"이 아님 → 접속 시도, (3) `DeviceApCandidates.Filter` 접두사·중복·정렬. App은 이 단계에서 단위 테스트가 없으므로 빌드로 확인한다(흐름 테스트는 U4).
- **선행 단위**: U1

### U3. 설정·KMS 라이브러리 이동

- **대상**: `App/AppConfiguration.cs`의 섹션 클래스 → `DeviceConfig/Configuration/DeviceConfigurationBase.cs`·`Sections.cs`(`public`), `App/Security/ConfigSecretProtector.cs` → `DeviceConfig/Security/ConfigSecretProtector.cs`(`public`), `App/AppConfiguration.cs`(`AppConfiguration : DeviceConfigurationBase`), `App/Program.cs`, csproj 패키지 참조 이동, 테스트 `Configuration/ConfigurationTests.cs`(신규)
- **코드 초안**:

```csharp
namespace Biz.Bizadm.PowerManager.DeviceConfig.Configuration
{
    public abstract class DeviceConfigurationBase : IValidatableConfiguration
    {
        [Property(PropertyType.STRING, required: true)]
        public string ContentRootPath { get; set; } = null!;
        public OsKmsSection OsKms { get; set; } = null!;
        public DeviceSection Device { get; set; } = new();
        public List<WifiProfileSection> WifiProfiles { get; set; } = [];
        public SettingsSection? Settings { get; set; }

        /// <summary>ConnectAp(설정 전송)을 쓰는 설정인지. 보안값 처리와 검증 기준.</summary>
        [YamlIgnore]
        public abstract bool UsesConnectAp { get; }

        // 지금 App 검증 순서(OsKms·Device → Commands → ConnectAp 조건)를 지키려고 공통 부분을 둘로 나눈다
        public virtual void Validate() { ValidateCommon(); ValidateConnectAp(); }

        /// <summary>OsKms 필수·CredentialService 절대 URI, Device 검증(기존 코드 이동).</summary>
        protected void ValidateCommon() { /* ... existing ... */ }

        /// <summary>UsesConnectAp일 때 WifiProfiles 1개 이상·중복 없음·ASCII, Settings 필수(기존 코드 이동).</summary>
        protected void ValidateConnectAp()
        {
            WifiProfiles ??= [];
            if (!UsesConnectAp)
                return;
            // ... existing ...
        }
    }
}
```

```csharp
// App
internal sealed class AppConfiguration : DeviceConfigurationBase
{
    public List<DeviceCommand> Commands { get; set; } = [];
    public override bool UsesConnectAp => Commands?.Contains(DeviceCommand.ConnectAp) == true;

    public override void Validate() { ValidateCommon(); ValidateCommands(); ValidateConnectAp(); }

    private void ValidateCommands()
    {
        Commands ??= [];
        // ... existing: 1개 이상, 종료성 명령은 마지막 ...
    }
}
```

```csharp
// ConfigSecretProtector (public, DeviceConfigurationBase를 받는다)
public static bool HasSecrets(DeviceConfigurationBase config) =>
    config.UsesConnectAp ||
    !string.IsNullOrEmpty(config.Settings?.MqttKey) ||
    config.WifiProfiles.Any(profile => !string.IsNullOrEmpty(profile.Pass));

public DecryptedSecrets Protect(DeviceConfigurationBase config, Action<string> log)
{
    if (settings is not null && config.UsesConnectAp && string.IsNullOrEmpty(settings.MqttKey))
    // ... existing ...
}
```

- App의 `using ...App.Security`는 `...DeviceConfig.Security`로, 섹션 타입 참조는 `...DeviceConfig.Configuration`으로 바꾼다. YAML 키·기본값·오류 메시지는 바꾸지 않는다.
- **검증**: 신규 `ConfigurationTests`(라이브러리 테스트 프로젝트, 테스트용 `DeviceConfigurationBase` 파생 클래스로): `UsesConnectAp`=false면 WifiProfiles·Settings 없어도 통과, true면 누락·중복·비ASCII 오류, `UsesConnectAp`가 YAML 직렬화에 나오지 않음(`[YamlIgnore]`), `HasSecrets`. 기존 `device.sample.yaml`을 App `AppConfiguration`으로 역직렬화·검증 통과는 실행으로 확인(`--config` 샘플 복사본, 장치 없이 설정 단계 통과 로그).
- **선행 단위**: U2

### U4. `DeviceRunner` · `IDeviceRunner` · `DeviceSettingsFactory`

- **대상**: `DeviceConfig/DeviceRunner.cs`(신규: `DeviceSelectors`, `DeviceContext`, `IDeviceRunner`, `DeviceRunner`), `DeviceConfig/Configuration/DeviceSettingsFactory.cs`(신규), `App/DeviceConfigService.cs`(runner 사용), `App/Program.cs`, 테스트 `DeviceRunnerTests.cs`(신규)
- **코드 초안**:

```csharp
public sealed record DeviceSelectors(IWifiAdapterSelector Adapter, IDeviceApSelector DeviceAp);
public sealed record DeviceContext(DeviceSession Session, DeviceEndpoint Endpoint, HostWifiAdapter? Adapter, string Mac);

public interface IDeviceRunner
{
    Task<T> RunAsync<T>(DeviceSelectors selectors, Func<DeviceContext, CancellationToken, Task<T>> action, CancellationToken cancellationToken);
    Task<IReadOnlyList<HostWifiNetwork>> ScanDeviceApsAsync(IWifiAdapterSelector adapterSelector, CancellationToken cancellationToken);
}

public sealed class DeviceRunner(IHostWifi? hostWifi, DeviceSection device, ILoggerFactory loggerFactory,
    Func<string, int, TimeSpan, CancellationToken, Task<IDeviceChannel>>? connect = null) : IDeviceRunner  // connect: 테스트용
{
    public async Task<T> RunAsync<T>(DeviceSelectors selectors, Func<DeviceContext, CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        HostWifiAdapter? adapter = await SelectAdapterOrNullAsync(selectors.Adapter, cancellationToken); // U2 코드 이동
        await using DeviceApLocator locator = new(adapter is null ? null : hostWifi, adapter?.Id, selectors.DeviceAp, apOptions, logger);
        DeviceEndpoint endpoint = await locator.LocateAsync(cancellationToken);
        await using IDeviceChannel channel = await Connect(endpoint.Host, device.Port, timeouts.Response, cancellationToken);
        DeviceSession session = new(channel, timeouts, logger);
        string mac = await session.HandshakeAsync(cancellationToken);
        return await action(new DeviceContext(session, endpoint, adapter, mac), cancellationToken);
    } // channel → locator 순으로 Dispose(연결 종료 후 PC Wi-Fi 복구)

    public async Task<IReadOnlyList<HostWifiNetwork>> ScanDeviceApsAsync(IWifiAdapterSelector adapterSelector, CancellationToken cancellationToken)
    {
        if (hostWifi is null)
            throw new HostWifiException("Host Wi-Fi control is not supported on this OS");
        IReadOnlyList<HostWifiAdapter> adapters = await hostWifi.ListAdaptersAsync(cancellationToken);
        if (adapters.Count == 0)
            throw new HostWifiException("No Wi-Fi adapter found");
        HostWifiAdapter adapter = await adapterSelector.SelectAsync(adapters, cancellationToken);
        return DeviceApCandidates.Filter(await hostWifi.ScanAsync(adapter.Id, cancellationToken), device.ApSsidPrefix);
    }
}

public enum DeviceRunStage { Adapter, DeviceAp, Tcp, Handshake, Command }

/// <summary>DeviceRunner 단계별 실패. 취소(OperationCanceledException)는 감싸지 않는다.</summary>
public sealed class DeviceRunException(DeviceRunStage stage, Exception inner)
    : Exception($"{stage} failed: {inner.Message}", inner)
{
    public DeviceRunStage Stage { get; } = stage;
}

// RunAsync 안: 단계마다 감싼다
//   HostWifiAdapter? adapter = await Stage(DeviceRunStage.Adapter, () => SelectAdapterOrNullAsync(...));
//   locator.LocateAsync → DeviceAp, Connect → Tcp, HandshakeAsync → Handshake, action → Command
private static async Task<T> Stage<T>(DeviceRunStage stage, Func<Task<T>> step)
{
    try { return await step(); }
    catch (Exception ex) when (ex is not OperationCanceledException and not DeviceRunException)
    {
        throw new DeviceRunException(stage, ex);
    }
}
// ScanDeviceApsAsync: 목록·선택 → Adapter, 스캔 → DeviceAp

public static class DeviceSettingsFactory
{
    public static DeviceSettings Create(SettingsSection settings, IReadOnlyList<WifiProfileSection> profiles, DecryptedSecrets secrets) =>
        new() { Mac = settings.Mac, /* ... DeviceConfigService.CreateDeviceSettings 그대로 ... */ };
}
```

```csharp
// App DeviceConfigService
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    await runner.RunAsync(new DeviceSelectors(adapterSelector, apSelector), async (context, ct) =>
    {
        logger.LogInformation("Device MAC {Mac}", context.Mac);
        foreach (DeviceCommand command in config.Commands)
        {
            DeviceResponse response = command == DeviceCommand.ConnectAp
                ? await context.Session.ConnectApAsync(DeviceSettingsFactory.Create(config.Settings!, config.WifiProfiles, secrets), wifiSelector, ct)
                : await context.Session.ExecuteAsync(command, ct);
            logger.LogInformation("{Command} response: {Response}", command, response.Raw);
        }
        return true;
    }, stoppingToken);
    // ... existing: 완료 로그, StopApplication ...
}

// Program: builder.Services.AddSingleton<IDeviceRunner>(sp => new DeviceRunner(HostWifiFactory.Create(loggerFactory), config.Device, loggerFactory));
```

- MCP는 `Settings.Mac`을 무시하므로 U6에서 기동 시 `Settings.Mac`을 비운다(이 단위의 팩토리는 그대로 `settings.Mac`을 쓴다).
- **검증**: `DeviceRunnerTests`(가짜 `IHostWifi`·`MockDevice` 채널): (1) 순서 = 어댑터 목록 → 선택 → 스캔 → 접속 → 핸드셰이크 → action → 연결 종료 → 임시 프로필 삭제 → 복구, (2) action 예외·취소에도 복구, 예외는 단계가 맞는 `DeviceRunException`(취소는 감싸지 않음), (3) `ListAdaptersAsync` 예외·0개·`hostWifi` null → 선택자 미호출, 설정 IP 접속, `DeviceContext.Adapter` null, (4) `ScanDeviceApsAsync` 후보 규칙·접속 안 함, (5) `DeviceSettingsFactory`가 기존 `CreateDeviceSettings`와 같은 값. App은 샘플 설정으로 기동해 어댑터 선택 → 장치 AP 선택 순서로 입력을 받는지 확인(장치 없으면 설정 IP 접속 실패까지).
- **선행 단위**: U3

### U5. `OverLoad` 긴 대기

- **대상**: `DeviceConfig/DeviceSession.cs`(`ExecuteAsync`의 `OverLoad`, `SendTestCommandAsync`), `DeviceResponse.cs`(`DeviceTimeouts` 주석), `DeviceSessionTests`
- **코드 초안**:

```csharp
DeviceCommand.OverLoad => SendTestCommandAsync("OVERLOAD", timeouts.ApScan, allowDisconnect: false, cancellationToken),
// 나머지 테스트 명령은 timeouts.Response

private Task<DeviceResponse> SendTestCommandAsync(string command, TimeSpan timeout, bool allowDisconnect, CancellationToken cancellationToken) =>
    SendAndReceiveAsync($"[DUT<-PC] {command}", DeviceFrame.Test, timeout, allowDisconnect, cancellationToken);

/// <param name="ApScan">apscan·SCAN_AP·OVERLOAD 응답 대기(일반 응답보다 오래 걸린다. OVERLOAD 실측 4.5~4.8s, #5).</param>
```

- **검증**: `DeviceSessionTests`에 `Response` 짧게·`ApScan` 길게 두고 지연 응답하는 `MockDevice`로 `OverLoad`는 성공, `Measurement`는 같은 지연에서 타임아웃.
- **선행 단위**: U4

### U6. MCP 프로젝트 기동

- **대상**: `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/`(신규: csproj, `Program.cs`, `ExitCode.cs`, `McpConfiguration.cs`, `HostWifiHolder.cs`, `device.mcp.sample.yaml`), `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/`(신규 csproj), `Biz.Bizadm.PowerManager.slnx`, `Directory.Packages.props`(`ModelContextProtocol` 2.4.0)
- **코드 초안**:

```csharp
internal sealed class McpConfiguration : DeviceConfigurationBase
{
    public override bool UsesConnectAp => Settings is not null;
}

public sealed record HostWifiHolder(IHostWifi? Value);
```

```csharp
// Program.RunAsync: App과 같은 순서(역직렬화·검증 → ProtectSecrets → 호스트). 진행 메시지는 모두 stderr
DecryptedSecrets secrets = ProtectSecrets(cmdMain.ConfigFilePath, config, Console.Error.WriteLine);
bool macIgnored = !string.IsNullOrEmpty(config.Settings?.Mac);
if (macIgnored)
    config.Settings!.Mac = null;   // 재작성 뒤에 비운다(파일의 값은 남긴다). configure_device는 핸드셰이크 MAC
// ... host 생성 후 macIgnored면 logger.LogWarning("Settings.Mac is ignored by the MCP server; the handshake MAC is used")

// CreateHostApplication
builder.Logging.Services.AddSerilog(c => c
    .MinimumLevel.Information().Enrich.WithCaller()
    .WriteTo.Console(LogEventLevel.Information, CallerEnricherOutputTemplate.Default,
        standardErrorFromLevel: LogEventLevel.Verbose)                  // stdout은 MCP 프로토콜 전용
    .WriteTo.File(/* ... App과 같음 ... */ "Biz.Bizadm.PowerManager.DeviceConfig.Mcp.log" /* ... */));

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(secrets);
builder.Services.AddSingleton(sp => new HostWifiHolder(HostWifiFactory.Create(sp.GetRequiredService<ILoggerFactory>())));
builder.Services.AddSingleton<IDeviceRunner>(sp => new DeviceRunner(
    sp.GetRequiredService<HostWifiHolder>().Value, config.Device, sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<DeviceToolGate>();   // U8
builder.Services
    .AddMcpServer(options => options.ServerInstructions = ServerInstructions.Text)
    .WithStdioServerTransport()
    .WithTools<DeviceTools>();                       // U8 전까지는 list_wifi_adapters만 있는 DeviceTools
```

```csharp
internal static class ServerInstructions
{
    public const string Text = """
        B540 smart plug configuration over the device SoftAP (TCP 5000).
        Connecting to the device AP takes over the selected PC Wi-Fi adapter and cuts its internet.
        Use only with a local LLM, or when the PC has another network path (Wi-Fi + wired, or two Wi-Fi adapters).
        Call list_wifi_adapters first and pass `adapter` for the adapter that is not used for internet.
        """;
}
```

- **검증**: (1) `dotnet run --project src/...Mcp -- --config <샘플 복사본> --log <dir>` 실행 시 stdout에 로그가 없음(stderr만), (2) 표준 입력을 닫으면 종료 코드 0으로 끝남(SDK stdio 호스트가 정지하지 않으면 `IHostApplicationLifetime.StopApplication`을 서버 종료에 연결하는 코드를 이 단위에서 추가), (3) `Commands`가 있는 App 설정을 주면 종료 코드 2, (4) `Settings` 없는 설정이면 KMS 없이 기동, (5) `dotnet package list --vulnerable --include-transitive`/`--deprecated` 결과 없음.
- **선행 단위**: U5

### U7. `McpSelectors` · elicitation

- **대상**: `Mcp/McpSelectors.cs`(신규: `McpSelection`, `McpWifiAdapterSelector`, `McpDeviceApSelector`, `McpWifiProfileSelector`), Mcp.Tests `McpSelectorsTests.cs`, `TestSupport/InMemoryMcp.cs`
- **코드 초안**:

```csharp
/// <summary>인자 일치 → 후보 1개 → elicitation → 미지원이면 오류. 오류는 McpException.</summary>
internal static class McpSelection
{
    public static async Task<T> SelectAsync<T>(McpServer server, string kind, string? requested,
        IReadOnlyList<T> candidates, Func<T, string> value, Func<T, bool> matches, Func<T, string> title, CancellationToken cancellationToken)
    {
        if (requested is not null)
            return candidates.FirstOrDefault(matches)
                ?? throw new McpException($"{kind} '{requested}' not found. Candidates: {Describe(candidates, title)}");
        if (candidates.Count == 1)
            return candidates[0];
        if (server.ClientCapabilities?.Elicitation is null)
            throw new McpException($"Multiple {kind} candidates; pass the argument. Candidates: {Describe(candidates, title)}");

        ElicitResult result = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = $"Select the {kind}",
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = { ["value"] = new ElicitRequestParams.TitledSingleSelectEnumSchema
                {
                    OneOf = candidates.Select(c => new ElicitRequestParams.EnumSchemaOption { Const = value(c), Title = title(c) }).ToList(),
                } },
                Required = ["value"],
            },
        }, cancellationToken);
        string? chosen = result.IsAccepted && result.Content?.TryGetValue("value", out JsonElement element) == true ? element.GetString() : null;
        return candidates.FirstOrDefault(c => value(c) == chosen)
            ?? throw new McpException($"{kind} selection was {result.Action}");
    }
}
```

```csharp
internal sealed class McpWifiAdapterSelector(McpServer server, string? adapter) : IWifiAdapterSelector
{
    public Task<HostWifiAdapter> SelectAsync(IReadOnlyList<HostWifiAdapter> adapters, CancellationToken cancellationToken) =>
        McpSelection.SelectAsync(server, "Wi-Fi adapter", adapter, adapters, a => a.Id,
            a => a.Id == adapter || a.Name == adapter,
            a => $"{a.Name} ({a.Description ?? a.Id}) - connected: {a.ConnectedSsid ?? "none"}", cancellationToken);
}
// McpDeviceApSelector(server, deviceAp): 값·일치 = Ssid, 제목 = "{Ssid} (signal {Signal})"
internal sealed class McpWifiProfileSelector(McpServer server, string? wifiSsid) : IWifiProfileSelector
{
    public WifiProfile? Selected { get; private set; }
    public async Task<WifiProfile> SelectAsync(IReadOnlyList<WifiCandidate> candidates, CancellationToken cancellationToken)
    {
        WifiCandidate chosen = await McpSelection.SelectAsync(server, "Wi-Fi", wifiSsid, candidates, c => c.Profile.Ssid,
            c => c.Profile.Ssid == wifiSsid, c => $"{c.Profile.Ssid} ({c.Rssi} dBm)", cancellationToken);
        return (Selected = chosen.Profile);
    }
}
```

```csharp
// 테스트: Pipe 두 쌍으로 서버·클라이언트 연결. 클라이언트 ElicitationHandler로 선택·거절을 정한다
Pipe clientToServer = new(), serverToClient = new();
await using McpServer server = McpServer.Create(new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()),
    serverOptions /* ToolCollection에 테스트용 tool: 선택자를 호출해 결과를 돌려줌 */);
_ = server.RunAsync(cancellationToken);
await using McpClient client = await McpClient.CreateAsync(
    new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
    new McpClientOptions { Handlers = new() { ElicitationHandler = (request, ct) => ValueTask.FromResult(response) } });
```

- elicitation 미지원은 `ElicitationHandler`를 주지 않은 클라이언트(capability 없음)로 시험한다. 정확한 생성자·속성 이름은 2.4.0 기준으로 구현 시 맞춘다(초안과 다르면 이 문서를 고친다).
- **검증**: `McpSelectorsTests` — 인자 일치(Id·Name)/불일치(후보 목록 포함 오류), 후보 1개 자동, elicitation 선택·거절(`decline`)·취소(`cancel`), 미지원 오류, Wi‑Fi `Selected` 기록.
- **선행 단위**: U6

### U8. `DeviceTools` · 결과 JSON · 동시 호출

- **대상**: `Mcp/DeviceTools.cs`(신규), `Mcp/DeviceToolGate.cs`(신규), `Mcp/DeviceResultParser.cs`(신규: 명령별 해석값), Mcp.Tests `DeviceToolsTests.cs`(가짜 `IDeviceRunner` + 메모리 클라이언트·서버), `DeviceResultParserTests.cs`
- **코드 초안**:

```csharp
internal sealed class DeviceToolGate { public SemaphoreSlim Semaphore { get; } = new(1, 1); }

[McpServerToolType]
internal sealed class DeviceTools(IDeviceRunner runner, HostWifiHolder hostWifi, DeviceToolGate gate,
    McpConfiguration config, DecryptedSecrets secrets, ILogger<DeviceTools> logger)
{
    [McpServerTool(Name = "list_wifi_adapters", ReadOnly = true), Description("PC wireless adapters. Pass `id` or `name` as `adapter` to other tools.")]
    public async Task<IReadOnlyList<HostWifiAdapter>> ListWifiAdaptersAsync(CancellationToken cancellationToken) =>
        await (hostWifi.Value ?? throw new McpException("Host Wi-Fi control is not supported on this OS")).ListAdaptersAsync(cancellationToken);

    [McpServerTool(Name = "factory_test", Destructive = true), Description("... OverLoad: meaning of the value is unconfirmed (#5).")]
    public Task<JsonObject> FactoryTestAsync(McpServer server, FactoryTestCommand command,
        string? adapter = null, string? deviceAp = null, CancellationToken cancellationToken = default) =>
        RunCommandAsync(server, "factory_test", (DeviceCommand)command, adapter, deviceAp, null, cancellationToken);

    [McpServerTool(Name = "configure_device", Destructive = true)]
    public async Task<JsonObject> ConfigureDeviceAsync(McpServer server, string? wifiSsid = null,
        string? adapter = null, string? deviceAp = null, CancellationToken cancellationToken = default)
    {
        if (config.Settings is null)
            throw new McpException("Settings is not configured; configure_device is unavailable");
        McpWifiProfileSelector wifi = new(server, wifiSsid);
        JsonObject result = await RunAsync(server, "configure_device", adapter, deviceAp, (context, ct) =>
            context.Session.ConnectApAsync(DeviceSettingsFactory.Create(config.Settings, config.WifiProfiles, secrets), wifi, ct),
            DeviceCommand.ConnectAp, cancellationToken);
        result["wifiSsid"] = wifi.Selected?.Ssid;
        return result;
    }

    private async Task<JsonObject> RunAsync(McpServer server, string tool, string? adapter, string? deviceAp,
        Func<DeviceContext, CancellationToken, Task<DeviceResponse>>? action, DeviceCommand? command, CancellationToken cancellationToken)
    // action null = get_device_info(핸드셰이크만). RunCommandAsync는 context.Session.ExecuteAsync(command)를 action으로 넘긴다
    {
        logger.LogInformation("{Tool} called (adapter: {Adapter}, deviceAp: {DeviceAp})", tool, adapter, deviceAp);
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            return await runner.RunAsync(new DeviceSelectors(new McpWifiAdapterSelector(server, adapter), new McpDeviceApSelector(server, deviceAp)),
                async (context, ct) =>
                {
                    DeviceResponse response = action is null ? new DeviceResponse("") : await action(context, ct);
                    JsonObject result = new()
                    {
                        ["adapter"] = context.Adapter?.Name, ["deviceAp"] = context.Endpoint.DeviceApSsid, ["host"] = context.Endpoint.Host,
                        ["mac"] = context.Mac, ["command"] = command?.ToString(), ["raw"] = response.Raw, ["disconnected"] = response.Disconnected,
                    };
                    DeviceResultParser.AddParsed(result, command, response);   // 형식이 다르면 아무것도 넣지 않는다
                    return result;
                }, cancellationToken);
        }
        catch (DeviceRunException ex)
        {
            throw new McpException($"{tool} failed at {ex.Stage}: {ex.InnerException!.Message}", ex);   // reboot_device는 "(may reboot without a response, #3)" 덧붙임
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }
}

public enum FactoryTestCommand { ScanAp = DeviceCommand.ScanAp, OnCmd = DeviceCommand.OnCmd, OffCmd = DeviceCommand.OffCmd,
    Measurement = DeviceCommand.Measurement, OverLoad = DeviceCommand.OverLoad, TestFactoryReset = DeviceCommand.TestFactoryReset }
```

```csharp
internal static partial class DeviceResultParser
{
    public static void AddParsed(JsonObject result, DeviceCommand? command, DeviceResponse response)
    {
        switch (command)
        {
            case DeviceCommand.Measurement when Number("MEASUREMENT_W", response.Raw) is { } watts: result["watts"] = watts; break;
            case DeviceCommand.OverLoad when Number("OVERLOAD_A", response.Raw) is { } value: result["overloadA"] = value; break;
            case DeviceCommand.OnCmd or DeviceCommand.OffCmd: result["ok"] = response.Raw.Contains("_CMD_OK#", StringComparison.Ordinal); break;
            case DeviceCommand.ApScan or DeviceCommand.ScanAp when TryParseScan(response.Raw) is { } networks: result["networks"] = networks; break;
        }
    }
    // Number: Regex @"\[DUT->PC\] {key}:(?<v>-?\d+(\.\d+)?)#" → double (InvariantCulture)
    // TryParseScan: ApScanParser.Parse, 실패하면 null. registered = WifiProfiles에 있는 SSID인지(scan_device_wifi)
}
```

- 단계는 U4의 `DeviceRunException.Stage`를 쓴다. 선택자의 `McpException`(인자 불일치·elicitation 거절 등)은 `DeviceRunException`에 감싸여 오므로 `InnerException`이 `McpException`이면 그 메시지를 그대로 돌려준다. `list_device_aps`도 같은 방식이다.
- tool 결과·로그에 비밀번호·`mqtt_key`를 넣지 않는다(결과 필드는 위 목록뿐).
- **검증**: `DeviceResultParserTests` — 설계 6.5 표의 응답 예(`MEASUREMENT_W:1.6#`, `OVERLOAD_A:0.27#`, `ON_CMD_OK#`, SCAN_AP JSON)와 형식 불일치 시 `raw`만. `DeviceToolsTests`(메모리 클라이언트·서버, 가짜 `IDeviceRunner`) — tool 8개 목록·`readOnly`/`destructive` 표시, 공통 결과 필드, `configure_device`의 `wifiSsid`, `Settings` 없음 오류, `DeviceException`/`TimeoutException` → `isError`와 단계 메시지, `reboot_device` 오류에 #3 표기, 동시 호출 2건이 순서대로 실행(가짜 runner가 겹침 감지), `list_wifi_adapters`는 gate를 쓰지 않음.
- **선행 단위**: U7

### U9. 실장비 확인

- **대상**: Mcp.Tests `Device/RealDeviceMcpTests.cs`(`[TestCategory("Device")]`), 결과 문서 `docs/mcp/` 아래(번호는 작성 시 카테고리 다음 순번)
- **코드 초안**:

```csharp
[TestMethod, TestCategory("Device")]
public async Task ListAdaptersAndMeasure()
{
    await using McpClient client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
    {
        Command = ServerExePath, Arguments = ["--config", ConfigPath, "--log", LogDir],
    }));
    CallToolResult adapters = await client.CallToolAsync("list_wifi_adapters");
    CallToolResult info = await client.CallToolAsync("get_device_info", new Dictionary<string, object?> { ["adapter"] = Adapter });
    CallToolResult measurement = await client.CallToolAsync("factory_test",
        new Dictionary<string, object?> { ["command"] = "Measurement", ["adapter"] = Adapter });
    // IsError false, mac·watts 확인. 실행 후 PC Wi-Fi가 원래 SSID로 복구됐는지 확인
}
```

- 서버 exe 경로·설정·어댑터는 환경 변수로 받는다(없으면 `Assert.Inconclusive`). 기존 `RealDeviceTests` 방식을 따른다.
- 설계 10절 미확인 항목(USB 무선 어댑터 이름 매핑, Wi‑Fi+유선 기본 경로, 클라이언트 tool 타임아웃, elicitation 중 장치 TCP 유지)은 이 단위에서 확인할 수 있는 만큼 결과 문서에 적는다.
- **검증**: 실장비에서 `list_wifi_adapters`, `get_device_info`, `factory_test Measurement` 성공, 장치 AP 접속 어댑터 외 어댑터의 연결이 유지되고 접속 어댑터는 원래 연결로 복구됨. App으로 같은 장치에 어댑터 선택 → 기존 명령 실행 회귀 확인.
- **선행 단위**: U8

## 결정 목록 (개발 단계)

| 항목 | 결정 |
|---|---|
| MCP SDK | `ModelContextProtocol` 2.4.0 |
| 작업 단위 | U1~U9(어댑터 지정 → 선택·Locator·App → 설정 이동 → DeviceRunner → OverLoad → MCP 기동 → 선택자 → tool → 실장비) |
| elicitation 스키마 | 제목 있는 단일 선택 enum(`TitledSingleSelectEnumSchema`), 값은 어댑터 Id / SSID |
| MCP 테스트 방식 | Pipe로 연결한 메모리 SDK 클라이언트·서버(제품 코드에 elicitation 추상화를 두지 않음) |
| tool 오류 단계 | `DeviceRunner`가 `DeviceRunException(Stage, inner)`로 감쌈(App 로그에도 단계 표시) |
| MCP `Settings.Mac` | 보안값 재작성 뒤 설정 객체에서 비움(파일 값 유지), 호스트 생성 후 경고 로그 |
