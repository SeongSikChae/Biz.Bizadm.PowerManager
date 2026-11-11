---
status: done
category: mcp
feature: device-config-mcp
phase: 설계
---

# device-config-mcp 설계

선행: [0001-device-config-mcp-방안](0001-device-config-mcp-방안.md) · GitHub: SeongSikChae/Biz.Bizadm.PowerManager#7

## 1. 프로젝트 구조

규칙 `dotnet/project-creation.md`(net10.0, `.slnx`, CPM, MSTest)와 `dotnet/console-app-template.md`(Generic Host, `--config`/`--log`, YAML 검증, Serilog, 종료 코드 0/1/2/3)를 따른다.

```
src/
  Biz.Bizadm.PowerManager.DeviceConfig            (기존) Runner에 명령별 결과 통보 추가(3절)
  Biz.Bizadm.PowerManager.DeviceConfig.Mcp        (신규) stdio MCP 서버 실행 프로그램
tests/
  Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests  (신규) tool 로직 단위·모의, stdio 통합
```

| 프로젝트 | 참조 |
|---|---|
| `DeviceConfig.Mcp` | `DeviceConfig` + RID 조건 `HostWifi.Runtime.<os>` 하나(App csproj의 `HostWifiOs` 블록과 같음), 패키지: App과 같은 KMS·Serilog·YAML·CommandLineParser·Hosting + `ModelContextProtocol` 2.4.0 |
| `DeviceConfig.Mcp.Tests` | `DeviceConfig.Mcp`(`InternalsVisibleTo`), `DeviceConfig` |

`DeviceConfig.App`은 바꾸지 않는다. 설정·KMS 코드는 App에서 MCP 프로젝트로 **복사해 별도 작성**한다(App 리팩터링 없음).

## 2. 실행·stdio

- 실행: `Biz.Bizadm.PowerManager.DeviceConfig.Mcp --config <yaml> --log <dir>`. MCP 클라이언트 설정의 `command`/`args`로 등록한다.
- **경로 전제**: MCP 클라이언트가 서버를 띄우는 작업 폴더는 정해져 있지 않다. `--config`, `--log`, 설정의 `ContentRootPath`는 **절대 경로라고 가정**하고 그대로 쓴다(별도 변환·검사 없음). 상대 경로를 쓰면 프로세스 작업 폴더 기준이 되어 DEK 파일·로그 위치가 달라질 수 있다는 점을 등록 안내에 적는다.
- 기동 순서는 App과 같다: 인수 파싱(실패 1) → YAML 읽기·검증(실패 2) → KMS 처리·설정 재기록(실패 2) → Host 생성 → `host.RunAsync()`(stdin 종료 시 정상 종료 0, 예외 3). stdin 종료 시 SDK가 Host를 멈추는지는 구현 단계에서 실측으로 확정한다.
  - 구현 결과(U2): stdin 종료 시 SDK가 Host를 정지함을 실측했다. `host.RunAsync()`는 끝날 때 Host를 dispose해 기동 실패 로그를 남길 수 없으므로 `StartAsync` + `WaitForShutdownAsync`를 쓰고 dispose는 `using`에 맡긴다(개발 문서 U2).
- **실행 중 종료**: `run_device_commands` 실행 중 stdin이 닫히면 프로세스가 Runner의 복구(`finally`) 전에 끝날 수 있다. SDK가 진행 중인 tool을 기다리는지 U5에서 실측하고, 기다리지 않으면 `DeviceConfigService`를 `IHostedService`로도 등록해 `StopAsync`에서 진행 중인 호출(세마포어)이 끝날 때까지 기다린다.
  - 실측 결과(U5): stdin이 닫혀도 SDK가 진행 중인 tool이 끝날 때까지 기다린 뒤 Host를 정지해, Runner 복구가 끝난 다음 종료했다(3회, 약 20초 대기까지 확인). 따라서 `IHostedService` 종료 대기는 추가하지 않았다. 30초를 넘는 대기와 클라이언트의 강제 종료는 확인하지 않았다(개발 문서 U5).
- `Host.CreateApplicationBuilder` + `AddMcpServer().WithStdioServerTransport().WithTools<DeviceConfigTools>()`.
- **stdout은 MCP 프로토콜 전용**이다. 모든 출력은 stderr·파일로 보낸다.
  - Serilog Console sink는 `standardErrorFromLevel: Verbose`로 전부 stderr.
  - 기동 단계 메시지(`Configuration error`, `Configuration file updated with encrypted secrets`)도 `Console.Error`.
- 로그 파일 이름: `Biz.Bizadm.PowerManager.DeviceConfig.Mcp.log`(일별, 12개 보관).
- **단일 인스턴스 전제**: 여러 MCP 서버 프로세스가 같은 설정·어댑터를 동시에 쓰는 경우(클라이언트 세션마다 프로세스 기동)는 직렬화하지 않는다(방안 비범위).

## 3. Runner 결과 통보 (`DeviceConfig` 변경)

```csharp
public sealed record DeviceCommandResult(DeviceCommand Command, string Response);

public Task RunAsync(CancellationToken cancellationToken);   // 기존 시그니처 유지 → 아래에 null 전달
public Task RunAsync(Action<DeviceCommandResult>? onCommandCompleted, CancellationToken cancellationToken);
```

- `CancellationToken`을 마지막 인자로 두는 관례(CA1068)를 지키려고 오버로드로 추가한다. App 호출은 그대로다.
- 명령이 성공할 때마다 `onCommandCompleted`를 호출한다. 일반 명령은 `ExecuteAsync` 응답, `ConnectAp`는 `OK (<선택한 공유기 SSID>)`.
- 기존 로그(`{Command} response: …`)와 흐름·예외·복구는 그대로다.
- 실패한 명령은 통보하지 않는다(예외가 그대로 전파). 콜백 안에서 예외가 나면 명령 실패와 같이 전파되고 복구 경로를 탄다. MCP는 그때까지 받은 결과와 예외 메시지를 함께 돌려준다(5.4).

## 4. 설정 (YAML, `McpConfiguration`)

App 설정에서 `Commands`만 뺀 형식이다. 키는 속성 이름 그대로(PascalCase).

```yaml
ContentRootPath: D:\DeviceConfigMcp      # 절대 경로 가정(2절)
OsKms:                    # 필수 (App과 같음)
  CredentialService: https://deviceconfig.powermanager.local
  CredentialAccount: deviceconfig
  Salt:
  Iterations: 600000
  SecretsDekFilePath: deviceconfig.dek
Device:                   # App과 같음 (ApPrefix, Host, Port, TimeoutSeconds, ApConnectTimeoutSeconds)
WifiProfiles:             # 선택. ConnectAp 실행 시 필요
  - Ssid: MyRouter
    Pass: plain           # 기동 시 DEKENC:... 로 암호화해 다시 기록
Settings:                 # 선택. ConnectAp 기본값 (항목은 App과 같음). run_device_commands의 settings 인자가 필드 단위로 덮어쓴다(#9)
```

- 검증: OsKms·Device 검사는 App과 같다. `WifiProfiles`가 있으면 항목 필수·SSID 중복 검사, `Settings`가 있으면 필수·포트 범위 검사. 둘 다 **기동 시 필수는 아니다**(조회·일반 명령만 쓸 수 있게).
- KMS: App의 `ConfigSecretProtector`를 `McpConfiguration`용으로 복사한다. 처리·재기록·복구 방법은 App 설계 6절과 같다. `WifiProfiles`가 비면 KMS(자격 증명 저장소·DEK 파일)를 건드리지 않는다.
- 복호화한 `WifiProfile` 목록은 메모리에만 두고 tool 응답·로그에 `Pass`를 내지 않는다.

## 5. tool (`DeviceConfigTools` → `DeviceConfigService`)

- SDK가 tool 클래스 인스턴스를 호출마다 만들 수 있으므로 상태(세마포어)·로직은 싱글턴 `DeviceConfigService`에 두고 `DeviceConfigTools`는 얇은 래퍼로 둔다.
- PC Wi‑Fi를 쓰는 tool(`list_wifi_adapters`, `scan_device_aps`, `run_device_commands`)은 하나의 `SemaphoreSlim(1)`로 직렬화한다(프로세스 안). 앞 호출이 실행 중이면 끝날 때까지 **기다린다**(긴 `run_device_commands` 뒤의 조회는 클라이언트 타임아웃에 걸릴 수 있음). `list_wifi_profiles`는 메모리 값만 읽으므로 잠그지 않는다.
- 반환 값은 모두 **객체로 감싸고** 네 tool 모두 `UseStructuredContent = true`로 `structuredContent`·`outputSchema`를 채운다.
- 서비스 설정 record 이름은 `DeviceConfigService.ServiceSettings`(`DeviceSettings`와 혼동 방지).
- 조회 tool 실패는 `McpException(<원인 메시지>)`로 바꿔 던진다(SDK가 일반 예외 메시지를 가릴 수 있으므로). 결과는 SDK `isError`.

| tool | 인자 | 반환 | annotations |
|---|---|---|---|
| `list_wifi_adapters` | 없음 | `{adapters:[{id, name, currentSsid}]}` | readOnly |
| `scan_device_aps` | `adapterId` | `{deviceAps:[{ssid, signal}]}` (ApPrefix로 시작, 신호 내림차순) | readOnly |
| `list_wifi_profiles` | 없음 | `{ssids:[…]}` | readOnly |
| `run_device_commands` | `adapterId`, `deviceApSsid`, `commands[]`, `wifiSsid`(선택, 기본 null), `settings`(선택, 기본 null, #9) | 5.4 | destructive, openWorld |

### 5.1 PC Wi‑Fi 미구현 OS

`list_wifi_adapters`·`scan_device_aps`는 `NotImplementedException`이면 `McpException("PC Wi-Fi is not implemented on this OS")`. `run_device_commands`는 Runner 동작대로 경고 후 `Device.Host`로 접속한다. 이때 `adapterId`·`deviceApSsid`는 쓰지 않는다.

### 5.2 선택 구현 (`ParameterDeviceConfigSelector`)

`IDeviceConfigSelector`를 tool 인자로 구현한다. 호출마다 새로 만든다.

- `SelectAdapterAsync`: `Id == adapterId`(대소문자 무시) 없으면 `ArgumentException("Adapter not found: …")`
- `SelectDeviceApAsync`: `Ssid == deviceApSsid`(대소문자 구분) 없으면 `ArgumentException("Device AP not found in scan: …")`
- `SelectWifiProfileAsync`: `wifiSsid` 없으면 `ArgumentException("wifiSsid is required for ConnectAp")`, 후보에 없으면 `ArgumentException("Wi-Fi not in device apscan result: …")`

### 5.3 실행 전 검사

- `commands`가 비면 오류.
- `commands`에 `ConnectAp`가 있으면 Wi‑Fi 접속 전에 검사한다: `WifiProfiles`가 비지 않았고, 설정 파일 `Settings`와 `settings` 인자를 필드 단위로 합친 값에 필수 필드가 모두 있으며(포트 범위 포함), `wifiSsid`가 `WifiProfiles`에 등록된 SSID여야 한다. 아니면 오류. `settings`의 모르는 키(`user_id` 등)는 ConnectAp 여부와 관계없이 오류로 돌려주고, ConnectAp가 없으면 `settings`의 값 검사는 하지 않는다. 상세는 [0005](0005-device-config-mcp-settings-override-개발.md).
- 명령 이름은 `DeviceCommand` 열거형 이름과 **정확히 일치**(대소문자 구분)해야 한다. `Enum.TryParse`는 숫자(`"1"`)·쉼표 조합(`"OnCmd,OffCmd"`)을 통과시키므로 쓰지 않고 `Enum.GetNames`와 비교한다. 모르는 이름이면 오류.

### 5.4 `run_device_commands` 결과·진행 알림

호출마다 `DeviceConfigOptions`를 만든다(`Commands`=인자, 나머지는 설정). `DeviceConfigRunner`를 새로 만들어 `RunAsync(onCommandCompleted, ct)`를 호출한다.

```json
{ "success": true,  "results": [{"command":"OnCmd","response":"[DUT->PC] ON_CMD_OK#"}], "error": null }
{ "success": false, "results": [ ...완료된 명령... ], "error": "TimeoutException: ..." }
```

- 명령 실패·Wi‑Fi 실패·인자 오류(5.2·5.3)는 예외를 던지지 않고 `success: false`로 돌려준다(완료 결과를 잃지 않도록).
- PC Wi‑Fi 복구 요청은 Runner가 성공·실패·취소와 관계없이 한다. 재연결 완료는 기다리지 않는다(방안). 복구 실패는 Runner 동작대로 로그만 남는다.
- 취소(클라이언트 cancel)는 Runner에 전달하고 `OperationCanceledException`은 SDK에 맡긴다.
- **진행 알림**: tool 인자 `IProgress<ProgressNotificationValue>`(SDK가 클라이언트 progressToken이 있을 때 주입)로 시작(`0/n`)과 명령 완료마다(`i/n`, 명령 이름)를 보고한다. 마지막 명령 완료가 `n/n`이다(별도 끝 보고 없음). 알림 전달 시점은 SDK에 맡기며 응답보다 늦게 도착할 수 있다. 클라이언트 요청 타임아웃을 넘길 수 있으므로 등록 안내에 타임아웃 설정 방법(예: Claude Code `MCP_TOOL_TIMEOUT`)을 적는다.

### 5.5 tool 설명

각 tool `Description`에 다음을 적는다: `run_device_commands`는 실행 중 PC Wi‑Fi가 장치 AP로 바뀌었다가 복구 요청된다는 점(응답 직후 잠시 오프라인일 수 있음), 명령 목록과 각 명령의 의미(App 설계 5.2 표), 상태 변경 명령(`ConnectAp`, `Reboot`, `FactoryReset`, `TestFactoryReset`, `OnCmd`, `OffCmd`).

## 6. 테스트 (`DeviceConfig.Mcp.Tests`, MSTest)

| 종류 | 대상 |
|---|---|
| 단위 | `McpConfiguration` 검증(WifiProfiles·Settings 생략 가능, 중복 SSID 등), `ParameterDeviceConfigSelector` 일치·불일치 |
| 모의 | `IHostWifi` 모의 + 로컬 TCP 모의 장치로 `DeviceConfigService`: 조회, 성공 결과, 중간 실패 시 완료 결과 + error, 복구 호출, ConnectAp 사전 검사, 미구현 OS → Host, 진행 알림, 직렬화 |
| 기존 회귀 | `DeviceConfig.Tests` 전체 통과(Runner 변경 영향) |
| stdio 통합 | Mcp 프로젝트 출력 폴더의 exe를 SDK `McpClient`(stdio)로 실행 → `tools/list`의 4개 tool·annotations(readOnly 3, destructive 1)·`wifiSsid` 비필수, `list_wifi_profiles` 호출, 조회 실패 시 isError 메시지. 잘못된 설정 → `Process`로 직접 실행해 종료 코드 2 |

실장비 테스트는 이번 범위에서 수동 확인(MCP 클라이언트로 조회·`Measurement`)만 한다.

## 7. 결정 목록

| 항목 | 결정 |
|---|---|
| 명령별 결과 | Runner에 `RunAsync(Action<DeviceCommandResult>?, CancellationToken)` 오버로드 |
| 설정·KMS 코드 | MCP 프로젝트에 별도 작성(App 무변경) |
| 프로젝트 | `DeviceConfig.Mcp`, `DeviceConfig.Mcp.Tests` |
| SDK | `ModelContextProtocol` 2.4.0 (설치 시 취약·Deprecated 재확인) |
| 경로 | `--config`·`--log`·`ContentRootPath`는 절대 경로 가정, 변환·검사 없음 |
| 동시성 | 프로세스 안 `SemaphoreSlim(1)`(Wi‑Fi tool), 다중 프로세스는 비범위 |
| 복구 대기 | 재연결 완료 대기 없음 |
| 실패 반환 | `run_device_commands`는 `success`/`results`/`error`, 조회 tool은 `McpException` → isError |
| 반환 형식 | 모든 tool 결과를 객체로 감싸고 `UseStructuredContent = true` |
| 실행 중 조회 | 세마포어 대기(즉시 busy 오류 아님) |
| 실행 중 종료 | U5 실측 후, SDK가 기다리지 않으면 `IHostedService.StopAsync`에서 진행 중 호출 대기 |
| 명령 이름 | `Enum.GetNames` 정확 일치, `ConnectAp`면 `wifiSsid` 사전 검사 |
| 장시간 실행 | progress 알림 + 클라이언트 타임아웃 안내 |
