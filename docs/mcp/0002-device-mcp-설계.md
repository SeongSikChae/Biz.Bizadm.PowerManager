---
status: done
category: mcp
feature: device-mcp
phase: 설계
---

# device-mcp 설계

선행: [0001-device-mcp-방안](0001-device-mcp-방안.md) · GitHub: SeongSikChae/Biz.Bizadm.PowerManager#4 · 관련: #5(OVERLOAD 의미 확인)

## 1. 솔루션 구조

```
src/
  Biz.Bizadm.PowerManager.DeviceConfig/          프로토콜 + PC Wi‑Fi + (이동) 설정·KMS·공통 실행 흐름
  Biz.Bizadm.PowerManager.DeviceConfig.App/      콘솔 도구(YAML Commands 순서 실행, 콘솔 선택)
  Biz.Bizadm.PowerManager.DeviceConfig.Mcp/      (신규) STDIO MCP 서버
tests/
  Biz.Bizadm.PowerManager.DeviceConfig.Tests/    기존 + 어댑터·공통 흐름 테스트
  Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/ (신규) MCP tool·선택 테스트
```

- App에 `internal`로 있던 설정 섹션, `ConfigSecretProtector`·`DecryptedSecrets`, `DeviceConfigService`의 실행 흐름, `DeviceSettings` 생성을 **프로토콜 라이브러리로 옮겨 `public`으로** 바꾼다. App과 Mcp는 라이브러리만 참조한다.
- 그 결과 라이브러리는 `Biz.Bizadm.KMS`, `Biz.Bizadm.KMS.Credentials`, `Biz.Bizadm.System.Configuration.Extensions`, `YamlDotNet`에 의존한다.

## 2. 무선 어댑터

### 2.1 `IHostWifi` 변경

어댑터 목록 조회를 추가하고, 스캔·현재 연결·접속은 어댑터 ID를 인자로 받는다. 복구·임시 프로필 삭제는 지금처럼 `HostWifiConnection.InterfaceId`를 쓴다. 구현은 상태를 갖지 않는다.

```csharp
/// <param name="Id">Windows는 GUID(<c>{...}</c>), Linux·macOS는 이름(<c>wlan0</c>, <c>en0</c>). NetworkInterface.Id와 같다.</param>
/// <param name="Name">사용자에게 보여 줄 이름(Windows <c>Wi-Fi 2</c>, Linux·macOS는 Id와 같음).</param>
public sealed record HostWifiAdapter(string Id, string Name, string? Description, string? ConnectedSsid);

public interface IHostWifi
{
    Task<IReadOnlyList<HostWifiAdapter>> ListAdaptersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<HostWifiNetwork>> ScanAsync(string adapterId, CancellationToken cancellationToken);
    Task<HostWifiConnection?> GetCurrentAsync(string adapterId, CancellationToken cancellationToken);
    Task<HostWifiConnection> ConnectOpenAsync(string adapterId, string ssid, CancellationToken cancellationToken);
    Task RestoreAsync(HostWifiConnection previous, CancellationToken cancellationToken);        // 시그니처 유지, Linux는 InterfaceId로 ifname 지정
    Task RemoveTemporaryAsync(HostWifiConnection temporary, CancellationToken cancellationToken); // 변경 없음
}
```

| OS | 목록 | 어댑터 지정 |
|---|---|---|
| Windows | `WlanEnumInterfaces`(GUID·설명). `Name`은 `NetworkInterface.Id == GUID`인 인터페이스의 `Name` | 모든 Wlan 호출에 해당 GUID |
| Linux | `nmcli -t -f DEVICE,TYPE,STATE,CONNECTION device status`에서 `wifi` | `nmcli dev wifi list ifname <id> --rescan yes`, `connect ... ifname <id>`, 복구 `connection up id <profile> ifname <InterfaceId>`(지금은 `ifname` 없음 → 추가) |
| macOS | `networksetup -listallhardwareports`의 Wi‑Fi 포트 전부 | `system_profiler` 결과에서 해당 이름의 인터페이스, `-setairportnetwork <id>` |

### 2.2 선택 지점

```csharp
public interface IWifiAdapterSelector
{
    /// <param name="adapters">1개 이상.</param>
    Task<HostWifiAdapter> SelectAsync(IReadOnlyList<HostWifiAdapter> adapters, CancellationToken cancellationToken);
}
```

- 후보가 1개여도 선택자를 부른다. 1개일 때 자동으로 쓸지는 선택자 구현이 정한다(App은 입력받음, MCP는 자동).
- App: `ConsoleWifiAdapterSelector` — `이름 (설명) - 연결: SSID|없음` 목록에서 번호 입력.

### 2.3 `DeviceApLocator` 변경

- 생성자에 `string? adapterId`를 받는다. `hostWifi`가 있으면 `adapterId`도 있어야 하고, 스캔·현재 연결·접속을 모두 그 어댑터로 한다.
- "이미 장치 AP에 연결됨" 판단도 그 어댑터의 현재 연결로 한다.
- `hostWifi`가 null이면(아래 3절에서 runner가 null로 넘기는 경우 포함) 설정 IP(`Device.Host`)로 대체한다. 스캔 실패·장치 AP 없음도 지금처럼 설정 IP로 대체한다.

## 3. 공통 실행 흐름 (라이브러리)

`DeviceConfigService.ExecuteAsync`의 흐름을 `DeviceRunner`로 옮긴다. App과 Mcp는 명령 실행 부분만 넘긴다.

```csharp
public sealed record DeviceSelectors(IWifiAdapterSelector Adapter, IDeviceApSelector DeviceAp);
public sealed record DeviceContext(DeviceSession Session, DeviceEndpoint Endpoint, HostWifiAdapter? Adapter, string Mac);

public sealed class DeviceRunner(IHostWifi? hostWifi, DeviceSection device, ILoggerFactory loggerFactory)
{
    // 어댑터 목록 → 선택 → 장치 AP 탐색·접속 → TCP → 핸드셰이크 → action → 연결 종료 → PC Wi‑Fi 복구
    public Task<T> RunAsync<T>(DeviceSelectors selectors, Func<DeviceContext, CancellationToken, Task<T>> action, CancellationToken cancellationToken);

    // list_device_aps용: 어댑터 선택 → 스캔 → 접두사 필터(DeviceApLocator와 같은 후보 규칙). 접속하지 않는다
    public Task<IReadOnlyList<HostWifiNetwork>> ScanDeviceApsAsync(IWifiAdapterSelector adapterSelector, CancellationToken cancellationToken);
}

public static class DeviceSettingsFactory
{
    public static DeviceSettings Create(SettingsSection settings, IReadOnlyList<WifiProfileSection> profiles, DecryptedSecrets secrets);
}
```

- 다음 경우 어댑터 선택을 건너뛰고 Locator에 `hostWifi: null, adapterId: null`을 넘겨 설정 IP로 접속한다. 이때 `DeviceContext.Adapter`는 null이다.
  - PC Wi‑Fi 제어를 지원하지 않는 OS(`hostWifi`가 null)
  - `ListAdaptersAsync` 실패(WLAN 서비스 중지, nmcli 없음 등). 경고 로그를 남긴다. 지금 스캔 실패를 설정 IP로 대체하는 것과 같다(유선 전용 PC의 App 동작 유지).
  - 무선 어댑터 0개
- App `DeviceConfigService`는 `RunAsync` 안에서 `Commands`를 순서대로 실행한다. 동작·로그는 지금과 같다(어댑터 선택 입력 1회 추가). 표준 입력을 리다이렉트해 실행하던 경우 입력 첫 줄에 어댑터 번호가 추가된다(장치 AP 번호보다 먼저).
- Wi‑Fi 프로필 선택(`IWifiProfileSelector`)은 runner가 아니라 action 안의 `ConnectApAsync`가 쓴다(지금과 같음).
- 복구는 예외·취소와 관계없이 `DeviceApLocator.DisposeAsync`가 한다(지금과 같음). MCP 클라이언트가 tool 호출을 취소해도 같다.
- 장치 AP 후보 규칙(접두사 필터·SSID 중복 제거·신호 내림차순)은 `DeviceApLocator`와 `ScanDeviceApsAsync`가 같은 함수를 쓴다.

## 4. 설정

### 4.1 섹션 공유

```csharp
// 라이브러리: Biz.Bizadm.PowerManager.DeviceConfig.Configuration
public abstract class DeviceConfigurationBase : IValidatableConfiguration
{
    public string ContentRootPath { get; set; } = null!;
    public OsKmsSection OsKms { get; set; } = null!;
    public DeviceSection Device { get; set; } = new();
    public List<WifiProfileSection> WifiProfiles { get; set; } = [];
    public SettingsSection? Settings { get; set; }
    /// <summary>ConnectAp(설정 전송)을 쓰는 설정인지. 보안값 처리와 검증 기준.</summary>
    [YamlIgnore] // 보안값 재작성 때 YAML에 나가지 않게 한다
    public abstract bool UsesConnectAp { get; }
    public virtual void Validate() { /* 공통 검증: OsKms·Device, UsesConnectAp면 WifiProfiles·Settings */ }
}

// App: Commands 추가, 기존 검증 유지
//   Commands 1개 이상, 종료성 명령(ConnectAp·Reboot·FactoryReset·TestFactoryReset)은 마지막,
//   ConnectAp 포함 시 WifiProfiles(1개 이상·중복 없음·ASCII)·Settings 필수
internal sealed class AppConfiguration : DeviceConfigurationBase
{
    public List<DeviceCommand> Commands { get; set; } = [];
    public override bool UsesConnectAp => Commands.Contains(DeviceCommand.ConnectAp);
}

// Mcp: Commands 없음. Settings는 선택(없으면 configure_device만 오류)
//   Settings가 있으면 App의 ConnectAp 포함 때와 같은 WifiProfiles·Settings 검증
internal sealed class McpConfiguration : DeviceConfigurationBase
{
    public override bool UsesConnectAp => Settings is not null;
}
```

- MCP는 `Settings.Mac`을 무시한다. 값이 있으면 기동 시 경고 로그를 남기고, `configure_device`는 항상 핸드셰이크로 받은 MAC을 보낸다(`DeviceSettings.Mac = null`). 서버 하나로 여러 장치를 설정하기 때문이다. App은 지금처럼 `Settings.Mac`을 쓴다.

- 섹션 클래스(`OsKmsSection`, `DeviceSection`, `WifiProfileSection`, `SettingsSection`, `FotaSection`)와 YAML 키는 바꾸지 않는다. 기존 App 설정 파일은 App에서 그대로 읽힌다.
- MCP에 App 설정 파일을 그대로 주면 `Commands` 키가 모르는 속성이라 설정 오류(종료 코드 2)가 난다. MCP 전용 형식이므로 무시하지 않는다.
- `UsesConnectAp`처럼 YAML 키가 아닌 계산 속성에는 `[YamlIgnore]`를 붙인다.
- MCP 설정 예시는 `device.mcp.sample.yaml`로 둔다. `Device.TimeoutSeconds` 등 기본값은 App과 같다.

### 4.2 보안값

- `ConfigSecretProtector`·`DecryptedSecrets`를 라이브러리로 옮기고 `DeviceConfigurationBase`를 받게 한다. 동작(기동 시 암호화, 평문이면 파일 재작성, 복호화 값 반환)은 같다.
- 지금 `Commands.Contains(ConnectAp)`로 판단하던 곳(`HasSecrets`, `mqtt_key` 자동 생성)은 `UsesConnectAp`로 바꾼다. App 동작은 같다.
- MCP는 `Settings`가 있고 `MqttKey`가 비어 있으면 **기동 시** App과 같이 생성·암호화해 YAML에 저장한다. 이후 `configure_device` 호출마다 같은 값을 쓴다.
- 진행 메시지를 쓰는 `Action<string> log`에 MCP는 stderr 출력을 넘긴다(stdout 금지). 설정 파일 재작성 안내(App은 `Program`에서 stdout 출력)도 MCP `Program`에서는 stderr로 출력한다.
- tool 인자와 결과에 비밀번호·`mqtt_key`를 넣지 않는다.

## 5. 대기 시간

- `OverLoad`는 응답에 4.5~4.8s가 걸려(#5 측정) 일반 대기(`TimeoutSeconds` 기본 5s)에 가깝다. `ApScan`·`ScanAp`처럼 긴 대기(`ApScanTimeoutSeconds`, 기본 15s)를 쓴다. App에도 같이 적용된다.
- 변경 위치는 `DeviceSession`의 `OverLoad` 처리(지금 `timeouts.Response` 사용)다. `DeviceTimeouts.ApScan` 이름은 유지하고 문서 주석에 `OverLoad`도 쓴다고 적는다.

## 6. MCP 서버 (`DeviceConfig.Mcp`)

### 6.1 기동

- 콘솔 템플릿과 같은 `--config`/`--log`, 종료 코드 0/1/2/3, YAML 검증, KMS 처리.
- `builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<DeviceTools>()`. SDK는 `ModelContextProtocol`(버전은 개발 단계에서 NuGet 규칙으로 선택).
- 서버 설명(`instructions`)에 사용 전제(로컬 LLM / Wi‑Fi+유선 / Wi‑Fi+Wi‑Fi)를 적는다.
- 클라이언트가 표준 입력을 닫으면(연결 종료) 호스트를 정지하고 종료 코드 0으로 끝난다. 실행 중인 tool은 취소되고 PC Wi‑Fi는 복구된다.

### 6.2 로그

- Serilog Console sink는 `standardErrorFromLevel: Verbose`로 모든 로그를 stderr에 쓴다. 파일 sink는 App과 같다(`Biz.Bizadm.PowerManager.DeviceConfig.Mcp.log`).
- tool 호출마다 tool 이름·인자(SSID 등)·결과 요약을 남긴다.

### 6.3 동시 호출

- 장치 tool(`list_wifi_adapters` 제외 전부)은 서버 안의 `SemaphoreSlim(1)`로 한 번에 하나씩 실행한다. 나중 호출은 앞 호출이 끝날 때까지 기다린다.

### 6.4 선택 (`McpSelectors`)

어댑터·장치 AP·Wi‑Fi 모두 같은 규칙이다.

| 경우 | 동작 |
|---|---|
| tool 인자 있음 | 후보 중 정확히 같은 값(어댑터는 `Id` 또는 `Name`, 장치 AP·Wi‑Fi는 SSID)을 쓴다. 없으면 후보 목록과 함께 오류 |
| 인자 없음, 후보 1개 | 그대로 쓴다 |
| 인자 없음, 후보 여러 개, 클라이언트가 elicitation 지원 | elicitation으로 후보(enum) 중 하나를 고르게 한다. 거절·취소면 오류 |
| 인자 없음, 후보 여러 개, elicitation 미지원 | 후보 목록과 함께 오류 |

- 오류일 때 장치에는 아무것도 보내지 않는다. 장치 AP 접속 전이면 PC Wi‑Fi도 바꾸지 않는다.
- `McpSelectors`는 tool 호출마다 그 호출의 인자와 MCP 서버 객체(elicitation 요청용)로 만들어 `DeviceSelectors`로 넘긴다.
- Wi‑Fi 선택자는 고른 프로필을 `Selected` 속성에 기록한다. `configure_device`는 이 값으로 결과의 `wifiSsid`를 채운다(`ConnectApAsync` 시그니처는 바꾸지 않는다).

### 6.4.1 DI

- `HostWifiFactory.Create`는 지원하지 않는 OS에서 null을 돌려주므로 `IHostWifi`를 직접 등록하지 않는다. `HostWifiHolder(IHostWifi? Value)`를 싱글턴으로 등록하고, `DeviceRunner`와 `list_wifi_adapters`가 이것을 받는다.
- `DeviceTools`는 `IDeviceRunner`(`RunAsync`·`ScanDeviceApsAsync`) 인터페이스를 받는다. 테스트에서 가짜로 바꾸기 위해서다. `DeviceRunner`가 이를 구현한다.

### 6.5 tool

장치에 접속하는 tool(`get_device_info`~`factory_test`)은 선택 인자 `adapter`, `deviceAp`를 받는다. 표의 "추가 인자"는 그 밖의 인자다. `list_device_aps`는 `adapter`만, `list_wifi_adapters`는 인자가 없다. 호출 1회 = `DeviceRunner.RunAsync` 1회 = 명령 1개다.

| tool | 추가 인자 | 실행 | 결과(해석값) | 표시 |
|---|---|---|---|---|
| `list_wifi_adapters` | — | `ListAdaptersAsync` | `[{id, name, description, connectedSsid}]` | readOnly |
| `list_device_aps` | `adapter?` | `DeviceRunner.ScanDeviceApsAsync`(접속 안 함) | `[{ssid, signal}]` | readOnly |
| `get_device_info` | — | 핸드셰이크만 | `{mac}` | readOnly |
| `scan_device_wifi` | — | `ApScan` | `{networks:[{ssid, rssi, registered}]}` | readOnly |
| `configure_device` | `wifiSsid?` | `ConnectAp` | `{wifiSsid}` | destructive |
| `reboot_device` | — | `Reboot` | — | destructive |
| `factory_reset_device` | — | `FactoryReset` | — | destructive |
| `factory_test` | `command`(enum) | `ScanAp`/`OnCmd`/`OffCmd`/`Measurement`/`OverLoad`/`TestFactoryReset` | 명령별(아래 표) | destructive |

- `list_wifi_adapters`·`list_device_aps`는 PC Wi‑Fi 제어를 지원하지 않는 OS에서 오류를 돌려준다. 장치 tool은 그 경우 설정 IP로 접속한다(3절).
- 공통 결과(JSON 객체): `{adapter, deviceAp, host, mac, command, raw, disconnected, ...해석값}`. 해석에 실패해도 `raw`는 돌려준다.
- `configure_device`의 Wi‑Fi는 YAML `WifiProfiles` 중 장치 `apscan` 결과에 있는 것만 후보다(지금 `ConnectAp`와 같음). `wifiSsid`는 SSID 이름만 받는다.
`factory_test` 명령별 응답과 해석값(근거: 0004 실장비, #5 측정):

| 명령 | 응답(확인된 형식) | 해석값 |
|---|---|---|
| `Measurement` | `[DUT->PC] MEASUREMENT_W:<수>#` | `watts` |
| `OverLoad` | `[DUT->PC] OVERLOAD_A:<수>#` | `overloadA`(단위·의미 미확인, #5) |
| `OnCmd` / `OffCmd` | `[DUT->PC] ON_CMD_OK#` / `OFF_CMD_OK#` | `ok`(`_OK` 여부) |
| `ScanAp` | `{"SCAN_AP_LIST":{...}}`(JSON 프레임) | `networks`(`ApScanParser`) |
| `TestFactoryReset` | 응답 없이 연결 종료 | `disconnected: true` |

- 형식이 다르면 해석값 없이 `raw`만 돌려준다(오류로 보지 않는다).
- `factory_test`의 `OverLoad` 설명에 "값의 의미 미확인(#5)"을 적는다.
- `reboot_device`: 실장비에서 `Reboot`는 응답도 연결 종료도 없이 타임아웃으로 실패했고 장치는 재부팅됐다(0004 2.6절). 처리 방식은 #3에서 정하므로 이번에는 App과 같이 타임아웃을 tool 오류로 돌려준다. tool 설명과 오류 메시지에 "응답 없이 재부팅될 수 있음(#3)"을 적는다.
- 장치 오류(`DeviceException` 등)·타임아웃은 MCP tool 오류(`isError`)로 돌려준다. 메시지에 단계(어댑터/장치 AP/TCP/핸드셰이크/명령)를 넣는다.

## 7. 테스트

| 대상 | 방법 |
|---|---|
| 어댑터 목록·지정(OS별) | 기존 `ProcessRunner` 가짜로 nmcli·networksetup 인자 확인(Linux 복구 `ifname` 포함), Windows는 이름 매핑 함수 단위 테스트 |
| `DeviceApLocator` | 기존 테스트에 어댑터 ID 전달·다른 어댑터 무시 케이스 추가 |
| `DeviceRunner` | 가짜 `IHostWifi`·채널로 순서·복구(예외·취소 포함), 목록 실패·0개 → 설정 IP |
| 설정 검증(신규) | 지금은 App 설정·보안값 테스트가 없다. 라이브러리로 옮기는 `DeviceConfigurationBase` 검증과 `UsesConnectAp`(App·MCP), MCP `Settings.Mac` 무시를 새로 테스트한다 |
| 기존 테스트 수정 | `IHostWifi` 시그니처 변경으로 `DeviceApLocatorTests`, `RealDeviceTests`, `HostWifi/*Tests`(Windows·Linux·macOS)를 고친다 |
| `McpSelectors` | 인자 일치/불일치, 후보 1개, elicitation 선택·거절, elicitation 미지원, Wi‑Fi `Selected` 기록 |
| `DeviceTools` | 가짜 `IDeviceRunner`로 결과 JSON·명령별 해석값·오류 변환 |
| 실장비(`Device` 카테고리) | SDK 클라이언트로 서버 exe를 STDIO로 띄워 `list_wifi_adapters`, `get_device_info`, `factory_test Measurement` 실행 |

## 8. 의존성

| 프로젝트 | 추가 |
|---|---|
| DeviceConfig | `Biz.Bizadm.KMS`, `Biz.Bizadm.KMS.Credentials`, `Biz.Bizadm.System.Configuration.Extensions`, `YamlDotNet` (App에서 이동) |
| DeviceConfig.Mcp | `ModelContextProtocol`, `Microsoft.Extensions.Hosting`, `Serilog.*`, `CommandLineParser` (App과 같은 템플릿 패키지) |
| DeviceConfig.Mcp.Tests | `MSTest`, `ModelContextProtocol`(클라이언트) |

## 9. 결정 목록

| 항목 | 결정 |
|---|---|
| 공유 코드 위치 | 프로토콜 라이브러리로 이동(`public`) |
| 어댑터 인터페이스 | `IHostWifi` 메서드에 `adapterId` 인자, `ListAdaptersAsync` 추가 |
| elicitation 미지원 | 후보 목록과 함께 tool 오류 |
| 동시 호출 | 서버 안에서 순서대로 대기 |
| `OverLoad` | `factory_test`에 포함, 긴 대기 시간 사용(App 포함), 설명에 #5 표기 |
| 스캔 tool | `scan_device_wifi`(ApScan)와 `factory_test`의 `ScanAp`로 나눔 |
| 결과 형식 | 해석값 + 원시 응답(JSON 객체) |
| MCP `mqtt_key` | `Settings`가 있으면 기동 시 생성·암호화(App과 같은 방식). 판단 기준은 `UsesConnectAp` |
| MCP `Settings.Mac` | 무시(경고 로그), 항상 핸드셰이크 MAC |
| `OverLoad` 해석값 | 중립 이름 `overloadA`, 단위·의미 미확인(#5) |
| `configure_device`의 SSID | Wi‑Fi 선택자가 고른 값을 기록해 결과에 넣음 |

## 10. 미확인 (개발·실장비에서 확인)

- `ModelContextProtocol` 선택 버전의 elicitation API 이름과 클라이언트 capability 확인 방법
- Windows에서 GUID → `NetworkInterface.Name` 매핑이 USB 무선 어댑터에서도 되는지
- Wi‑Fi + 유선 환경에서 장치 AP 접속 중 기본 경로가 Wi‑Fi로 바뀌는지(방안 미확인 항목)
- 사용할 MCP 클라이언트의 tool 호출 타임아웃(장치 AP 접속~복구 약 10~20s)
- `configure_device`의 Wi‑Fi 선택은 장치에 TCP로 연결해 `apscan`을 받은 뒤에 elicitation을 한다. 사람이 고르는 동안 장치가 TCP 연결을 얼마나 유지하는지 모른다. 끊기면 장치 오류로 돌려주고 복구한다. 오래 걸리면 `wifiSsid` 인자로 다시 호출하게 안내한다.
