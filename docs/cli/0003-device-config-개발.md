---
status: done
category: cli
feature: device-config
phase: 개발
---

# device-config 개발 계획

선행: [0001-device-config-방안](0001-device-config-방안.md), [0002-device-config-설계](0002-device-config-설계.md) · GitHub: SeongSikChae/Biz.Bizadm.PowerManager#6

표기: 프로젝트 이름 앞의 `Biz.Bizadm.PowerManager.`는 생략한다. 단위마다 구현 → 검증 → 커밋 확인을 반복한다.

| 단위 | 내용 | 선행 |
|---|---|---|
| U1 | 기존 프로젝트 삭제 + 새 솔루션 골격 | 없음 |
| U2 | 프로토콜(메시지·응답 파서·설정 JSON·마스킹·`mqtt_key`) | U1 |
| U3 | 전송(TCP·핸드셰이크·수신 프레임·명령 10개) | U2 |
| U4 | HostWifi API·로더·linux/osx 스텁 | U1 |
| U5 | `DeviceConfigRunner`·Wi‑Fi 대조 | U3, U4 |
| U6 | `HostWifi.Runtime.win` WLAN | U4 |
| U7 | App(설정·OsKms·콘솔 선택·종료 코드) | U5, U6 |
| U8 | 실장비 전체 명령 + `Reboot`·`TestFactoryReset` 처리 확정 | U7 |

## U1. 기존 프로젝트 삭제 + 새 솔루션 골격

**대상**: `src/DeviceConfig`, `src/DeviceConfig.App`, `tests/DeviceConfig.Tests` 삭제 후 재생성, `src/DeviceConfig.HostWifi.Runtime.{win,linux,osx}` 생성, `Biz.Bizadm.PowerManager.slnx`, `Directory.Packages.props`(기존 버전 유지).

**코드 초안**

`HostWifi.Runtime.win.csproj` (linux·osx도 `AssemblyName`·`RootNamespace`·참조가 같다. `AllowUnsafeBlocks`는 win만):

```xml
<PropertyGroup>
  <AssemblyName>Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.Runtime</AssemblyName>
  <RootNamespace>Biz.Bizadm.PowerManager.DeviceConfig.HostWifi</RootNamespace>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks> <!-- win만: LibraryImport -->
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="..\Biz.Bizadm.PowerManager.DeviceConfig\Biz.Bizadm.PowerManager.DeviceConfig.csproj" />
</ItemGroup>
```

`DeviceConfig.App.csproj` RID 조건 참조:

```xml
<PropertyGroup>
  <HostWifiOs Condition="$(RuntimeIdentifier.StartsWith('win'))">win</HostWifiOs>
  <HostWifiOs Condition="$(RuntimeIdentifier.StartsWith('linux'))">linux</HostWifiOs>
  <HostWifiOs Condition="$(RuntimeIdentifier.StartsWith('osx'))">osx</HostWifiOs>
  <HostWifiOs Condition="'$(HostWifiOs)' == '' And $([MSBuild]::IsOSPlatform('Windows'))">win</HostWifiOs>
  <HostWifiOs Condition="'$(HostWifiOs)' == '' And $([MSBuild]::IsOSPlatform('Linux'))">linux</HostWifiOs>
  <HostWifiOs Condition="'$(HostWifiOs)' == '' And $([MSBuild]::IsOSPlatform('OSX'))">osx</HostWifiOs>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="..\Biz.Bizadm.PowerManager.DeviceConfig\Biz.Bizadm.PowerManager.DeviceConfig.csproj" />
  <ProjectReference Include="..\Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.Runtime.$(HostWifiOs)\Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.Runtime.$(HostWifiOs).csproj" />
</ItemGroup>
```

`DeviceConfig.csproj`에 `<InternalsVisibleTo Include="Biz.Bizadm.PowerManager.DeviceConfig.Tests" />`를 둔다(`DeviceFrameReader` 등 internal 테스트용). 테스트 프로젝트는 Windows에서만 `Runtime.win`을 참조한다(`Condition="$([MSBuild]::IsOSPlatform('Windows'))"`). 실장비 테스트는 `[TestCategory("Device")]`이고 기본 `dotnet test`에서 실행되면 안 된다(장치 초기화·PC Wi‑Fi 변경 위험). U1에서 확인해 정한 방식: 테스트 csproj에 `<TestingPlatformCommandLineArguments>--filter "TestCategory!=Device"</TestingPlatformCommandLineArguments>`를 고정한다. Device 테스트 실행은 `dotnet test -p:TestingPlatformCommandLineArguments= --filter "TestCategory=Device"`. 실측: `dotnet test -- --filter …`는 `--filter` 중복으로 종료 코드 5, 테스트 exe 직접 실행에는 필터가 적용되지 않는다(exe 직접 실행은 막지 않기로 함).

**검증**: `dotnet build Biz.Bizadm.PowerManager.slnx` 경고 0. `dotnet publish src/…App -r linux-x64`·`-r osx-arm64`가 각 스텁을 참조해 빌드되는지 확인(출력 폴더의 `…HostWifi.Runtime.dll` 1개).

## U2. 프로토콜

**대상**: `DeviceConfig/Protocol/DeviceMessages.cs`, `DeviceResponseParser.cs`, `DeviceSettings.cs`, `MqttKeyGenerator.cs`, `DeviceProtocolException.cs`, `DeviceConfig/HostWifi/WifiModels.cs`(`WifiAdapter`·`WifiNetwork` record — U2·U3가 쓰므로 여기서 만든다).

**코드 초안**

```csharp
public static class DeviceMessages
{
    public const string Start = "[DUT<-PC] START\n";
    public const string Hello = "hello tcp SUCCESS_CONNECT\n";
    public static string Test(string name) => $"[DUT<-PC] {name}\n";        // SCAN_AP, ON_CMD ...
    public static string Command(string name) => $"{{\"command\":\"{name}\"}}\n"; // apscan, reboot, factoryreset
    public static string ConnectAp(string mac) => $"{{\"mac\":\"{mac}\",\"command\":\"connectap\"}}\n";
}

public static class DeviceResponseParser
{
    // "[DUT->PC] START_OK:<mac>#"
    public static string ParseStartOk(string frame) { /* 접두사·# 확인 후 MAC 12hex 반환, 아니면 DeviceProtocolException */ }

    // {"mac":"…","respone":"OK"} — 펌웨어 오타 키
    // OK면 frame을 그대로 돌려준다(switch 식 분기에서 사용).
    public static string EnsureOk(string frame)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(frame);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("respone", out JsonElement value)
                && value.ValueKind == JsonValueKind.String && value.GetString() == "OK")
                return frame;
        }
        catch (JsonException) { } // '#' 프레임 등 JSON 아님
        throw new DeviceProtocolException($"Device response is not OK: {frame}");
    }

    // {"SCAN_AP_LIST":{"ssid:-51","ssid2:-70"}} — 정식 JSON 아님
    public static IReadOnlyList<WifiNetwork> ParseScanApList(string frame)
    {
        int key = frame.IndexOf("SCAN_AP_LIST", StringComparison.Ordinal);
        if (key < 0)
            throw new DeviceProtocolException($"SCAN_AP_LIST not found: {frame}");
        int open = frame.IndexOf('{', key);
        if (open < 0 || !frame.EndsWith("}}", StringComparison.Ordinal))
            throw new DeviceProtocolException($"Invalid SCAN_AP_LIST frame: {frame}");
        string body = frame[(open + 1)..^2];
        return Regex.Matches(body, "\"(?<ssid>[^\"]*):(?<rssi>-?\\d+)\"")
            .Select(m => new WifiNetwork(m.Groups["ssid"].Value, int.Parse(m.Groups["rssi"].Value)))
            .ToList();
    }
}

public sealed record DeviceSettings(/* ApiServerAddr, ApiServerPort, ServerAddr, ServerPort, SslSupport,
                                       UserId, Company, Model, Lati, Long, Topic */)
{
    public string ToJson(string mac, string ssid, string pass, string mqttKey, bool masked = false)
    {
        Dictionary<string, string> values = new()
        {
            ["mac"] = mac, ["api_server_addr"] = ApiServerAddr, ["api_server_port"] = ApiServerPort.ToString(CultureInfo.InvariantCulture),
            ["server_addr"] = ServerAddr, ["server_port"] = ServerPort.ToString(CultureInfo.InvariantCulture),
            ["ssl_support"] = SslSupport ? "yes" : "no", ["ssid"] = ssid, ["pass"] = masked ? "***" : pass,
            ["user_id"] = UserId, ["company"] = Company, ["model"] = Model, ["lati"] = Lati, ["long"] = Long,
            ["topic"] = Topic, ["mqtt_key"] = masked ? "***" : mqttKey,
        };
        return JsonSerializer.Serialize(values, JsonOptions) + "\n";
    }

    // 장치 파서가 \uXXXX를 처리한다는 근거가 없으므로 + & < > ' 와 비ASCII를 그대로 보낸다(#2와 같음).
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}

public static class MqttKeyGenerator
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789/";
    public static string Create() => RandomNumberGenerator.GetString(Alphabet, 16);
}
```

**검증**: `DeviceConfig.Tests` 단위 테스트 — `START_OK` 파싱(정상·접두사 없음), `EnsureOk`(OK·FAIL·`respone` 키 없음·`#` 프레임 → `DeviceProtocolException`), 실장비 형태 `SCAN_AP_LIST` 파싱·빈 목록 `{"SCAN_AP_LIST":{}}`·키 없음, 설정 JSON 키·문자열 값·`yes/no`·마스킹, `+&` 포함 비밀번호가 이스케이프 없이 그대로 나옴, `mqtt_key` 16자·문자 집합.

## U3. 전송

**대상**: `DeviceConfig/Transport/DeviceFrameReader.cs`, `DeviceConnection.cs`(`OpenAsync`·`HandshakeAsync`·`RequestAsync`), `DeviceClient.cs`, `DeviceCommand.cs`.

**코드 초안**

```csharp
public enum DeviceCommand { ConnectAp, ApScan, Reboot, FactoryReset, ScanAp, OnCmd, OffCmd, Measurement, OverLoad, TestFactoryReset }

// 수신 버퍼에서 '#' 프레임 또는 JSON(중괄호 짝) 하나를 꺼낸다. "[DUT->PC] BUTTON_…#"는 버린다.
internal sealed class DeviceFrameReader
{
    // 송수신 모두 UTF-8. Decoder 인스턴스로 읽기 경계에서 잘린 멀티바이트를 이어 붙인다.
    // 한글 등 비ASCII SSID는 지원하지 않는다(장치 인코딩 미확인, 설계 7절).
    private readonly Decoder decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder buffer = new();
    public void Append(ReadOnlySpan<byte> data)
    {
        Span<char> chars = stackalloc char[Encoding.UTF8.GetMaxCharCount(data.Length)];
        buffer.Append(chars[..decoder.GetChars(data, chars, flush: false)]);
    }
    public bool TryTake(out string frame) { /* 앞 공백 제거 → '{'로 시작하면 짝 '}'까지, 아니면 '#'까지. BUTTON_ 프레임이면 버리고 계속 */ }
}

public sealed class DeviceClient(string host, int port, TimeSpan timeout, ILogger logger) 
{
    // 명령마다 새 연결: 연결 → 핸드셰이크 → body → 연결 종료
    public async Task<string> ExecuteAsync(DeviceCommand command, CancellationToken cancellationToken)
    {
        await using DeviceConnection connection = await DeviceConnection.OpenAsync(host, port, timeout, logger, cancellationToken);
        string mac = await connection.HandshakeAsync(cancellationToken);
        return command switch
        {
            // 응답은 예상 형식을 확인한다(다르면 DeviceProtocolException). OverLoad는 '#' 프레임만 확인.
            DeviceCommand.ApScan => EnsureScanList(await connection.RequestAsync(DeviceMessages.Command("apscan"), cancellationToken)),
            DeviceCommand.FactoryReset => DeviceResponseParser.EnsureOk(await connection.RequestAsync(DeviceMessages.Command("factoryreset"), cancellationToken)),
            DeviceCommand.Reboot => await connection.RequestAsync(DeviceMessages.Command("reboot"), cancellationToken), // U8에서 확정
            DeviceCommand.ScanAp => EnsureScanList(await connection.RequestAsync(DeviceMessages.Test("SCAN_AP"), cancellationToken)),
            DeviceCommand.OnCmd => Expect(await connection.RequestAsync(DeviceMessages.Test("ON_CMD"), cancellationToken), @"^\[DUT->PC\] ON_CMD_OK#$"),
            DeviceCommand.OffCmd => Expect(await connection.RequestAsync(DeviceMessages.Test("OFF_CMD"), cancellationToken), @"^\[DUT->PC\] OFF_CMD_OK#$"),
            DeviceCommand.Measurement => Expect(await connection.RequestAsync(DeviceMessages.Test("MEASUREMENT"), cancellationToken), @"^\[DUT->PC\] MEASUREMENT_W:\d+(\.\d+)?#$"),
            DeviceCommand.OverLoad => Expect(await connection.RequestAsync(DeviceMessages.Test("OVERLOAD"), cancellationToken), "#$"),
            DeviceCommand.TestFactoryReset => await connection.RequestAsync(DeviceMessages.Test("FACTORY_RESET"), cancellationToken), // 응답 처리는 U8에서 확정
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
    }

    private static string EnsureScanList(string frame) { DeviceResponseParser.ParseScanApList(frame); return frame; }

    private static string Expect(string frame, string pattern) =>
        Regex.IsMatch(frame, pattern) ? frame : throw new DeviceProtocolException($"Unexpected response: {frame}");

    // ConnectAp는 한 연결에서 apscan → 선택 → 설정 JSON → connectap
    public async Task ConnectApAsync(Func<IReadOnlyList<WifiNetwork>, CancellationToken, Task<(string Ssid, string Pass)>> selectWifi,
        DeviceSettings settings, CancellationToken cancellationToken)
    {
        await using DeviceConnection connection = await DeviceConnection.OpenAsync(host, port, timeout, logger, cancellationToken);
        string mac = await connection.HandshakeAsync(cancellationToken);
        IReadOnlyList<WifiNetwork> scanned = DeviceResponseParser.ParseScanApList(
            await connection.RequestAsync(DeviceMessages.Command("apscan"), cancellationToken));
        (string ssid, string pass) = await selectWifi(scanned, cancellationToken);
        string mqttKey = MqttKeyGenerator.Create();
        logger.LogInformation("TX {Message}", settings.ToJson(mac, ssid, pass, mqttKey, masked: true).TrimEnd());
        DeviceResponseParser.EnsureOk(await connection.RequestAsync(settings.ToJson(mac, ssid, pass, mqttKey), cancellationToken, logSend: false));
        DeviceResponseParser.EnsureOk(await connection.RequestAsync(DeviceMessages.ConnectAp(mac), cancellationToken));
    }
}

// DeviceConnection.RequestAsync(string message, CancellationToken cancellationToken, bool logSend = true):
//   송신(logSend면 TX 로그. 설정 JSON은 false로 하고 마스킹한 TX 로그를 따로 남긴다) → timeout 안에서 프레임 1개 수신(RX 로그).
//   시간 초과는 TimeoutException, 응답 전 연결 종료는 IOException. 예외 메시지에는 송신 내용을 넣지 않는다(설정 JSON의 pass·mqtt_key 보호).
```

**검증**: `DeviceFrameReader.TryTake` 단위 테스트 — 조각 입력(한 프레임을 여러 번 Append), 연속 두 프레임, BUTTON 프레임 뒤 응답, `{`/`#` 판별, UTF‑8 멀티바이트가 두 Append에 걸친 경우. 로컬 `TcpListener` 모의 장치(`tests/…/TestSupport/MockDevice.cs`)로 — 핸드셰이크 MAC, 명령 송신 문자열(9개는 `ExecuteAsync`, `ConnectAp`은 `ConnectApAsync`), 명령마다 새 연결(모의 장치 접속 횟수), `ConnectAp` 한 연결 순서, `respone` FAIL·응답 없음(타임아웃)·연결 종료·예상과 다른 응답(예: `ON_CMD` 에 `OFF_CMD_OK#`)이 예외, BUTTON 프레임 건너뜀, 로그에 `pass`·`mqtt_key` 평문 없음, 설정 JSON 응답이 타임아웃·연결 종료일 때 예외 메시지와 로그에 `pass`·`mqtt_key` 평문 없음.

## U4. HostWifi API·로더·linux/osx 스텁

**대상**: `DeviceConfig/HostWifi/IHostWifi.cs`(설계 2절 인터페이스, record는 U2에서 만듦), `HostWifiLoader.cs`, `Runtime.linux/RuntimeHostWifiFactory.cs`·`LinuxHostWifi.cs`, `Runtime.osx/…`, `Runtime.win/RuntimeHostWifiFactory.cs`·`WindowsHostWifi.cs`(이 단위에서는 linux와 같은 `NotImplementedException` 스텁, U6에서 구현으로 교체).

**코드 초안**

```csharp
public static class HostWifiLoader
{
    private const string RuntimeAssemblyName = "Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.Runtime";
    private const string FactoryTypeName = "Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.RuntimeHostWifiFactory";

    public static IHostWifi CreateForCurrentOs(ILogger logger)
    {
        Type type = Type.GetType($"{FactoryTypeName}, {RuntimeAssemblyName}", throwOnError: false)
            ?? throw new InvalidOperationException($"Runtime implementation '{RuntimeAssemblyName}' is not available.");
        MethodInfo create = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, [typeof(ILogger)])
            ?? throw new InvalidOperationException($"Create method not found on '{FactoryTypeName}'.");
        return (IHostWifi)create.Invoke(null, [logger])!;
    }
}

// Runtime.linux (osx도 같은 형태)
public static class RuntimeHostWifiFactory
{
    public static IHostWifi Create(ILogger logger) => new LinuxHostWifi();
}
internal sealed class LinuxHostWifi : IHostWifi
{
    public Task<IReadOnlyList<WifiAdapter>> ListAdaptersAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    // ... 나머지 5개 메서드도 throw new NotImplementedException();
}
```

`DeviceConfig`는 `Microsoft.Extensions.Logging.Abstractions`만 참조한다.

**검증**: 테스트 프로젝트(Windows)에서 `HostWifiLoader.CreateForCurrentOs`가 `IHostWifi`를 돌려줌. `dotnet publish -r linux-x64` 출력에서 같은 이름 어셈블리가 linux 스텁임(U1 publish 결과의 타입 확인).

## U5. `DeviceConfigRunner`·Wi‑Fi 대조

**대상**: `DeviceConfig/DeviceConfigRunner.cs`, `DeviceConfigOptions.cs`, `IDeviceConfigSelector.cs`, `Wifi/WifiProfile.cs`, `WifiProfileMatcher.cs`.

**코드 초안**

```csharp
public sealed record WifiProfile(string Ssid, string Pass)
{
    public override string ToString() => Ssid; // Pass가 로그·예외·콘솔에 나가지 않게
}
public sealed record WifiProfileCandidate(WifiProfile Profile, int Signal);

// 장치 apscan에 같은 SSID가 여럿(메시·다중 AP)이면 묶지 않고 모두 후보로 둔다.
// 콘솔은 "[n] <SSID> (<RSSI> dBm)"로 보여 RSSI로 구분할 수 있게 한다(apscan은 SSID·RSSI만 준다).
public static class WifiProfileMatcher
{
    public static IReadOnlyList<WifiProfileCandidate> Match(IReadOnlyList<WifiProfile> profiles, IReadOnlyList<WifiNetwork> scanned) =>
        profiles.Join(scanned, p => p.Ssid, n => n.Ssid, (p, n) => new WifiProfileCandidate(p, n.Signal), StringComparer.Ordinal)
            .OrderByDescending(c => c.Signal).ToList();
}

public sealed class DeviceConfigRunner(IHostWifi hostWifi, IDeviceConfigSelector selector, DeviceConfigOptions options, ILogger<DeviceConfigRunner> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        WifiAdapter? connectedAdapter = null; // ConnectOpenAsync를 호출한 어댑터(복구 대상)
        bool failed = false;
        string? previousSsid = null;
        string host = options.Host;
        try
        {
            try
            {
                WifiAdapter adapter = await selector.SelectAdapterAsync(await hostWifi.ListAdaptersAsync(cancellationToken), cancellationToken);
                previousSsid = await hostWifi.GetCurrentSsidAsync(adapter, cancellationToken);
                IReadOnlyList<WifiNetwork> deviceAps = (await hostWifi.ScanAsync(adapter, cancellationToken))
                    .Where(n => n.Ssid.StartsWith(options.ApPrefix, StringComparison.Ordinal)).ToList();
                WifiNetwork deviceAp = await selector.SelectDeviceApAsync(deviceAps, cancellationToken);
                connectedAdapter = adapter; // 이 시점부터는 실패·취소에도 복구한다(설계 5절)
                await hostWifi.ConnectOpenAsync(adapter, deviceAp.Ssid, options.ApConnectTimeout, cancellationToken);
                host = (await hostWifi.GetGatewayAsync(adapter, cancellationToken))?.ToString()
                    ?? throw new InvalidOperationException("Device AP gateway not found"); // ConnectOpenAsync가 게이트웨이까지 기다리므로 정상 경로에서는 오지 않음
            }
            catch (NotImplementedException)
            {
                // 미구현 OS만 폴백한다. 그 밖의 PC Wi-Fi 실패는 원인을 보이도록 전파(설계 4절)
                logger.LogWarning("PC Wi-Fi is not implemented on this OS; connecting to configured host {Host}", options.Host);
                host = options.Host;
            }

            DeviceClient client = new(host, options.Port, options.Timeout, logger);
            foreach (DeviceCommand command in options.Commands)
            {
                if (command == DeviceCommand.ConnectAp)
                    await client.ConnectApAsync(SelectWifiAsync, options.Settings!, cancellationToken);
                else
                    logger.LogInformation("{Command} response: {Response}", command, await client.ExecuteAsync(command, cancellationToken));
            }
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            if (connectedAdapter is not null)
            {
                try
                {
                    await hostWifi.RestoreAsync(connectedAdapter, previousSsid, CancellationToken.None);
                }
                catch (Exception ex) when (failed)
                {
                    // 원래 실패 원인을 덮지 않도록 복구 실패는 로그만 남긴다. 원래 예외가 없으면 복구 예외를 그대로 던진다.
                    logger.LogError(ex, "PC Wi-Fi restore failed");
                }
            }
        }
    }

    private async Task<(string, string)> SelectWifiAsync(IReadOnlyList<WifiNetwork> scanned, CancellationToken cancellationToken)
    {
        IReadOnlyList<WifiProfileCandidate> candidates = WifiProfileMatcher.Match(options.WifiProfiles, scanned);
        if (candidates.Count == 0)
            throw new InvalidOperationException("No registered Wi-Fi found in device apscan result (if the router shares the device SoftAP channel, change the router channel and retry)");
        WifiProfile profile = await selector.SelectWifiProfileAsync(candidates, cancellationToken);
        return (profile.Ssid, profile.Pass);
    }
}
```

`DeviceConfigOptions`는 App이 설정에서 만들어 넘기는 값 객체(Host, Port, Timeout, ApPrefix, ApConnectTimeout, Commands, WifiProfiles(복호화된 Pass), Settings)다.

**검증**: 모의 `IHostWifi`·`IDeviceConfigSelector` + U3 모의 장치로 — 정상 흐름에서 게이트웨이 주소 사용·복구 호출(이전 SSID), 게이트웨이 null이면 예외·복구 호출, `ListAdaptersAsync`가 `NotImplementedException`이면 경고 후 `Host`로 진행·복구 미호출, `ScanAsync` 등 그 밖의 예외는 폴백 없이 전파, `ConnectOpenAsync`·`GetGatewayAsync` 실패와 취소 시 예외 전파 + 복구 호출, 명령 실패 + 복구 실패면 원래 예외가 전파되고 복구 예외는 로그, 명령 성공 + 복구 실패면 복구 예외 전파, 명령 실패 시 남은 명령 미실행·예외 전파·복구 호출, Wi‑Fi 대조(완전 일치·대소문자·RSSI 순·후보 없음이면 설정 미전송·같은 SSID 중복이면 RSSI별로 모두 후보), `WifiProfile.ToString()`에 Pass 없음.

## U6. `HostWifi.Runtime.win`

**대상**: `Runtime.win/WindowsHostWifi.cs`(U4 스텁을 구현으로 교체), `WlanNative.cs`(LibraryImport, #2 코드 참고해 새로 작성), `tests/DeviceConfig.Tests/Device/WindowsHostWifiTests.cs`(`Device` 카테고리. `HostWifiLoader.CreateForCurrentOs`로 `IHostWifi`를 얻어 쓰고 internal 형식은 쓰지 않는다. 임시 프로필 삭제는 `netsh wlan show profiles` 출력에 `B540-Config-Temp`가 없는지로 확인).

**코드 초안**

```csharp
// AssemblyInfo.cs — 팩터리·구현 전체를 Windows 전용으로 표시해 CA1416 경고를 없앤다(로더는 리플렉션이라 경고 없음).
[assembly: SupportedOSPlatform("windows")]

internal sealed class WindowsHostWifi(ILogger logger) : IHostWifi
{
    internal const string TemporaryProfileName = "B540-Config-Temp";

    public Task<IReadOnlyList<WifiAdapter>> ListAdaptersAsync(CancellationToken cancellationToken)
    {
        using WlanClient client = WlanClient.Open();
        return Task.FromResult<IReadOnlyList<WifiAdapter>>(client.GetInterfaces()
            .Select(i => new WifiAdapter(i.Guid.ToString("B").ToUpperInvariant(), i.Description)).ToList());
    }

    public async Task ConnectOpenAsync(WifiAdapter adapter, string ssid, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Guid id = Guid.Parse(adapter.Id);
        using (WlanClient client = WlanClient.Open())
        {
            client.SetProfile(id, OpenProfileXml(TemporaryProfileName, ssid));
            client.Connect(id, TemporaryProfileName);
        }
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            // SSID가 같고 IPv4 게이트웨이가 생길 때까지 기다린다(APIPA 169.254.x.x에서 끝나지 않도록 게이트웨이 기준)
            while (!(await GetCurrentSsidAsync(adapter, cts.Token) == ssid && await GetGatewayAsync(adapter, cts.Token) is not null))
                await Task.Delay(500, cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Device AP connection timed out after {timeout.TotalSeconds}s: {ssid}");
        }
    }

    public Task<IPAddress?> GetGatewayAsync(WifiAdapter adapter, CancellationToken cancellationToken) =>
        Task.FromResult(FindInterface(adapter.Id)?.GetIPProperties().GatewayAddresses
            .Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork));

    public Task RestoreAsync(WifiAdapter adapter, string? previousSsid, CancellationToken cancellationToken)
    {
        Guid id = Guid.Parse(adapter.Id);
        using WlanClient client = WlanClient.Open();
        string? profile = previousSsid is null ? null
            : client.GetAvailableNetworks(id).FirstOrDefault(n => n.Ssid == previousSsid && n.ProfileName.Length > 0)?.ProfileName;
        if (previousSsid is not null && profile is null)
            logger.LogWarning("Profile for previous Wi-Fi {Ssid} not found; disconnecting", previousSsid);
        bool failed = false;
        try
        {
            if (profile is null) client.Disconnect(id); else client.Connect(id, profile);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            try
            {
                client.DeleteProfile(id, TemporaryProfileName); // 재연결이 실패해도 임시 프로필은 지운다
            }
            catch (Exception ex) when (failed)
            {
                // 재연결 예외를 가리지 않도록 삭제 실패는 로그만 남긴다
                logger.LogError(ex, "Failed to delete temporary profile {Profile}", TemporaryProfileName);
            }
        }
        return Task.CompletedTask;
    }

    private static NetworkInterface? FindInterface(string id) =>
        NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));
    // ScanAsync: WlanScan → 4s 대기 → WlanGetAvailableNetworkList(SSID별 최대 신호)
    // GetCurrentSsidAsync: WlanQueryInterface(current_connection). isState == wlan_interface_state_connected일 때만 SSID,
    //   아니면 null(연결 시도 중에도 SSID가 보이는 것을 #2 실장비에서 확인, 커밋 6e5c7ef).
    //   연결되지 않은 어댑터의 조회 결과 ERROR_INVALID_STATE(5023)도 null(#2 WlanNative와 같음).
}
```

**검증**: `Device` 카테고리 테스트(장치 전원 켠 상태, 수동 실행) — 어댑터 목록, `DWD-` AP 스캔, `ConnectOpenAsync` 후 게이트웨이 `192.168.244.1`(공유기에 연결된 상태에서 시작해도 같음), `RestoreAsync` 뒤 이전 상태 복귀·임시 프로필 삭제(`netsh wlan show profiles`), Wi‑Fi 연결 해제 상태에서 시작 → 접속 후 복구하면 연결 해제 상태(이전 SSID가 있는데 프로필을 못 찾는 경고·연결 해제 경로는 재현이 어려워 검증하지 않는다), 존재하지 않는 SSID로 `ConnectOpenAsync`(timeout 5s) 시 `TimeoutException`(U6 실측으로 확정: `WlanConnect`는 바로 성공을 돌려줌), 그 뒤 `RestoreAsync`로 이전 상태 복귀·임시 프로필 삭제.

## U7. App

**대상**: `DeviceConfig.App/Program.cs`, `AppConfiguration.cs`, `ExitCode.cs`, `ConsoleDeviceConfigSelector.cs`, `Security/ConfigSecretProtector.cs`.

**코드 초안**

`Program.RunAsync` 호스트 실행 부분(콘솔 템플릿에서 `WaitForShutdownAsync` 대신 Runner 직접 실행, 설계 9절):

```csharp
using (host)
{
    ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();
    try
    {
        await host.StartAsync();
        IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        await host.Services.GetRequiredService<DeviceConfigRunner>().RunAsync(lifetime.ApplicationStopping);
        await host.StopAsync();
        return ExitCode.Success;
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Host terminated unexpectedly");
        return ExitCode.RuntimeError;
    }
}
```

서비스 등록:

```csharp
builder.Services.AddSingleton(options);   // AppConfiguration + 복호화된 Pass → DeviceConfigOptions
builder.Services.AddSingleton<IDeviceConfigSelector, ConsoleDeviceConfigSelector>();
builder.Services.AddSingleton(sp => HostWifiLoader.CreateForCurrentOs(sp.GetRequiredService<ILogger<DeviceConfigRunner>>()));
builder.Services.AddSingleton<DeviceConfigRunner>();
```

콘솔 선택(세 메서드 공통). 라벨: 어댑터 `Name`, 장치 AP `"{Ssid} ({Signal})"`, 공유기 Wi‑Fi `"{Profile.Ssid} ({Signal} dBm)"`(같은 SSID가 여러 개여도 RSSI로 구분):

```csharp
private static T Select<T>(string title, IReadOnlyList<T> items, Func<T, string> label)
{
    if (items.Count == 0)
        throw new InvalidOperationException($"No {title} found");
    for (int i = 0; i < items.Count; i++)
        Console.WriteLine($"  [{i + 1}] {label(items[i])}");
    while (true)
    {
        Console.Write($"Select {title} (1-{items.Count}): ");
        string line = Console.ReadLine() ?? throw new InvalidOperationException("Console input closed"); // EOF → 종료 코드 3
        if (int.TryParse(line, out int n) && n >= 1 && n <= items.Count)
            return items[n - 1];
    }
}
```

입력 대기 중 Ctrl+C는 별도 처리하지 않는다. U8 실측(2026-11-11, Windows 콘솔): Ctrl+C를 누르면 `ReadLine`이 null을 돌려 바로 `Console input closed`(Fatal) 로그와 함께 종료 코드 3으로 끝난다. Enter 입력은 필요 없다.

`AppConfiguration`: 설계 6절 YAML. 필수 필드(`Settings.ApiServerAddr`·`ApiServerPort`·`ServerAddr`·`ServerPort`·`UserId`, `WifiProfiles[].Ssid`·`Pass`, `OsKms` 필수 항목)는 #2처럼 `[Property(…, required: true)]` + `ConfigurationValidator.Validate`로 검사한다.

```csharp
internal sealed class SettingsSection
{
    [Property(PropertyType.STRING, required: true)] public string ApiServerAddr { get; set; } = null!;
    [Property(PropertyType.INT, required: true)] public int ApiServerPort { get; set; }
    // ... ServerAddr, ServerPort, UserId 필수 / SslSupport, Company, Model, Lati, Long, Topic 선택(기본값)
}
```

`Validate()` — 포트(`Device.Port`, `ApiServerPort`, `ServerPort`) 1~65535, `TimeoutSeconds`·`ApConnectTimeoutSeconds` > 0, `Commands` 1개 이상, `ConnectAp`가 있으면 `WifiProfiles`(1개 이상, SSID 중복 없음)·`Settings` 필수, `OsKms.CredentialService` 절대 URI. 순서 검사 없음. `ConfigSecretProtector`는 #2 구현을 참고해 `WifiProfiles[].Pass`만 암호화·복호화한다(`mqtt_key` 처리 제거). `OsKms` 섹션은 항상 필수이고, `WifiProfiles`가 비어 있으면 KMS 처리를 건너뛴다.

**검증**: `dotnet build` 경고 0. 빌드한 exe로 종료 코드 시나리오 — 인자 없음 1, `--help` 0, 설정 파일 없음·`Commands` 비어 있음·`ConnectAp`인데 `Settings` 없음·`Settings.UserId` 누락·`TimeoutSeconds: 0` 2. 설정 파일의 평문 `Pass`가 `DEKENC:`로 다시 기록되는지 확인. `Commands: [Measurement]`, `WifiProfiles` 없이 표준 입력을 EOF로 넘겨 실행하면 종료 코드 3이고, DEK 파일이 생기지 않으며 `OsKms.Salt`가 기록되지 않는지 확인(KMS 건너뜀). 어댑터 선택 입력 대기 중 Ctrl+C 동작을 확인해 위 문구를 맞춘다. 성공(0)·명령 실패(3)는 실제 PC Wi‑Fi·콘솔 선택을 거치므로 U8 실장비에서 확인한다(실패 흐름 자체는 U5 모의 테스트). 테스트용 우회 코드는 넣지 않는다.

## U8. 실장비 전체 명령

**대상**: `tests/DeviceConfig.Tests/Device/RealDeviceTests.cs`(`DeviceConfigRunner` 전체를 실행: 로더의 `IHostWifi` + 아래 선택기), `tests/DeviceConfig.Tests/Device/EnvironmentSelector.cs`(`IDeviceConfigSelector` 테스트 구현: 어댑터는 첫 번째, 장치 AP는 `DEVICE_AP_SSID`, 공유기 Wi‑Fi는 `DEVICE_WIFI_SSID`와 같은 후보), 필요 시 `DeviceClient`의 `Reboot`·`TestFactoryReset` 처리.

**코드 초안**

```csharp
[TestClass, TestCategory("Device")]
public sealed class RealDeviceTests
{
    // 환경 변수: DEVICE_AP_SSID, (ConnectAp용) DEVICE_WIFI_SSID, DEVICE_WIFI_PASS, DEVICE_SETTINGS_* — 값은 로그·출력에 남기지 않는다
    [TestMethod] public Task Measurement() => RunAsync(DeviceCommand.Measurement);
    // ApScan, ScanAp, OnCmd, OffCmd, OverLoad, ConnectAp, FactoryReset, Reboot, TestFactoryReset 각각 1개
}
```

`Reboot`·`TestFactoryReset`는 실장비 결과로 처리 방식을 정한 뒤 `DeviceClient`에 반영한다. 후보: ① 응답 `{"mac":"…","respone":"reboot"}`(정적 분석 `docs/.tmp/b540-tcp-config.md` 2절)이 오면 그 값을 확인(`EnsureOk` 적용 안 함) ② 송신 후 응답 대기 없이 성공 ③ 타임아웃·연결 종료를 경고 후 성공. `{"command":"reboot"}`에 응답이 없으면 `connectap`처럼 mac을 넣은 `{"mac":"<mac>","command":"reboot"}`도 시험해 비교한다.

U8 결과(2026-11-11, `DWD-LS120_501f96`): `ConnectAp`는 설정 JSON·`connectap` 모두 `respone: OK`(로그의 `pass`·`mqtt_key`는 `***`). PC가 공유기에 연결된 상태에서 시작하자 복구 후 연결 해제로 끝나는 결함이 드러났다. 원인은 Windows `RestoreAsync`가 재연결 요청 뒤 연결 중인 임시 프로필을 지워 재연결이 취소된 것으로, 임시 프로필을 먼저 지우고 재연결하도록 고쳤다(삭제 실패는 로그 후 재연결, 재연결 예외 우선). 수정 후 공유기 연결 상태·연결 해제 상태 시작 모두 원래 상태로 복구됨을 확인했다.

`Reboot`·`TestFactoryReset` 결과: `Reboot`·`TestFactoryReset` 모두 응답·연결 종료 없이 10s 타임아웃, mac 포함 reboot도 15s 동안 같음, 약 40s 뒤 재접속 정상. 사용자 결정으로 ③ "대기 시간 초과·연결 종료는 경고 후 성공"을 채택해 `DeviceClient.RequestOrWarnAsync`로 반영했다(송신 형식은 `{"command":"reboot"}` 유지). 설계 5.2절 표를 갱신했다.

**검증**: 장치 상태를 덜 바꾸는 순서로 하나씩 수동 실행 — PC Wi‑Fi 접속·복구 → `Measurement`·`ScanAp`·`ApScan` → `OnCmd`/`OffCmd` → `OverLoad` → `ConnectAp` → `Reboot` → `FactoryReset`/`TestFactoryReset`. 각 결과를 기록하고 장치 재접속(SoftAP 복귀)을 확인한다. `Commands`에 여러 명령(예: `Measurement`, `ScanAp`, `OnCmd`, `OffCmd`)을 넣고 연속 실행해, 명령마다 재연결·`START` 핸드셰이크를 반복해도 정상인지 확인한다. 빌드한 exe로 성공 시 종료 코드 0, 명령 실패 시 3(예: 등록 Wi‑Fi가 `apscan`에 없는 `ConnectAp`)을 확인한다.
