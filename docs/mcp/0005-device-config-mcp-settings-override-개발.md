---
status: done
category: mcp
feature: device-config-mcp-settings-override
phase: 개발
---

# device-config-mcp-settings-override 개발

선행: [0002 설계](0002-device-config-mcp-설계.md) 4절 설정·5.3절 · [0003 개발](0003-device-config-mcp-개발.md) · GitHub: SeongSikChae/Biz.Bizadm.PowerManager#9

단축(바로 구현): 기존 tool에 선택 인자 하나를 더하는 작은 확장이라 방안·설계는 생략하고, 결정 사항은 이 문서에 적는다. 바뀐 내용은 0002 설계의 tool 표와 5.3절에 반영한다.

## 문제

ConnectAp 설정 JSON 값(`Settings`)은 기동할 때 설정 파일에서만 읽는다. 기기마다 `UserId`·`Lati`·`Long`·`Topic` 등이 다르면 설정 파일을 고치고 MCP 서버를 다시 시작해야 한다.

## 결정 (사용자 합의)

- `run_device_commands`에 선택 인자 `settings`(object)를 추가한다. 서버에는 상태를 남기지 않는다(호출마다 적용).
- **필드 단위 덮어쓰기**: 인자로 넘긴(null이 아닌) 필드만 바꾸고, 나머지는 설정 파일 `Settings` 값을 쓴다.
- **설정 파일에 `Settings`가 없을 때**: 인자로 필수 필드(`apiServerAddr`·`apiServerPort`·`serverAddr`·`serverPort`·`userId`)를 모두 넘기면 ConnectAp를 허용한다. 선택 필드(`sslSupport`·`company`·`model`·`lati`·`long`·`topic`)는 `SettingsSection` 기본값을 쓴다.
- `WifiProfiles`(비밀번호)는 지금처럼 설정 파일에서만 받는다. 인자로 받지 않는다.
- `settings`는 ConnectAp에서만 쓴다. `commands`에 ConnectAp가 없으면 검사하지 않고 무시한다.
- 검사는 Wi‑Fi 접속 전에 한다. 실패하면 기존처럼 `success=false`, `error`로 돌려준다.
  - 설정 파일 `Settings`도 없고 인자도 없거나 `WifiProfiles`가 비어 있으면 기존 메시지(`ConnectAp requires Settings and WifiProfiles in the config file`)를 그대로 쓴다.
  - 필드를 합쳤는데도 필수 필드가 비어 있으면 `ConnectAp requires settings: missing apiServerPort, userId (set Settings in the config file or the settings argument)`.
  - 포트는 설정 파일과 같은 형식으로 검사한다: `settings.apiServerPort out of range: 70000`.
- 빈 문자열 검사처럼 설정 파일 검증에 없는 검사는 추가하지 않는다. null만 "넘기지 않음"으로 본다.
- 검사 순서: commands 파싱 → (ConnectAp일 때) 기존 메시지 조건 → 필수 필드 누락 → 포트 범위 → `wifiSsid`.
- **모르는 키는 거부한다**(검토 후 사용자 결정). 장치 JSON이 snake_case라 `user_id`처럼 보내기 쉬운데, 무시하면 덮어쓰기가 빠진 채 상태 변경 명령이 성공한다. 속성마다 `[Description]`으로 형식(문자열 여부)을 알린다.
  - 처음에는 `[JsonUnmappedMemberHandling(Disallow)]`로 SDK 바인딩 단계에서 거부했다. 그러나 구현 검토 후 stdio로 실측해 보니 SDK(2.4.0)가 원인을 가리고 `An error occurred invoking 'run_device_commands'.`만 돌려줬다.
  - 그래서 `[JsonExtensionData] UnknownKeys`로 모르는 키를 받아 두고, `RunAsync`에서 commands를 파싱한 직후에 `ArgumentException: Unknown settings keys: user_id (use camelCase names such as userId, apiServerPort)`를 던진다. 이 오류는 `success=false`, `error`로 돌아간다. ConnectAp 포함 여부와 관계없이 항상 거부하며, Wi‑Fi 접속 전에 끝난다. 입력 스키마의 properties에는 `UnknownKeys`가 나오지 않는다(11개 필드 그대로).
  - 타입 불일치(예: `serverPort: true`)는 지금도 SDK 바인딩에서 실패하고, 원인 없는 일반 오류(isError)로 나간다. 입력 스키마에 타입이 나와 있으므로 이번 범위에서는 다루지 않는다.

---

## U1. settings 인자와 필드 병합

**대상**
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/DeviceConfigService.cs`: `SettingsOverride`(새 record), `RunAsync`, `ResolveSettings`(새 메서드)
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/DeviceConfigTools.cs`: `RunDeviceCommands`
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/DeviceConfigServiceTests.cs`
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/StdioIntegrationTests.cs`

**코드 초안**

```csharp
// DeviceConfigService.cs — 입력 스키마는 SDK가 이 record로 만든다(camelCase, 모두 선택)
/// <summary>run_device_commands settings 인자. null 필드는 설정 파일 Settings 값(없으면 SettingsSection 기본값)을 쓴다.</summary>
public sealed record SettingsOverride
{
    [JsonExtensionData] // user_id 같은 모르는 키 → RunAsync에서 error로 거부(바인딩 거부는 SDK가 원인을 가림)
    public Dictionary<string, JsonElement>? UnknownKeys { get; init; }

    [Description("API server address (string)")]
    public string? ApiServerAddr { get; init; }
    // ... 나머지 속성도 [Description]: 포트는 integer, lati/long은 string(e.g. "37.5") ...
    public int? ApiServerPort { get; init; }
    public string? ServerAddr { get; init; }
    public int? ServerPort { get; init; }
    public bool? SslSupport { get; init; }
    public string? UserId { get; init; }
    public string? Company { get; init; }
    public string? Model { get; init; }
    public string? Lati { get; init; }
    public string? Long { get; init; }
    public string? Topic { get; init; }
}
```

```csharp
// DeviceConfigService.RunAsync — 시그니처에 SettingsOverride? settingsOverride 추가(wifiSsid 뒤)
DeviceSettings? deviceSettings = settings.BaseOptions.Settings;
if (parsed.Contains(DeviceCommand.ConnectAp))
{
    // Wi-Fi 접속 전에 검사한다(설계 5.3절)
    if ((settings.BaseOptions.Settings is null && settingsOverride is null) || settings.WifiProfiles.Count == 0)
        throw new ArgumentException("ConnectAp requires Settings and WifiProfiles in the config file");
    deviceSettings = ResolveSettings(settings.BaseOptions.Settings, settingsOverride);
    if (wifiSsid is null || !settings.WifiProfiles.Any(p => p.Ssid == wifiSsid))
        throw new ArgumentException($"wifiSsid must be one of list_wifi_profiles for ConnectAp: {wifiSsid}");
}
// ... existing ...
DeviceConfigRunner runner = new(hostWifi, new ParameterDeviceConfigSelector(adapterId, deviceApSsid, wifiSsid),
    settings.BaseOptions with { Commands = parsed, Settings = deviceSettings }, logger);
```

```csharp
/// <summary>필드 단위 병합: 인자 → 설정 파일 Settings → SettingsSection 기본값(선택 필드만).</summary>
internal static DeviceSettings ResolveSettings(DeviceSettings? file, SettingsOverride? o)
{
    if (o is null)
        return file!; // 호출 전에 둘 다 null인 경우를 걸렀다
    SettingsSection defaults = new();
    string? apiServerAddr = o.ApiServerAddr ?? file?.ApiServerAddr;
    int? apiServerPort = o.ApiServerPort ?? file?.ApiServerPort;
    string? serverAddr = o.ServerAddr ?? file?.ServerAddr;
    int? serverPort = o.ServerPort ?? file?.ServerPort;
    string? userId = o.UserId ?? file?.UserId;

    List<string> missing = [];
    if (apiServerAddr is null) missing.Add("apiServerAddr");
    if (apiServerPort is null) missing.Add("apiServerPort");
    if (serverAddr is null) missing.Add("serverAddr");
    if (serverPort is null) missing.Add("serverPort");
    if (userId is null) missing.Add("userId");
    // 필드마다 따로 검사해 아래에서 non-null 지역 변수로 받는다(-warnaserror에서 CS8629 방지, 검토 지적 1)
    if (apiServerAddr is null || apiServerPort is not int apiPort || serverAddr is null || serverPort is not int serverPortValue || userId is null)
        throw new ArgumentException($"ConnectAp requires settings: missing {string.Join(", ", missing)} (set Settings in the config file or the settings argument)");
    ValidatePort("settings.apiServerPort", apiPort);
    ValidatePort("settings.serverPort", serverPortValue);

    return new DeviceSettings
    {
        ApiServerAddr = apiServerAddr, ApiServerPort = apiPort,
        ServerAddr = serverAddr, ServerPort = serverPortValue, UserId = userId,
        SslSupport = o.SslSupport ?? file?.SslSupport ?? defaults.SslSupport,
        Company = o.Company ?? file?.Company ?? defaults.Company,
        Model = o.Model ?? file?.Model ?? defaults.Model,
        Lati = o.Lati ?? file?.Lati ?? defaults.Lati,
        Long = o.Long ?? file?.Long ?? defaults.Long,
        Topic = o.Topic ?? file?.Topic ?? defaults.Topic,
    };
}
```

`ValidatePort`는 `McpConfiguration`의 private 메서드와 같은 메시지 형식(`{name} out of range: {port}`)이다. `McpConfiguration.ValidatePort`를 `internal static`으로 열어 재사용할지, 서비스에 같은 두 줄을 둘지는 구현 시 `McpConfiguration`을 수정 범위에 넣지 않는 쪽(서비스에 private 두 줄)으로 한다.

```csharp
// DeviceConfigTools.RunDeviceCommands — wifiSsid 뒤에 선택 인자 추가
[Description("Per-device overrides of the config file Settings for ConnectAp. Only the given fields replace the config file values; omitted fields keep them. If the config file has no Settings, apiServerAddr, apiServerPort, serverAddr, serverPort and userId are required. Ignored without ConnectAp.")]
DeviceConfigService.SettingsOverride? settings = null) =>
    service.RunAsync(adapterId, deviceApSsid, commands, wifiSsid, settings, progress, cancellationToken);
```

tool `Description`의 ConnectAp 설명에 `optional settings overrides the config file Settings per field`를 덧붙인다.

**테스트 초안** (`DeviceConfigServiceTests`)

- 기존 `RunAsync(..., wifiSsid, null, ct)` 호출은 `settingsOverride` 자리에 `null`을 넣는다.
- `Run_ConnectAp_SettingsOverride_MergesPerField`: 파일 Settings + 인자 `{ UserId = "dev01", Lati = "37.5" }` → 장치가 받은 설정 JSON에 `"user_id":"dev01"`, `"lati":"37.5"`, 파일 값 `"api_server_addr":"api.example.local"`이 있다.
- `Run_ConnectAp_NoFileSettings_ArgumentComplete_UsesDefaults`: 파일 Settings null + 필수 5개 인자 → 성공, JSON에 `"company":"DAWONDNS"`, `"topic":"dwd"`.
- `Run_ConnectAp_NoFileSettings_ArgumentMissing_FailsBeforeWifi`: 파일 Settings null + `{ UserId = "x" }` → `error`가 `ArgumentException: ConnectAp requires settings: missing apiServerAddr, apiServerPort, serverAddr, serverPort (set Settings in the config file or the settings argument)`, `hostWifi.Calls` 비어 있음.
- `Run_ConnectAp_SettingsPortOutOfRange_FailsBeforeWifi` (`DataRow(0)`, `DataRow(65536)`): `ArgumentException: settings.apiServerPort out of range: {port}`.
- `Run_NoConnectAp_SettingsIgnored`: 파일 Settings null + 잘못된 포트 인자 + `["Measurement"]` → 성공.
- `Run_ConnectAp_FileSettings_ArgumentPortOutOfRange_Fails`: 파일 Settings 있음 + 인자 `{ ServerPort = 70000 }` → `settings.serverPort out of range: 70000`.
- `Run_ConnectAp_EmptyOverride_NoFileSettings_ReportsAllMissing`: 파일 Settings null + `settings: {}` → missing 5개 메시지.
- `Run_ConnectAp_OverrideWithoutProfiles_KeepsLegacyMessage`: 인자 있음 + WifiProfiles 비어 있음 → 기존 메시지.
- MergesPerField 테스트는 파일에도 있는 `UserId`·`ApiServerAddr`를 인자로 바꿔 인자가 이기는지 확인한다.
- `SettingsOverride_Deserialize_CamelCase`: SDK 직렬화 옵션(`McpJsonUtilities.DefaultOptions`)으로 `{"userId":"x","apiServerPort":443}`이 바인딩되는지 확인(검토 지적 2).
- `Run_UnknownSettingsKey_FailsBeforeWifi`(ConnectAp / Measurement): `{"user_id":..}` → `Unknown settings keys: user_id ...` error, Wi‑Fi·장치 호출 없음.
- `Stdio_RunUnknownSettingsKey_ReturnsErrorWithKey`: 실제 SDK 경로로 같은 error가 `structuredContent`에 나오는지 확인.
- 기존 `Run_ConnectApWithoutSettings_FailsBeforeWifi`는 바꾸지 않고 통과해야 한다(기존 메시지 유지).

장치가 받은 JSON은 기존 `device.Received`의 `Line`에서 `"pass"`가 든 줄을 찾아 확인한다.

**테스트 초안** (`StdioIntegrationTests`)

- `run_device_commands` 입력 스키마 properties에 `settings`가 추가되고, required는 `adapterId`·`deviceApSsid`·`commands` 그대로인지 확인(기존 테스트의 기대값 수정).
- `settings` 스키마의 properties에 `apiServerAddr`·`userId`·`lati` 등 11개 이름이 camelCase로 있고 required가 없는지 확인.

**검증**

```powershell
dotnet build Biz.Bizadm.PowerManager.slnx -warnaserror
dotnet test --solution Biz.Bizadm.PowerManager.slnx
```

- 새 테스트 통과, 기존 테스트 회귀 없음.
- 실제 장치 ConnectAp 실행은 상태 변경 명령이라 이번 검증에 넣지 않는다(실행 시 대상 장치 SSID를 사용자에게 확인).

**선행 단위**: 없음

## U2. 설계 문서 반영

**대상**: `docs/mcp/0002-device-config-mcp-설계.md` 4절(Settings 주석), 5절 tool 표, 5.3절 ConnectAp 검사

**코드 초안**

```yaml
Settings:                 # 선택. ConnectAp 기본값. run_device_commands의 settings 인자가 필드 단위로 덮어쓴다(#9)
```

```markdown
| `run_device_commands` | `adapterId`, `deviceApSsid`, `commands[]`, `wifiSsid`(선택, 기본 null), `settings`(선택, 기본 null, #9) | 5.4 | destructive, openWorld |

- `commands`에 `ConnectAp`가 있으면 Wi‑Fi 접속 전에 검사한다: `WifiProfiles`가 비지 않았고, 설정 파일 `Settings`와 `settings` 인자를 필드 단위로 합친 값에 필수 필드가 모두 있으며(포트 범위 포함), `wifiSsid`가 `WifiProfiles`에 등록된 SSID여야 한다. 아니면 오류. 상세는 [0005](0005-device-config-mcp-settings-override-개발.md).
```

**검증**: 문서 링크·표가 깨지지 않는지 확인.

**선행 단위**: U1
