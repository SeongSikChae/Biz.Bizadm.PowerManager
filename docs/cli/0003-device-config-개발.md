---
status: done
category: cli
feature: device-config
phase: 개발
---

# device-config 개발 계획

선행: [0001-device-config-방안](0001-device-config-방안.md) · [0002-device-config-설계](0002-device-config-설계.md) · GitHub: SeongSikChae/Biz.Bizadm.PowerManager#2

## 0. 확인 결과 (설계 11절)

| 항목 | 결과 |
|---|---|
| SDK | .NET SDK `10.0.403` |
| `ConfigurationValidator` | `PropertyType`은 스칼라·enum만 지원(객체·목록 없음). 사용자 정의 검증은 `IValidatableConfiguration.Validate()`. → 최상위 스칼라는 `[Property]`, 중첩 섹션·목록·조건부 필수는 `AppConfiguration.Validate()`에서 처리 |
| KMS(OsKms) | `OsKekCredentialProvider.CreateForCurrentOs(service, account, OsKekCredentialDefaults.Namespace)` → `IOsKekCredentialStore.StorePassword` / `AesGcmKekManager.Create(store, salt, iterations)` / `AesGcmDekCipher.Create(manager.Current, FileInfo)` / `new DekCipherService(dek)` |

### 패키지 버전 (nuget.org 안정판 최신 패치, 2026-11-10 조회)

| 패키지 | 버전 | 사용 |
|---|---|---|
| Biz.Bizadm.KMS | 1.0.6 | App |
| Biz.Bizadm.KMS.Credentials | 1.0.2 | App |
| Biz.Bizadm.System.Configuration.Extensions | 1.0.1 | App |
| Biz.Bizadm.Serilog.Enrichers.Caller | 1.0.0 | App |
| Biz.Bizadm.Serilog.Sinks.File.Extensions | 1.0.0 | App |
| CommandLineParser | 2.9.1 | App |
| Microsoft.Extensions.Hosting | 10.0.14 | App |
| Microsoft.Extensions.Logging.Abstractions | 10.0.14 | DeviceConfig |
| Serilog.Extensions.Hosting | 10.0.0 | App |
| Serilog.Sinks.Console | 6.1.1 | App |
| YamlDotNet | 18.1.0 | App |
| MSTest | 4.5.2 | Tests |

취약·Deprecated 여부는 U1에서 `dotnet package list --vulnerable --include-transitive`, `--deprecated`로 확인한다.

## 작업 단위

순서대로 구현하고, 각 단위의 검증이 통과해야 다음 단위로 간다. 경로는 저장소 루트 기준이다.

### U1. 솔루션 골격

- **대상**: `Directory.Build.props`, `Directory.Packages.props`, `Biz.Bizadm.PowerManager.slnx`, `src/Biz.Bizadm.PowerManager.DeviceConfig/*.csproj`, `src/Biz.Bizadm.PowerManager.DeviceConfig.App/*.csproj`, `tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/*.csproj`, `global.json`(MTP 러너 지정이 필요할 때만)
- **코드 초안**:

```xml
<!-- Directory.Build.props -->
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
</PropertyGroup>
```

```xml
<!-- tests/.../Biz.Bizadm.PowerManager.DeviceConfig.Tests.csproj -->
<PropertyGroup>
  <EnableMSTestRunner>true</EnableMSTestRunner>
  <OutputType>Exe</OutputType>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="MSTest" />
  <ProjectReference Include="..\..\src\Biz.Bizadm.PowerManager.DeviceConfig\Biz.Bizadm.PowerManager.DeviceConfig.csproj" />
</ItemGroup>
```

```xml
<!-- src/Biz.Bizadm.PowerManager.DeviceConfig/...csproj -->
<PropertyGroup>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks> <!-- U6 [LibraryImport] 구조체·포인터 마샬링 -->
</PropertyGroup>
<ItemGroup>
  <InternalsVisibleTo Include="Biz.Bizadm.PowerManager.DeviceConfig.Tests" />
  <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
</ItemGroup>
```

- **검증**: `dotnet build Biz.Bizadm.PowerManager.slnx` 종료 코드 0·경고 0, `dotnet test` 실행 가능(테스트 0개), 취약·Deprecated 조회 결과 없음
- **선행 단위**: 없음

### U2. 메시지 모델

- **대상**: `DeviceConfig/DeviceCommand.cs`, `DeviceSettings.cs`, `WifiProfile.cs`, `Messages/DeviceSettingsMessage.cs`, `Messages/DeviceCommandMessage.cs`, `Messages/DeviceMessageSerializer.cs`
- **코드 초안**:

```csharp
public enum DeviceCommand
{
    ConnectAp, ApScan, Reboot, FactoryReset,
    ScanAp, OnCmd, OffCmd, Measurement, OverLoad, TestFactoryReset,
}

internal sealed class DeviceSettingsMessage
{
    [JsonPropertyName("mac")] public string? Mac { get; init; }
    [JsonPropertyName("api_server_addr")] public string? ApiServerAddr { get; init; }
    // ... 설계 5절 순서 그대로 ...
    [JsonPropertyName("mqtt_key")] public string? MqttKey { get; init; }
    [JsonPropertyName("LFota_Url")] public string? FotaUrl { get; init; }
}

internal static class DeviceMessageSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 장치 파서에 \uXXXX 회피
    };

    public static string Serialize<T>(T message) => JsonSerializer.Serialize(message, Options);

    public static string Mask(string json) =>
        Regex.Replace(json, "\"(pass|mqtt_key)\":\"(?:[^\"\\\\]|\\\\.)*\"", "\"$1\":\"***\"");
}
```

- **검증**: 단위 테스트 — 키 이름·순서, null 생략, `LFota_*` 포함, `Mask` 결과에 원래 값 없음
- **선행 단위**: U1

### U3. 수신 프레이밍과 TCP 채널

- **대상**: `DeviceConfig/Transport/DeviceFrame.cs`, `DeviceFrameBuffer.cs`, `IDeviceChannel.cs`, `TcpDeviceChannel.cs`
- **코드 초안**:

```csharp
public enum DeviceFrame { Test, Json }

// 순수 버퍼 로직(소켓 없음)으로 테스트 가능하게 분리
internal sealed class DeviceFrameBuffer
{
    private readonly StringBuilder buffer = new();

    public void Append(ReadOnlySpan<byte> data) => buffer.Append(Encoding.ASCII.GetString(data));

    public bool TryTake(DeviceFrame frame, out string message)
    {
        TrimLeadingWhitespace();   // 앞 메시지 뒤에 남은 \r\n·공백 제거 (TryTakeEvent도 먼저 호출)
        string text = buffer.ToString();
        int end = frame == DeviceFrame.Test ? text.IndexOf('#') : FindJsonEnd(text);
        if (end < 0) { message = string.Empty; return false; }
        message = text[..(end + 1)].Trim();
        buffer.Remove(0, end + 1);
        return true;
    }

    private static int FindJsonEnd(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && depth > 0 && --depth == 0) return i;
            else if (text[i] == '\n' && depth == 0 && i > 0) return i;
        }
        return -1;
    }
}

// TcpDeviceChannel.ReceiveAsync
public async Task<string> ReceiveAsync(DeviceFrame frame, TimeSpan timeout, CancellationToken ct)
{
    using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(timeout);
    while (true)
    {
        // 비요청 이벤트는 프레임 종류와 무관하게 먼저 걸러낸다
        while (buffer.TryTakeEvent(out string evt)) logger.LogInformation("Device event: {Event}", evt);
        if (buffer.TryTake(frame, out string message)) return message;
        int read;
        try { read = await stream.ReadAsync(readBuffer, cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TimeoutException($"Device response timeout ({frame}): {buffer.Peek()}"); }
        if (read == 0) throw new DeviceDisconnectedException(buffer.Peek());
        buffer.Append(readBuffer.AsSpan(0, read));
    }
}
```

`TryTakeEvent`는 앞쪽 공백·개행을 지운 뒤, 버퍼가 `[DUT->PC] BUTTON_`으로 시작하고 `#`까지 있으면 꺼낸다.

- **검증**: 단위 테스트 — 조각 수신, 두 메시지 병합, `BUTTON_*_EVT#` 건너뛰기, 앞 메시지 뒤 `\n`이 남은 상태에서 이벤트·다음 프레임 인식, `}}` 중첩 JSON, loopback 타임아웃·연결 종료 예외. loopback 모의 장치는 `new TcpListener(IPAddress.Loopback, 0)`로 임시 포트를 쓴다(5000 고정 금지).
- **선행 단위**: U2

### U4. 장치 세션 (핸드셰이크·단순 명령)

- **대상**: `DeviceConfig/DeviceSession.cs`, `DeviceResponse.cs`
- **코드 초안**:

```csharp
public async Task<string> HandshakeAsync(CancellationToken ct)
{
    await channel.SendLineAsync("[DUT<-PC] START", ct);
    await channel.SendLineAsync("hello tcp SUCCESS_CONNECT", ct);
    string response = await channel.ReceiveAsync(DeviceFrame.Test, timeout, ct);
    Match match = StartOkRegex().Match(response); // \[DUT->PC\] START_OK:(?<Mac>\S+)#
    if (!match.Success) throw new DeviceProtocolException($"Unexpected handshake response: {response}");
    return Mac = match.Groups["Mac"].Value;
}

public Task<DeviceResponse> ExecuteAsync(DeviceCommand command, CancellationToken ct) => command switch
{
    DeviceCommand.ApScan => SendJsonCommandAsync("apscan", ct),
    DeviceCommand.Reboot => SendJsonCommandAsync("reboot", ct, allowDisconnect: true),
    DeviceCommand.FactoryReset => SendJsonCommandAsync("factoryreset", ct, allowDisconnect: true),
    DeviceCommand.ScanAp => SendTestCommandAsync("SCAN_AP", ct),
    // ... OnCmd/OffCmd/Measurement/OverLoad ...
    DeviceCommand.TestFactoryReset => SendTestCommandAsync("FACTORY_RESET", ct, allowDisconnect: true),
    DeviceCommand.ConnectAp => throw new InvalidOperationException("Use ConnectApAsync"),
    _ => throw new ArgumentOutOfRangeException(nameof(command)),
};
```

`allowDisconnect`이면 `DeviceDisconnectedException`을 경고 로그로 바꾸고 빈 응답을 반환한다. 대상은 연결을 끝내는 명령 중 `Reboot`·`FactoryReset`·`TestFactoryReset`이다(`ConnectAp`는 U5에서 응답을 받는다).

- **검증**: loopback 모의 장치 테스트 — 핸드셰이크 송신 바이트(`[DUT<-PC] START\nhello tcp SUCCESS_CONNECT\n`)·MAC 파싱·실패 응답 예외, 명령별 송신 문자열, `Reboot`·`FactoryReset`·`TestFactoryReset` 후 연결 종료 허용
- **선행 단위**: U3

### U5. Wi‑Fi 선택과 ConnectAp

- **대상**: `DeviceConfig/Wifi/ScannedAp.cs`, `ApScanParser.cs`, `WifiProfileMatcher.cs`, `IWifiProfileSelector.cs`, `DeviceSession.ConnectApAsync`
- **코드 초안**:

```csharp
public sealed record ScannedAp(string Ssid, int Rssi);

public static IReadOnlyList<ScannedAp> Parse(string response)
{
    // {"SCAN_AP_LIST":{"a:-40","b:c:-70"}} — 엄격한 JSON이 아니므로 따옴표 문자열만 뽑는다
    int start = response.IndexOf("\"SCAN_AP_LIST\"", StringComparison.Ordinal);
    if (start < 0) throw new DeviceProtocolException($"apscan response: {response}");
    return EntryRegex().Matches(response[start..]).Skip(1) // 첫 매치는 키 이름
        .Select(m => m.Groups[1].Value)
        .Select(s => (s, i: s.LastIndexOf(':')))
        .Where(x => x.i > 0 && int.TryParse(x.s[(x.i + 1)..], out _))
        .Select(x => new ScannedAp(x.s[..x.i], int.Parse(x.s[(x.i + 1)..])))
        .ToList();
}

public static IReadOnlyList<WifiCandidate> Match(IEnumerable<ScannedAp> scanned, IEnumerable<WifiProfile> profiles)
{
    Dictionary<string, WifiProfile> bySsid = profiles.ToDictionary(p => p.Ssid, StringComparer.Ordinal);
    return scanned.Where(s => bySsid.ContainsKey(s.Ssid))
        .GroupBy(s => s.Ssid, StringComparer.Ordinal)
        .Select(g => new WifiCandidate(bySsid[g.Key], g.Max(s => s.Rssi)))
        .OrderByDescending(c => c.Rssi)
        .ToList();
}

public async Task<DeviceResponse> ConnectApAsync(DeviceSettings settings, IWifiProfileSelector selector, CancellationToken ct)
{
    DeviceResponse scan = await SendJsonCommandAsync("apscan", ct, apScanTimeout);
    IReadOnlyList<WifiCandidate> candidates = WifiProfileMatcher.Match(ApScanParser.Parse(scan.Raw), settings.WifiProfiles);
    if (candidates.Count == 0) throw new DeviceProtocolException("No registered Wi-Fi found in apscan result");
    WifiProfile chosen = await selector.SelectAsync(candidates, ct);
    string mac = string.IsNullOrEmpty(settings.Mac) ? Mac : settings.Mac; // YAML 빈 문자열 대응
    string json = DeviceMessageSerializer.Serialize(DeviceSettingsMessage.From(settings, chosen, mac));
    await channel.SendLineAsync(json, ct, logText: DeviceMessageSerializer.Mask(json));
    string settingsResponse = await channel.ReceiveAsync(DeviceFrame.Json, timeout, ct);
    await channel.SendLineAsync(DeviceMessageSerializer.Serialize(new DeviceCommandMessage("connectap", mac)), ct);
    return new DeviceResponse(await channel.ReceiveAsync(DeviceFrame.Json, timeout, ct));
}
```

`apscan` 대기 시간은 `Device.ApScanTimeoutSeconds`(기본 15, 설계 12절 미확인 대응)로 둔다.

- **검증**: 단위 테스트 — `:` 포함 SSID, 빈 목록, 형식 오류, 중복 SSID 최대 RSSI, 정렬, 후보 없음 예외; loopback으로 송신 순서(apscan → 설정 → connectap)·선택 프로필의 `ssid`/`pass` 확인
- **선행 단위**: U4

### U6. PC Wi‑Fi (`HostWifi`)와 장치 AP 탐색

- **대상**: `DeviceConfig/HostWifi/IHostWifi.cs`, `HostWifiNetwork.cs`, `ProcessRunner.cs`, `NmcliParser.cs`, `LinuxHostWifi.cs`, `SystemProfilerParser.cs`, `MacHostWifi.cs`, `Windows/WlanNative.cs`, `Windows/WindowsHostWifi.cs`, `HostWifiFactory.cs`, `DeviceApLocator.cs`, `IDeviceApSelector.cs`
- **코드 초안**:

```csharp
public interface IHostWifi
{
    Task<IReadOnlyList<HostWifiNetwork>> ScanAsync(CancellationToken ct);
    Task<HostWifiConnection?> GetCurrentAsync(CancellationToken ct);       // 인터페이스·SSID·프로필 이름
    Task<HostWifiConnection> ConnectOpenAsync(string ssid, CancellationToken ct);
    Task RestoreAsync(HostWifiConnection previous, CancellationToken ct);
    Task RemoveTemporaryAsync(HostWifiConnection temporary, CancellationToken ct);
}

public static IHostWifi? Create(ILoggerFactory loggerFactory)
{
    if (OperatingSystem.IsWindows()) return new WindowsHostWifi(loggerFactory.CreateLogger<WindowsHostWifi>());
    if (OperatingSystem.IsLinux()) return new LinuxHostWifi(new ProcessRunner(), loggerFactory.CreateLogger<LinuxHostWifi>());
    if (OperatingSystem.IsMacOS()) return new MacHostWifi(new ProcessRunner(), loggerFactory.CreateLogger<MacHostWifi>());
    return null;
}

// nmcli -t: ':' 구분, 값 안의 ':'는 '\:'로 이스케이프
public static IReadOnlyList<string> SplitTerse(string line)
{
    List<string> fields = new();
    StringBuilder current = new();
    for (int i = 0; i < line.Length; i++)
    {
        if (line[i] == '\\' && i + 1 < line.Length) { current.Append(line[++i]); continue; }
        if (line[i] == ':') { fields.Add(current.ToString()); current.Clear(); continue; }
        current.Append(line[i]);
    }
    fields.Add(current.ToString());
    return fields;
}

// Windows: 개방형 임시 프로필
private static string OpenProfileXml(string name, string ssid) => $"""
    <?xml version="1.0"?>
    <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
      <name>{SecurityElement.Escape(name)}</name>
      <SSIDConfig><SSID><name>{SecurityElement.Escape(ssid)}</name></SSID></SSIDConfig>
      <connectionType>ESS</connectionType><connectionMode>manual</connectionMode>
      <MSM><security><authEncryption>
        <authentication>open</authentication><encryption>none</encryption><useOneX>false</useOneX>
      </authEncryption></security></MSM>
    </WLANProfile>
    """;

[LibraryImport("wlanapi.dll")]
internal static partial uint WlanOpenHandle(uint clientVersion, nint reserved, out uint negotiatedVersion, out nint clientHandle);
// ... WlanEnumInterfaces, WlanScan, WlanGetAvailableNetworkList, WlanQueryInterface(current_connection),
//     WlanSetProfile, WlanConnect, WlanDeleteProfile, WlanFreeMemory, WlanCloseHandle ...

// DeviceApLocator (설계 3.3절)
public async Task<DeviceEndpoint> LocateAsync(CancellationToken ct)
{
    if (hostWifi is null) return Fallback("host Wi-Fi not supported");
    IReadOnlyList<HostWifiNetwork> found;
    try { found = await hostWifi.ScanAsync(ct); }
    catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Wi-Fi scan failed"); return Fallback("scan failed"); }

    List<HostWifiNetwork> candidates = found.Where(n => n.Ssid.StartsWith(options.ApSsidPrefix, StringComparison.Ordinal))
        .OrderByDescending(n => n.Signal).ToList();
    if (candidates.Count == 0) return Fallback("no device AP");

    HostWifiConnection? current = await hostWifi.GetCurrentAsync(ct);
    if (current is not null && candidates.Any(c => c.Ssid == current.Ssid)) return await ResolveAsync(current, ct);

    HostWifiNetwork chosen = await selector.SelectAsync(candidates, ct);
    previous = current;
    temporary = await hostWifi.ConnectOpenAsync(chosen.Ssid, ct);
    return await ResolveAsync(temporary, ct); // IPv4 대기(ApConnectTimeoutSeconds) → 게이트웨이 또는 Device.Host
}

public async ValueTask DisposeAsync()
{
    if (hostWifi is null || temporary is null) return;
    try
    {
        await hostWifi.RemoveTemporaryAsync(temporary, CancellationToken.None);
        if (previous is not null) await hostWifi.RestoreAsync(previous, CancellationToken.None);
    }
    catch (Exception ex) { logger.LogWarning(ex, "Wi-Fi restore failed"); }
}
```

`ProcessRunner`는 `ProcessStartInfo.ArgumentList`로 인수를 넘기고(셸 미사용), 종료 코드 0이 아니면 stderr를 담아 예외를 던진다. 테스트용으로 `IProcessRunner` 인터페이스를 둔다.

- **검증**:
  - 단위 테스트 — nmcli 이스케이프·`IN-USE` 표시, `system_profiler` JSON(현재 연결·주변 네트워크), Linux/macOS 명령 인수(가짜 `IProcessRunner`), Windows 프로필 XML 이스케이프
  - `DeviceApLocator` — 가짜 `IHostWifi`로 미지원(null)·스캔 실패·후보 없음 → `Device.Host`, 이미 접속 시 접속 생략, 선택 후 접속, `DisposeAsync`가 임시 삭제·복구 호출, 복구 예외 삼킴
  - 수동(Windows) — 실제 무선 어댑터로 `WindowsHostWifi.ScanAsync`·`GetCurrentAsync` 결과 조회(테스트 카테고리 `Manual`). 환경 변수 `B540_MANUAL_WIFI=1`이 없으면 `Assert.Inconclusive`로 건너뛴다. 실행: `$env:B540_MANUAL_WIFI='1'; dotnet test tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests --filter "TestCategory=Manual"`. 위치 서비스 권한이 꺼져 접근 거부가 나면 그 결과(오류 코드)를 기록하고, `DeviceApLocator`가 `Device.Host`로 넘어가는지 확인한다.
- **선행 단위**: U1 (U2~U5와 독립)

### U7. App 골격 (템플릿·설정·검증)

- **대상**: `DeviceConfig.App/Program.cs`, `ExitCode.cs`, `AppConfiguration.cs`(+ 섹션 클래스), `device.sample.yaml`
- **코드 초안**:

```csharp
internal sealed class AppConfiguration : IValidatableConfiguration
{
    [Property(PropertyType.STRING, required: true)]
    public string ContentRootPath { get; set; } = null!;

    public OsKmsSection OsKms { get; set; } = null!;
    public DeviceSection Device { get; set; } = new();
    public List<DeviceCommand> Commands { get; set; } = [];
    public List<WifiProfileSection> WifiProfiles { get; set; } = [];
    public SettingsSection? Settings { get; set; }

    public void Validate()
    {
        if (OsKms is null) throw new ArgumentException("OsKms is required");
        ConfigurationValidator.Validate(OsKms);
        ConfigurationValidator.Validate(Device);
        if (Commands.Count == 0) throw new ArgumentException("Commands must have at least one item");
        int terminalIndex = Commands.FindIndex(IsTerminal); // ConnectAp, Reboot, FactoryReset, TestFactoryReset
        if (terminalIndex >= 0 && terminalIndex != Commands.Count - 1)
            throw new ArgumentException($"{Commands[terminalIndex]} must be the last command");
        if (!Commands.Contains(DeviceCommand.ConnectAp)) return;

        if (WifiProfiles.Count == 0) throw new ArgumentException("WifiProfiles is required for ConnectAp");
        foreach (WifiProfileSection profile in WifiProfiles) ConfigurationValidator.Validate(profile);
        string? duplicate = WifiProfiles.GroupBy(p => p.Ssid, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (duplicate is not null) throw new ArgumentException($"Duplicate WifiProfiles.Ssid: {duplicate}");
        if (WifiProfiles.Any(p => !Ascii.IsValid(p.Ssid))) throw new ArgumentException("WifiProfiles.Ssid must be ASCII");
        if (Settings is null) throw new ArgumentException("Settings is required for ConnectAp");
        ConfigurationValidator.Validate(Settings);
    }
}

internal sealed class DeviceSection
{
    [Property(PropertyType.STRING, required: false, DefaultValue = "DWD-")] public string ApSsidPrefix { get; set; } = "DWD-";
    [Property(PropertyType.INT, required: false, DefaultValue = 30)] public int ApConnectTimeoutSeconds { get; set; } = 30;
    [Property(PropertyType.STRING, required: false, DefaultValue = "192.168.4.1")] public string Host { get; set; } = "192.168.4.1";
    [Property(PropertyType.INT, required: false, DefaultValue = 5000)] public int Port { get; set; } = 5000;
    [Property(PropertyType.INT, required: false, DefaultValue = 5)] public int TimeoutSeconds { get; set; } = 5;
    [Property(PropertyType.INT, required: false, DefaultValue = 15)] public int ApScanTimeoutSeconds { get; set; } = 15;
}
```

`Program`은 템플릿 그대로 두고, 검증 호출만 `ConfigurationValidator.Validate(appConfiguration)` 다음에 `appConfiguration.Validate()`가 불리는지 확인한다(라이브러리가 `IValidatableConfiguration`을 자동 호출하지 않으면 직접 호출). `DefaultValue` 속성 문법은 구현 시 attribute 시그니처로 확정한다.

- **검증**: `dotnet build` 경고 0. 빌드된 exe로 템플릿 검증 표 — 인자 없음 → 1, `--help`/`--version` → 0, 없는 설정 파일·빈 YAML·`Commands` 비움·`[ConnectAp, OnCmd]`(끝내는 명령이 마지막 아님)·`ConnectAp`인데 `WifiProfiles` 없음·SSID 중복 → 각 `Configuration error`·2
- **선행 단위**: U2

### U8. KMS와 `mqtt_key` 자동 생성

- **대상**: `DeviceConfig.App/Security/ConfigSecretProtector.cs`, `Program.RunAsync`(설정 검증 뒤 호출)
- **코드 초안**:

```csharp
// KEK manager와 DEK 서비스를 같은 수명으로 묶는다(KEK를 먼저 정리하지 않음)
internal sealed class ConfigSecretProtector : IDisposable
{
    private readonly AesGcmKekManager manager;
    private readonly DekCipherService dek;
    public bool Rewrite { get; private set; }

    public static ConfigSecretProtector Create(AppConfiguration config)
    {
        OsKmsSection kms = config.OsKms;
        IOsKekCredentialStore store = OsKekCredentialProvider.CreateForCurrentOs(
            kms.CredentialService, kms.CredentialAccount, OsKekCredentialDefaults.Namespace);
        bool rewrite = false;
        FileInfo dekFile = new(Path.Combine(config.ContentRootPath, kms.SecretsDekFilePath));
        if (string.IsNullOrEmpty(kms.Salt))
        {
            // 최초 기동은 Salt와 DEK 파일이 모두 없을 때만. Salt만 지운 경우 기존 KEK를 덮어쓰지 않는다
            if (dekFile.Exists)
                throw new InvalidOperationException($"OsKms.Salt is empty but DEK file exists: {dekFile.FullName}");
            store.StorePassword(RandomNumberGenerator.GetBytes(32));
            kms.Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            rewrite = true;
        }
        AesGcmKekManager manager = AesGcmKekManager.Create(store, Convert.FromBase64String(kms.Salt), kms.Iterations);
        return new ConfigSecretProtector(manager, new DekCipherService(AesGcmDekCipher.Create(manager.Current, dekFile)), rewrite);
    }

    public DecryptedSecrets Protect(AppConfiguration config, Action<string> log)
    {
        SettingsSection? settings = config.Settings;
        if (settings is not null && config.Commands.Contains(DeviceCommand.ConnectAp) && string.IsNullOrEmpty(settings.MqttKey))
        {
            settings.MqttKey = RandomNumberGenerator.GetString(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", 16);
            log("mqtt_key generated");   // 값은 남기지 않는다
        }

        string? mqttKey = null;
        if (settings?.MqttKey is not null)
        {
            settings.MqttKey = EnsureEncrypted(settings.MqttKey);
            mqttKey = dek.Decrypt(settings.MqttKey);
        }

        Dictionary<string, string> passBySsid = new(StringComparer.Ordinal);
        foreach (WifiProfileSection profile in config.WifiProfiles)   // 람다 대신 foreach (ref 캡처 불가)
        {
            if (string.IsNullOrEmpty(profile.Pass)) continue;          // ConnectAp가 없으면 검증 대상이 아님
            profile.Pass = EnsureEncrypted(profile.Pass);
            passBySsid[profile.Ssid] = dek.Decrypt(profile.Pass);
        }
        return new DecryptedSecrets(mqttKey, passBySsid);
    }

    private string EnsureEncrypted(string value)
    {
        if (dek.StartsWithFromPrefix(value)) return value;
        Rewrite = true;
        return dek.Encrypt(value);
    }

    public void Dispose() { dek.Dispose(); manager.Dispose(); }
}

// rewrite == true 이면 YamlDotNet Serializer로 설정 파일 덮어쓰기(원래 키 이름 PascalCase 유지)
```

- 복호화한 값은 YAML 모델에 되돌려 넣지 않고 `DecryptedSecrets`로만 서비스에 넘긴다.
- 실패(금고 접근 불가·DEK 불일치 등)는 `Configuration error`·종료 코드 2.
- 위험: YAML 재직렬화 시 주석이 사라진다. 샘플 파일에 이 점을 적는다.

- **검증**: 임시 디렉터리에서 빌드된 exe 실행 —
  1. 평문 `Pass`·빈 `MqttKey`·빈 `Salt` → 실행 후 YAML의 `Pass`·`MqttKey`가 `DEKENC:`, `Salt` 기록, DEK 파일 생성
  2. 같은 YAML 재실행 → `Pass`·`MqttKey`·`Salt` 문자열 변화 없음(파일 해시 비교)
  3. 로그 파일에 평문 비밀번호·키가 없음(`Select-String`)
  3a. `Commands: [OnCmd]`이고 `Pass`가 빈 프로필이 있을 때 예외 없이 진행
  3b. 1번 실행 뒤 YAML의 `Salt`만 비우고 재실행 → `Configuration error`·종료 코드 2, KEK 금고 항목·DEK 파일 변화 없음
  - 장치 접속 단계는 U9 전이라 연결 실패로 끝나도 된다.
  4. 정리(완료 조건) — 테스트 YAML의 `CredentialService`는 테스트 전용 이름(`Biz.Bizadm.PowerManager.DeviceConfig.Test`)으로 둔다. 검증 후 같은 이름으로 `IOsKekCredentialStore.RemovePassword()`를 호출해 `true`를 확인하고, Windows 자격 증명 관리자(`cmdkey /list`)에 해당 항목이 없음을 증거로 남긴다.
- **선행 단위**: U7

### U9. 실행 서비스·콘솔 선택·로그

- **대상**: `DeviceConfig.App/DeviceConfigService.cs`, `ConsoleWifiProfileSelector.cs`, `ConsoleDeviceApSelector.cs`, `Program.CreateHostApplication`(서비스 등록)
- **코드 초안**:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    await using DeviceApLocator locator = new(HostWifiFactory.Create(loggerFactory), apSelector, options, logger);
    DeviceEndpoint endpoint = await locator.LocateAsync(stoppingToken);
    logger.LogInformation("Device endpoint {Host}:{Port}", endpoint.Host, options.Port);

    await using TcpDeviceChannel channel = await TcpDeviceChannel.ConnectAsync(endpoint.Host, options.Port, logger, stoppingToken);
    DeviceSession session = new(channel, timeouts, logger);
    string mac = await session.HandshakeAsync(stoppingToken);
    logger.LogInformation("Device MAC {Mac}", mac);

    foreach (DeviceCommand command in commands)
    {
        DeviceResponse response = command == DeviceCommand.ConnectAp
            ? await session.ConnectApAsync(deviceSettings, wifiSelector, stoppingToken)
            : await session.ExecuteAsync(command, stoppingToken);
        logger.LogInformation("{Command} response: {Response}", command, response.Raw);
    }
    lifetime.StopApplication();
}

// ConsoleWifiProfileSelector
public Task<WifiProfile> SelectAsync(IReadOnlyList<WifiCandidate> candidates, CancellationToken ct)
{
    for (int i = 0; i < candidates.Count; i++)
        Console.WriteLine($"  [{i + 1}] {candidates[i].Profile.Ssid} ({candidates[i].Rssi} dBm)");
    Console.Write("Wi-Fi 번호: ");
    string? line = Console.ReadLine();
    if (!int.TryParse(line, out int n) || n < 1 || n > candidates.Count)
        throw new InvalidOperationException($"Invalid selection: {line}");
    return Task.FromResult(candidates[n - 1].Profile);
}
```

예외는 템플릿대로 `ExecuteAsync` 밖으로 올린다(호스트가 `BackgroundService failed` 기록, 종료 코드 0). Serilog 로그 파일명은 `Biz.Bizadm.PowerManager.DeviceConfig.App.log`.

- **검증**: 빌드된 exe + 로컬 모의 장치(스크래치패드의 PowerShell `TcpListener` 스크립트, `127.0.0.1:5000`, `ApSsidPrefix`를 존재하지 않는 값으로 두어 `Device.Host` 사용) —
  1. `Commands: [ScanAp, OnCmd]` → 핸드셰이크·두 명령 송수신 로그, 자연 종료 코드 0
  2. `Commands: [ConnectAp]` + 표준 입력 `1` 리다이렉트 → 모의 장치가 받은 순서(apscan → 설정 JSON → connectap)와 선택 SSID 확인, 로그에 `pass`·`mqtt_key`가 `***`
  3. 모의 장치 미기동 → 연결 실패가 `BackgroundService failed`로 로그에 남고 종료 코드 0(템플릿 동작)
  4. 기본 테스트 전체 통과: `dotnet test --filter "TestCategory!=Manual&TestCategory!=Device"`
- **선행 단위**: U5, U6, U8

### U10. 실장비 테스트 (설계 9.1절)

- **대상**: 실장비 B540(펌웨어 1.01.3x), 빌드된 `Biz.Bizadm.PowerManager.DeviceConfig.App.exe`, `tests/.../Device/`(MSTest 카테고리 `Device`, 기본 실행 제외), `docs/cli/` 결과 기록
- **코드 초안**:

```csharp
// 실장비 전용 테스트: 환경 변수가 없으면 Inconclusive로 건너뛴다
[TestClass, TestCategory("Device")]
public sealed class RealDeviceTests
{
    private static string Host => Environment.GetEnvironmentVariable("B540_DEVICE_HOST")
        ?? throw new AssertInconclusiveException("B540_DEVICE_HOST not set");

    [TestMethod]
    public async Task Handshake_ReturnsMac()
    {
        await using TcpDeviceChannel channel = await TcpDeviceChannel.ConnectAsync(Host, 5000, NullLogger.Instance, default);
        DeviceSession session = new(channel, DeviceTimeouts.Default, NullLogger.Instance);
        string mac = await session.HandshakeAsync(default);
        Assert.MatchesRegex("^[0-9a-fA-F]{12}$", mac);
    }

    [TestMethod]
    public async Task ApScan_ParsesAtLeastOneAp() { /* ApScanParser.Parse 결과 1개 이상, 원문은 TestContext로 기록 */ }
}
```

```powershell
# 수동 실행 (PC가 장치 AP에 접속 가능한 상태)
$env:B540_DEVICE_HOST = '192.168.4.1'
dotnet test tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests --filter "TestCategory=Device"
```

- **절차·검증**: 설계 9.1절 순서(①~⑧)로 진행하고 단계마다 아래를 기록한다. 상태를 바꾸는 ④ 이후는 단계마다 사용자 확인 후 실행한다.

| 단계 | 실행 | 확인·기록 |
|---|---|---|
| ① | exe `Commands: [ScanAp]`, 기본 `ApSsidPrefix` | PC 장치 AP 스캔 결과·선택·접속·게이트웨이, 종료 후 원래 네트워크 복구 |
| ② | `TestCategory=Device` `Handshake_ReturnsMac` | `START_OK` 원문, MAC 형식 |
| ③ | `Measurement`, `ScanAp` | 응답 원문·`#` 프레이밍 |
| ④ | `OnCmd`, `OffCmd` | 릴레이 동작, 응답 원문 |
| ⑤ | `ApScan` | `SCAN_AP_LIST` 실제 형식, 소요 시간 → `ApScanTimeoutSeconds` 조정 여부 |
| ⑥ | `ConnectAp`(테스트 공유기, 서버 #1 또는 접속 확인용 주소) | 설정 응답·`connectap` 응답 원문, 장치의 공유기 DHCP 임대, (서버 준비 시) REST·MQTT 접속과 `mqtt_key` 사용 여부 |
| ⑦ | `Reboot` | 응답 또는 연결 종료 처리 |
| ⑧ | `FactoryReset` / `TestFactoryReset` | 장치 AP 재등장, 설정 초기화 여부 |

- 결과는 `docs/cli/`에 새 순번 문서(`XXXX-device-config-실장비.md`)로 남기고, 설계 12절 미확인 항목을 확정·정정한다. 추정과 실제가 다르면 해당 단계(설계·개발)로 회귀한다.
- 장치·공유기 식별자(MAC, SSID, 비밀번호, `mqtt_key`)는 기록 시 마스킹한다.
- **선행 단위**: U9, 실장비 확보

## 완료 판정

- `dotnet build` 경고 0, `dotnet test --filter "TestCategory!=Manual&TestCategory!=Device"` 통과, 취약·Deprecated 없음
- `Manual`·`Device` 테스트는 환경 변수(`B540_MANUAL_WIFI`, `B540_DEVICE_HOST`)가 없으면 `Inconclusive`로 건너뛰므로 필터 없이 실행해도 실패하지 않는다
- U7~U9 exe 시나리오의 실제 종료 코드·로그 증거, U8 금고 항목 정리 증거
- U10 실장비 결과 기록. 실장비가 없으면 U1~U9 완료와 실장비 미검증을 구분해 보고하고 이슈 #2는 열어 둔다
- Linux·macOS 동작은 미검증으로 보고한다(설계 12절)
