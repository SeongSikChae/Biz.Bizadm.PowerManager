---
status: done
category: mcp
feature: device-config-mcp
phase: 개발
---

# device-config-mcp 개발

선행: [0001 방안](0001-device-config-mcp-방안.md) · [0002 설계](0002-device-config-mcp-설계.md) · GitHub: SeongSikChae/Biz.Bizadm.PowerManager#7

순서: U1 → U2 → U3 → U4 → U5. 단위마다 구현 → 검증 → 커밋 여부 확인. 각 단위는 그 단위까지의 코드만으로 빌드·테스트된다.

공통 검증 명령(저장소 루트):

```powershell
dotnet build Biz.Bizadm.PowerManager.slnx -warnaserror
dotnet test --solution Biz.Bizadm.PowerManager.slnx
```

---

## U1. Runner 명령별 결과 통보 (설계 3절)

**대상**
- `src/Biz.Bizadm.PowerManager.DeviceConfig/DeviceCommandResult.cs` (신규)
- `src/Biz.Bizadm.PowerManager.DeviceConfig/DeviceConfigRunner.cs` `RunAsync` 오버로드
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/DeviceConfigRunnerTests.cs` (테스트 추가)

**코드 초안**

```csharp
using Biz.Bizadm.PowerManager.DeviceConfig.Transport; // DeviceCommand

namespace Biz.Bizadm.PowerManager.DeviceConfig
{
    /// <summary>성공한 명령 하나의 결과. ConnectAp는 "OK (<공유기 SSID>)".</summary>
    public sealed record DeviceCommandResult(DeviceCommand Command, string Response);
}
```

```csharp
public Task RunAsync(CancellationToken cancellationToken) => RunAsync(null, cancellationToken);

public async Task RunAsync(Action<DeviceCommandResult>? onCommandCompleted, CancellationToken cancellationToken)
{
    // ... existing (Wi-Fi 접속·host 결정) ...
    foreach (DeviceCommand command in options.Commands)
    {
        logger.LogInformation("Command {Command}", command);
        if (command == DeviceCommand.ConnectAp)
        {
            string? selectedSsid = null;
            await client.ConnectApAsync(async (scanned, ct) =>
            {
                (string Ssid, string Pass) selected = await SelectWifiAsync(scanned, ct);
                selectedSsid = selected.Ssid;
                return selected;
            }, options.Settings ?? throw new InvalidOperationException("Settings is required for ConnectAp"), cancellationToken);
            onCommandCompleted?.Invoke(new DeviceCommandResult(command, $"OK ({selectedSsid})"));
        }
        else
        {
            string response = await client.ExecuteAsync(command, cancellationToken);
            logger.LogInformation("{Command} response: {Response}", command, response);
            onCommandCompleted?.Invoke(new DeviceCommandResult(command, response));
        }
    }
    // ... existing ...
}
```

**검증**
- 신규 `Run_ReportsEachCompletedCommand`: `[OnCmd, Measurement]` 성공 → 콜백 2회, 응답 문자열 일치.
- 신규 `Run_CommandFails_ReportsOnlyCompleted`: `[OnCmd, OverLoad(무응답)]` → 콜백 1회(OnCmd), 예외 전파, 복구 호출.
- 신규 `Run_ConnectAp_ReportsSelectedSsid`: 응답 `OK (MyRouter)`.
- 기존 `DeviceConfig.Tests` 전체 통과, App 빌드 통과(호출 무변경).

**선행 단위**: 없음

---

## U2. MCP 프로젝트·설정·KMS·stdio 기동 (설계 1·2·4절)

**대상**
- `Directory.Packages.props`: `ModelContextProtocol` 2.4.0 추가(규칙 `dotnet/nuget-package-selection.md`로 취약·Deprecated 재확인)
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/` : csproj(App csproj 복사 + `ModelContextProtocol`, `InternalsVisibleTo` Mcp.Tests), `Program.cs`, `ExitCode.cs`(App과 같은 값), `McpConfiguration.cs`(App `AppConfiguration` 복사·`Commands` 제거), `Security/ConfigSecretProtector.cs`(App 복사, `AppConfiguration` → `McpConfiguration`)
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/` : csproj(MSTest, `EnableMSTestRunner`, OutputType Exe, Mcp 참조), `McpConfigurationTests.cs`
- `Biz.Bizadm.PowerManager.slnx`에 두 프로젝트 추가

**코드 초안**

```xml
<ItemGroup>
  <PackageReference Include="ModelContextProtocol" />
  <!-- ... App과 같은 패키지 ... -->
</ItemGroup>
<ItemGroup>
  <InternalsVisibleTo Include="Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests" />
</ItemGroup>
```

```csharp
internal sealed class McpConfiguration : IValidatableConfiguration
{
    // ContentRootPath, OsKms, Device, WifiProfiles, Settings — App과 같은 섹션 타입(복사)
    public void Validate()
    {
        // ... OsKms·Device 검사: App과 같음 ...
        WifiProfiles ??= [];
        foreach (WifiProfileSection profile in WifiProfiles)
            ConfigurationValidator.Validate(profile);
        string? duplicate = WifiProfiles.GroupBy(p => p.Ssid, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate WifiProfiles.Ssid: {duplicate}");
        if (Settings is null)
            return;
        ConfigurationValidator.Validate(Settings);
        ValidatePort("Settings.ApiServerPort", Settings.ApiServerPort);
        ValidatePort("Settings.ServerPort", Settings.ServerPort);
    }

    /// <summary>Commands는 tool 호출마다 채운다.</summary>
    public DeviceConfigOptions ToOptions(IReadOnlyList<WifiProfile> wifiProfiles) => new()
    {
        Host = Device.Host, Port = Device.Port, /* ... App CreateOptions와 같음 ... */
        Commands = [],
        WifiProfiles = wifiProfiles,
        Settings = Settings is not { } s ? null : new DeviceSettings { /* ... */ },
    };
}
```

```csharp
// Program.RunAsync: App과 같은 순서. Console.WriteLine → Console.Error.WriteLine. 경로는 변환하지 않는다(절대 경로 가정)
using (host)
{
    // host.RunAsync()는 finally에서 Host를 dispose해 catch에서 로그를 남길 수 없으므로 쓰지 않는다(U2 검토 반영)
    ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();
    try
    {
        await host.StartAsync();
        await host.WaitForShutdownAsync(); // stdin 종료 → SDK가 Host 정지(실측 확인), 정지 후 StopAsync까지 호출
        return ExitCode.Success;
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Host terminated unexpectedly");
        return ExitCode.RuntimeError;
    }
}
```

```csharp
builder.Logging.ClearProviders();
builder.Logging.Services.AddSerilog(c => c
    .MinimumLevel.Information()
    .Enrich.WithCaller()
    .WriteTo.Console(LogEventLevel.Information, CallerEnricherOutputTemplate.Default,
        standardErrorFromLevel: LogEventLevel.Verbose) // stdout은 MCP 전용
    .WriteTo.File(/* ... App과 같음, 파일 이름만 Biz.Bizadm.PowerManager.DeviceConfig.Mcp.log ... */));

builder.Services.AddSingleton(sp => HostWifiLoader.CreateForCurrentOs(sp.GetRequiredService<ILogger<DeviceConfigRunner>>()));
builder.Services.AddMcpServer().WithStdioServerTransport(); // tool 등록은 U4
```

- `WriteTo.Console(level, template, standardErrorFromLevel:)`는 Serilog.Sinks.Console 기본 overload로 예상한다. 빌드에서 인자가 없다고 나오면 `CallerEnricherOutputTemplate.Default`의 형식을 확인하고, Console sink 대신 stderr 전용 `TextWriter` sink(`WriteTo.TextWriter(Console.Error, …)`)로 바꾼다.

**검증**
- 패키지 추가 전 `ModelContextProtocol` 2.4.0의 존재·취약·Deprecated와 의존성 버전을 확인한다. `Microsoft.Extensions.*`를 10.0.14보다 높게 요구해 NU1605가 나면 CPM의 해당 버전을 요구 버전의 최신 패치로 올린다(사용자 확인 후).
- `dotnet build -warnaserror` 통과.
- `McpConfigurationTests`: `Validate_NoWifiProfilesNoSettings_Ok`, `Validate_WifiProfilesWithoutSettings_Ok`, `Validate_DuplicateSsid_Throws`, `Validate_SettingsPortOutOfRange_Throws`, `Validate_OsKmsMissing_Throws`, `ToOptions_CopiesDeviceAndSettings`(`Lati`·`Long`은 기본값이 아닌 값으로 확인).
- exe 실측(임시 폴더, 절대 경로): 인수 없음 → 종료 코드 1. 없는 설정 파일 → 2(stderr `Configuration error`). WifiProfiles 없는 정상 설정 + stdin 즉시 EOF(`Process`로 실행 후 stdin 닫기) → 종료 코드 0, stdout 0바이트, `ContentRootPath`에 DEK 파일이 생기지 않음. stdin EOF로 종료되지 않으면 원인을 기록하고 이 단위에서 처리 방식을 정한다.
- KMS 수동 1회: 평문 `Pass` 설정(테스트 전용 `CredentialService`)으로 기동 → 파일에 `DEKENC:`·`Salt` 기록, stdout 0바이트. 끝나면 테스트 자격 증명·DEK 파일을 지운다.

**선행 단위**: U1

---

## U3. 인자 선택기 (`ParameterDeviceConfigSelector`) (설계 5.2절)

**대상**
- `src/.../Mcp/ParameterDeviceConfigSelector.cs`
- `tests/.../Mcp.Tests/ParameterDeviceConfigSelectorTests.cs`

**코드 초안**

```csharp
internal sealed class ParameterDeviceConfigSelector(string adapterId, string deviceApSsid, string? wifiSsid) : IDeviceConfigSelector
{
    public Task<WifiAdapter> SelectAdapterAsync(IReadOnlyList<WifiAdapter> adapters, CancellationToken cancellationToken) =>
        Task.FromResult(adapters.FirstOrDefault(a => string.Equals(a.Id, adapterId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Adapter not found: {adapterId}"));

    public Task<WifiNetwork> SelectDeviceApAsync(IReadOnlyList<WifiNetwork> deviceAps, CancellationToken cancellationToken) =>
        Task.FromResult(deviceAps.FirstOrDefault(n => n.Ssid == deviceApSsid)
            ?? throw new ArgumentException($"Device AP not found in scan: {deviceApSsid}"));

    public Task<WifiProfile> SelectWifiProfileAsync(IReadOnlyList<WifiProfileCandidate> candidates, CancellationToken cancellationToken)
    {
        if (wifiSsid is null)
            throw new ArgumentException("wifiSsid is required for ConnectAp");
        return Task.FromResult(candidates.FirstOrDefault(c => c.Profile.Ssid == wifiSsid)?.Profile
            ?? throw new ArgumentException($"Wi-Fi not in device apscan result: {wifiSsid}"));
    }
}
```

**검증**: 일치(어댑터 ID 대소문자 무시), 각 불일치 예외 메시지, `wifiSsid` null 예외.

**선행 단위**: U2

---

## U4. tool (`DeviceConfigService`, `DeviceConfigTools`) (설계 5절)

**대상**
- `src/.../Mcp/DeviceConfigService.cs` (싱글턴: 세마포어·로직, 결과 record)
- `src/.../Mcp/DeviceConfigTools.cs` (`[McpServerToolType]`, 얇은 래퍼)
- `Program.cs`: `DeviceConfigService.ServiceSettings`·`DeviceConfigService` 등록, `.WithTools<DeviceConfigTools>()`
- `tests/.../Mcp.Tests/`: `DeviceConfigServiceTests.cs`(옵션·서비스 생성 도우미는 이 클래스에 새로 작성: 기존 `DeviceConfigRunnerTests`의 `Options`/`Runner`는 private이라 공유하지 않음), `TestSupport/SyncProgress.cs`(동기로 기록하는 `IProgress<ProgressNotificationValue>`), csproj에 기존 `DeviceConfig.Tests/TestSupport/*.cs`를 `<Compile Include="..\Biz.Bizadm.PowerManager.DeviceConfig.Tests\TestSupport\*.cs" Link="TestSupport\%(Filename)%(Extension)" />`로 공유(원본 수정 없음), `TestSupport/GatedHostWifi.cs`(신규: `IHostWifi` 래퍼, `ConnectOpenAsync`에서 지연·동시 진입 수 기록)

**코드 초안**

```csharp
internal sealed class DeviceConfigService(IHostWifi hostWifi, DeviceConfigService.ServiceSettings settings, ILogger<DeviceConfigRunner> logger)
{
    public sealed record ServiceSettings(DeviceConfigOptions BaseOptions, IReadOnlyList<WifiProfile> WifiProfiles);
    public sealed record AdapterInfo(string Id, string Name, string? CurrentSsid);
    public sealed record AdaptersResult(IReadOnlyList<AdapterInfo> Adapters);
    public sealed record DeviceApsResult(IReadOnlyList<WifiNetwork> DeviceAps);
    public sealed record WifiProfilesResult(IReadOnlyList<string> Ssids);
    public sealed record CommandResult(string Command, string Response);
    public sealed record RunResult(bool Success, IReadOnlyList<CommandResult> Results, string? Error);

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
            .OrderByDescending(n => n.Signal).ToList());
    }, ct);

    public WifiProfilesResult ListWifiProfiles() => new(settings.WifiProfiles.Select(p => p.Ssid).ToList()); // 잠그지 않음

    public Task<RunResult> RunAsync(string adapterId, string deviceApSsid, IReadOnlyList<string> commands, string? wifiSsid,
        IProgress<ProgressNotificationValue>? progress, CancellationToken ct) => LockedAsync(async () =>
    {
        List<CommandResult> results = [];
        try
        {
            IReadOnlyList<DeviceCommand> parsed = ParseCommands(commands);
            if (parsed.Contains(DeviceCommand.ConnectAp))
            {
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

    /// <summary>열거형 이름과 정확히 일치만 허용(Enum.TryParse는 "1"·"OnCmd,OffCmd"를 통과시킴).</summary>
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
        try { return await action(); }
        finally { gate.Release(); }
    }
}
```

```csharp
[McpServerToolType]
internal sealed class DeviceConfigTools(DeviceConfigService service)
{
    [McpServerTool(Name = "list_wifi_adapters", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Lists this PC's wireless adapters and their currently connected SSID. Does not change the connection.")]
    public Task<DeviceConfigService.AdaptersResult> ListWifiAdapters(CancellationToken cancellationToken) =>
        ToMcpError(() => service.ListAdaptersAsync(cancellationToken));

    [McpServerTool(Name = "run_device_commands", ReadOnly = false, Destructive = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Connects the PC Wi-Fi adapter to the device AP, runs the commands in order (new TCP connection per command), then requests reconnection to the previous Wi-Fi. The adapter is offline while this runs and may stay offline briefly after the response. Commands: ConnectAp (send router Wi-Fi + server settings, requires wifiSsid), ApScan, Reboot, FactoryReset, ScanAp, OnCmd, OffCmd, Measurement, OverLoad, TestFactoryReset. State-changing: ConnectAp, Reboot, FactoryReset, TestFactoryReset, OnCmd, OffCmd.")]
    public Task<DeviceConfigService.RunResult> RunDeviceCommands(
        [Description("Adapter id from list_wifi_adapters")] string adapterId,
        [Description("Device AP SSID from scan_device_aps")] string deviceApSsid,
        [Description("Command names in execution order")] string[] commands,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken cancellationToken,
        [Description("Router SSID from list_wifi_profiles; required for ConnectAp")] string? wifiSsid = null) =>
        service.RunAsync(adapterId, deviceApSsid, commands, wifiSsid, progress, cancellationToken);

    // scan_device_aps(adapterId), list_wifi_profiles: ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true

    /// <summary>조회 tool 실패 원인이 클라이언트에 보이도록 McpException으로 바꾼다.</summary>
    private static async Task<T> ToMcpError<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (NotImplementedException) { throw new McpException("PC Wi-Fi is not implemented on this OS"); }
        catch (Exception ex) when (ex is not (OperationCanceledException or McpException))
        { throw new McpException($"{ex.GetType().Name}: {ex.Message}"); }
    }
}
```

- SDK 바인딩(`IProgress<ProgressNotificationValue>`·`CancellationToken`은 schema에서 제외, 기본값 있는 인자는 비필수)은 U5의 `tools/list` 검사로 확인한다. 다르면 이 단위로 돌아와 시그니처를 고친다.

**검증** (`FakeHostWifi` + `MockDevice` 링크 재사용)
- `ListAdapters_ReturnsCurrentSsid`, `ScanDeviceAps_FiltersPrefix_SortsBySignal`, `ScanDeviceAps_UnknownAdapter_Throws`
- `Run_Success_ReturnsResults_Restores`, `Run_ReportsProgress`(`SyncProgress`로 0/n, 1/n … n/n)
- `Run_MidFailure_ReturnsCompletedAndError_Restores`
- `Run_ConnectApWithoutSettings_FailsBeforeWifi`, `Run_ConnectApWithoutOrUnknownWifiSsid_FailsBeforeWifi`(둘 다 `FakeHostWifi.Calls` 비어 있음)
- `Run_UnknownCommand_Fails`(`"Foo"`, `"1"`, `"OnCmd,OffCmd"`, `"oncmd"`), `Run_EmptyCommands_Fails`
- `Run_NotImplementedWifi_UsesHost`
- `Run_Cancelled_PropagatesOce_Restores`: 장치 AP 접속 후 명령 대기 중 토큰 취소 → `OperationCanceledException`이 그대로 전파(`RunResult`로 바뀌지 않음), `FakeHostWifi.Restored` 기록됨
  - 취소는 `ConnectOpenAsync` 뒤에 건다(예: `MockDevice` `respond` 콜백 안에서 `cts.Cancel()`, 응답 null). `Timeout`은 크게 잡는다.
- `Tools_NotImplemented_ThrowsMcpException`: `FakeHostWifi.ThrowOnListAdapters`/`ThrowOnScan = new NotImplementedException()` → `list_wifi_adapters`·`scan_device_aps`가 `McpException("PC Wi-Fi is not implemented on this OS")`
- `Lookup_WaitsWhileRunInProgress`: `GatedHostWifi`로 run을 `ConnectOpenAsync`에서 멈춘 동안 `list_wifi_adapters`가 끝나지 않고, run 해제 후 끝남
- `Run_ConnectAp_DoesNotLogPass`: ConnectAp 성공 시 캡처 logger(`ListLogger`) 전체 텍스트에 `Pass` 값이 없음
- `Run_ConcurrentCalls_Serialized`(`GatedHostWifi`로 `ConnectOpenAsync`에 지연을 주고 두 호출 동시 시작 → 최대 동시 진입 1)
- `ListWifiProfiles_SsidsOnly`
- `Tools_LookupFailure_ThrowsMcpExceptionWithMessage`(`DeviceConfigTools` 직접 호출)

**선행 단위**: U3

---

## U5. stdio 통합 테스트 (설계 6절)

**대상**
- `tests/.../Mcp.Tests/StdioIntegrationTests.cs`
- Mcp.Tests csproj: Mcp 프로젝트 **자체 출력 폴더**의 exe 경로를 `AssemblyMetadata`로 주입(테스트 출력 폴더의 Mcp.dll은 deps.json·runtimes 자산이 실제와 다를 수 있으므로 쓰지 않는다)

**코드 초안**

```xml
<ItemGroup>
  <AssemblyAttribute Include="System.Reflection.AssemblyMetadataAttribute">
    <_Parameter1>McpExePath</_Parameter1>
    <!-- 확장자 없는 apphost 경로. 테스트에서 Windows면 ".exe"를 붙인다 -->
    <_Parameter2>$(MSBuildThisFileDirectory)..\..\src\Biz.Bizadm.PowerManager.DeviceConfig.Mcp\bin\$(Configuration)\$(TargetFramework)\Biz.Bizadm.PowerManager.DeviceConfig.Mcp</_Parameter2>
  </AssemblyAttribute>
</ItemGroup>
```

```csharp
[TestMethod]
public async Task Stdio_ListsFourTools_WithAnnotations()
{
    string config = WriteConfig(TestContext.TestRunDirectory!, wifiProfiles: false); // 절대 경로, KMS 미사용
    await using McpClient client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
    {
        Command = McpExePath,
        Arguments = ["--config", config, "--log", LogDir],
    }), cancellationToken: TestContext.CancellationToken);

    IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: TestContext.CancellationToken);
    CollectionAssert.AreEquivalent(new[] { "list_wifi_adapters", "scan_device_aps", "list_wifi_profiles", "run_device_commands" },
        tools.Select(t => t.Name).ToArray());
    Tool run = tools.Single(t => t.Name == "run_device_commands").ProtocolTool;
    Assert.IsTrue(run.Annotations!.DestructiveHint);
    // readOnlyHint: 나머지 3개 true. inputSchema.required에 wifiSsid·progress·cancellationToken 없음
}
```

- `Stdio_ListWifiProfiles_ReturnsEmptySsids`: `structuredContent`가 `{ssids:[]}`. (`outputSchema`는 `Stdio_ListsFourTools_WithAnnotations`에서 4개 tool 모두 확인)
- `Stdio_ScanUnknownAdapter_IsError`: `scan_device_aps("nope")` → `isError` true, 텍스트에 원인이 포함됨. SDK가 `An error occurred invoking '…': <원인>` 형식으로 감쌀 수 있으므로 "`An error occurred invoking 'scan_device_aps'.`(원인 없는 일반 문구)와 같지 않음"으로 단언한다. 원인 문구는 환경(`Adapter not found`, WLAN 서비스 오류, `not implemented`)마다 달라 특정 문구는 단언하지 않는다.
- 실행 중 stdin 종료(**수동 실측**, 실장비 필요): 장치 AP 접속이 **성공한 뒤** 오래 걸리는 상태에서 클라이언트를 종료해야 한다. 존재하지 않는 장치 AP로는 접속 전에 실패해 복구 자체가 없으므로 판단 근거가 될 수 없다. 방법: 강제 종료와 구분하기 위해 SDK 클라이언트 대신 `Process`로 서버를 띄우는 작은 하네스를 쓴다(initialize → `tools/call run_device_commands [Reboot]` JSON-RPC를 직접 씀). 실장비에서 `Reboot`(응답 없이 `TimeoutSeconds`까지 대기) 또는 `TimeoutSeconds`를 크게 한 설정으로 호출 → 로그에 `Connected to device AP`가 찍힌 뒤 `StandardInput.Close()`만 한다(프로세스 kill 금지) → 기록: stdin 닫은 시각~프로세스 종료 시간, `PC Wi-Fi restored`·OCE 로그 유무, `netsh wlan show interfaces`의 실제 SSID. 3회 반복한다. 로그 유실과 구분하기 위해 Host dispose로 Serilog가 flush되는지(종료 직전 로그가 파일에 남는지) 먼저 확인한다. 남지 않으면 설계 2절대로 `DeviceConfigService`를 `IHostedService`로 등록해 `StopAsync`에서 세마포어를 획득·해제하도록 U4에 추가하고 다시 확인한다. 이때 `HostOptions.ShutdownTimeout`(기본 30초)은 최악 실행 시간(`ApConnectTimeoutSeconds` + 4 + (일반 명령 수 × 3 + `ConnectAp` 수 × 5) × `TimeoutSeconds`)보다 짧을 수 있고, 클라이언트도 일정 시간 뒤 강제 종료할 수 있으므로, 대기 상한과 강제 종료 시 복구가 보장되지 않는다는 점을 결과와 함께 기록한다.
  - U5 결과(2026-11-11, `DWD-LS120_501f96` 2회 + `DWD-LS120_1055b3` 1회, `TimeoutSeconds` 40, `Reboot`): 3회 모두 stdin이 닫히면 `transport completed reading messages`가 찍힌 뒤에도 SDK가 진행 중인 `run_device_commands`가 끝날 때까지 기다렸다. stdin은 1·3회차는 `StandardInput.Close()`, 2회차는 하네스 스크립트 오류로 하네스 프로세스가 끝나며 파이프가 닫혔다(어느 경우도 서버 kill 아님). Reboot 무응답 → `treated as sent` → `PC Wi-Fi restored` → `run_device_commands completed` → Host 종료 순서이며, stdin 종료부터 Host 종료 로그까지 1회차 20.2초, 2회차 20.0초, 3회차 17.8초(3회차만 프로세스 종료 코드 0 기록, 1·2회차는 미기록)였다. 종료 직전 로그(`shut down`)까지 파일에 남아 Serilog flush도 확인했다. 대기 중 OCE 로그가 없어 SDK가 tool 토큰을 취소하지 않고 완료를 기다린 것으로 보며, 따라서 `IHostedService` 종료 대기는 추가하지 않는다. 확인된 대기 범위는 약 20초까지다: 3회 모두 `TimeoutSeconds`(40초)가 아니라 송신 후 약 19~22초에 장치가 연결을 끊어 끝났다. 30초를 넘는 대기(`HostOptions.ShutdownTimeout` 기본 30초 초과, 최악 실행 시간 `ApConnectTimeoutSeconds` + 4 + (일반 명령 수 × 3 + `ConnectAp` 수 × 5) × `TimeoutSeconds`)는 실측하지 않았다. 3회 모두 PC Wi‑Fi 연결 해제 상태에서 시작해 "이전 SSID로 재연결" 경로는 이 실측에서 보지 않았다(cli U8에서 확인). 클라이언트가 프로세스를 강제 종료하면 복구는 보장되지 않는다. 1회차는 대상 장치를 사용자에게 확인받지 않고 신호가 가장 센 장치를 자동 선택해 실행한 것으로, 이후 실장비 명령은 대상 SSID를 확인받은 뒤에만 보낸다.
- `Exe_InvalidConfig_ExitCode2`: `System.Diagnostics.Process`로 직접 실행(OsKms 없는 설정) → `ExitCode == 2`, stderr에 `Configuration error`, stdout 빈 문자열.

**검증**: `dotnet test --solution` 전체 통과, `dotnet build -warnaserror` 경고 0.

**선행 단위**: U4

---

## 완료 후

### 등록 안내

빌드 출력(또는 `dotnet publish`)의 `Biz.Bizadm.PowerManager.DeviceConfig.Mcp.exe`를 stdio MCP 서버로 등록한다. Claude Code 예시:

```powershell
claude mcp add device-config -- D:\Tools\DeviceConfigMcp\Biz.Bizadm.PowerManager.DeviceConfig.Mcp.exe --config D:\Tools\DeviceConfigMcp\config.yaml --log D:\Tools\DeviceConfigMcp\log
```

- `--config`, `--log`, 설정의 `ContentRootPath`는 **절대 경로**로 쓴다. 상대 경로는 클라이언트가 서버를 띄우는 작업 폴더 기준이 되어 DEK 파일·로그 위치가 달라진다.
- 설정 형식은 설계 4절. `WifiProfiles[].Pass`는 첫 기동 때 OsKms로 암호화(`DEKENC:`)되어 설정 파일에 다시 기록되므로, 서버 계정이 설정 파일에 쓸 수 있어야 한다.
- `run_device_commands`는 오래 걸릴 수 있다. `TimeoutSeconds`는 명령마다 한 번이 아니라 단계마다 따로 걸린다(TCP 연결, `hello` 응답, 명령 응답: 일반 명령 최대 3번, `ConnectAp`는 연결·`hello`·`apscan`·설정 JSON·`connectap`으로 최대 5번). Windows는 장치 AP 스캔에 4초를 더 기다린다. 응답이 없는 `Reboot`·`TestFactoryReset`은 보통 대기 시간을 다 쓰거나 장치가 연결을 끊을 때까지 기다린다. 최악 실행 시간은 `ApConnectTimeoutSeconds` + 4 + (일반 명령 수 × 3 + `ConnectAp` 수 × 5) × `TimeoutSeconds`(초)다.
- Claude Code는 `MCP_TOOL_TIMEOUT`(ms)을 위 최악 실행 시간보다 크게 잡는다. 환경 변수는 `claude`를 띄우기 **전에** 그 셸에서 설정해야 적용된다(예: `$env:MCP_TOOL_TIMEOUT = "300000"` 후 `claude` 실행). 매번 설정하지 않으려면 Claude Code settings.json의 `env`에 `"MCP_TOOL_TIMEOUT": "300000"`을 넣는다. 300000ms는 기본값(`TimeoutSeconds` 10, `ApConnectTimeoutSeconds` 30)에서 `ConnectAp` 1개 + 일반 명령 7개까지 덮는다. 시간 초과가 나도 서버는 실행을 끝까지 마치고 PC Wi‑Fi를 복구한다(클라이언트만 응답을 받지 못함).
- 실행 중에는 PC Wi‑Fi가 장치 AP로 바뀌므로 그동안 이 PC의 네트워크가 끊긴다. 응답 직후 원래 Wi‑Fi로 재연결 중일 수 있다.
- 한 번에 하나의 클라이언트 세션에서만 쓴다(여러 프로세스가 같은 어댑터를 쓰는 경우는 직렬화하지 않음).
- 주변에 장치가 여러 대일 수 있으므로 `run_device_commands`의 `deviceApSsid`는 대상 장치를 확인한 뒤 지정한다. 상태 변경 명령(`ConnectAp`, `Reboot`, `FactoryReset`, `TestFactoryReset`, `OnCmd`, `OffCmd`)은 특히 주의한다.

### 수동 확인 결과

2026-11-11 11:24, MCP SDK 클라이언트(`ModelContextProtocol` 2.4.0 `McpClient` + `StdioClientTransport`)로 빌드한 exe를 실행했다(Claude Code 등록은 하지 않음: 사용자 설정을 바꾸지 않기 위해 SDK 클라이언트로 대신 확인).

- `tools/list`: tool 4개.
- `list_wifi_adapters`: Intel Wi‑Fi 6 AX201 어댑터 1개.
- `scan_device_aps`: `DWD-` 장치 AP 5개.
- `run_device_commands [Measurement]` → 사용자가 지정한 `DWD-LS120_501f96`: `success: true`, `MEASUREMENT_W:0.0#`. 끝난 뒤 PC Wi‑Fi는 시작할 때와 같은 연결 해제 상태, 남은 서버 프로세스 없음.

### 남은 확인(미검증 범위)

- 30초를 넘는 실행 도중 클라이언트가 stdin을 닫는 경우(U5 실측은 약 20초까지).
- MCP 경로에서 "이전 SSID로 재연결"(실측이 모두 연결 해제 상태에서 시작, cli U8에서는 확인).
- Claude Code에 실제로 등록해 쓰는 경우.
