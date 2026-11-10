---
status: done
category: cli
feature: device-config
phase: 설계
---

# device-config 설계

선행: [0001-device-config-방안](0001-device-config-방안.md) · GitHub: SeongSikChae/Biz.Bizadm.PowerManager#6

## 1. 프로젝트 구조

기존 #2 프로젝트 3개를 먼저 삭제한 뒤 아래 프로젝트를 새로 만든다. 규칙 `dotnet/project-creation.md`(net10.0, `.slnx`, CPM, MSTest)와 `dotnet/console-app-template.md`를 따른다.

```
src/
  Biz.Bizadm.PowerManager.DeviceConfig                    프로토콜·전송·메시지 + HostWifi 공통 API·로더(facade 겸임)
  Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.Runtime.win    Windows WLAN 구현
  Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.Runtime.linux  스텁(NotImplementedException)
  Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.Runtime.osx    스텁(NotImplementedException)
  Biz.Bizadm.PowerManager.DeviceConfig.App                콘솔 실행 프로그램
tests/
  Biz.Bizadm.PowerManager.DeviceConfig.Tests              단위·모의 장치·Device 카테고리 실장비
```

### 1.1 참조

| 프로젝트 | 참조 |
|---|---|
| `DeviceConfig` | `Microsoft.Extensions.Logging.Abstractions`만 |
| `HostWifi.Runtime.*` | `DeviceConfig` |
| `App` | `DeviceConfig` + RID 조건으로 `HostWifi.Runtime.<os>` 하나 |
| `Tests` | `DeviceConfig` + Windows에서 `HostWifi.Runtime.win`(Device 카테고리용) |

- `HostWifi.Runtime.*` 세 프로젝트의 `AssemblyName`은 모두 `Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.Runtime`이다.
- `DeviceConfig`의 로더는 `Type.GetType("<Factory 타입>, Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.Runtime")`로 팩터리를 찾고 정적 `Create`를 리플렉션으로 호출한다(`Biz.Bizadm.KMS.Credentials`의 `OsKekCredentialProvider`와 같은 방식). 런타임 어셈블리가 없으면 `InvalidOperationException`.
- App의 RID 조건: `RuntimeIdentifier`가 `win`/`linux`/`osx`로 시작하면 그 OS, RID가 없으면 빌드 호스트 OS(`$([MSBuild]::IsOSPlatform(...))`)의 `Runtime` 프로젝트를 참조한다. 배포는 `dotnet publish -r <rid>`. NuGet 패키지화는 하지 않는다.

## 2. HostWifi API (`DeviceConfig`)

어댑터를 인자로 받는 상태 없는 API다. 런타임 구현은 이 인터페이스만 구현한다.

```csharp
public sealed record WifiAdapter(string Id, string Name);
public sealed record WifiNetwork(string Ssid, int Signal);

public interface IHostWifi
{
    Task<IReadOnlyList<WifiAdapter>> ListAdaptersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<WifiNetwork>> ScanAsync(WifiAdapter adapter, CancellationToken cancellationToken);
    Task<string?> GetCurrentSsidAsync(WifiAdapter adapter, CancellationToken cancellationToken);
    // 임시 프로필을 만들어 개방형 AP에 연결하고, 연결·IPv4 획득까지 timeout 안에서 기다린다.
    Task ConnectOpenAsync(WifiAdapter adapter, string ssid, TimeSpan timeout, CancellationToken cancellationToken);
    Task<IPAddress?> GetGatewayAsync(WifiAdapter adapter, CancellationToken cancellationToken);
    // previousSsid로 다시 연결하고(null이면 연결 해제) 임시 프로필을 삭제한다.
    Task RestoreAsync(WifiAdapter adapter, string? previousSsid, CancellationToken cancellationToken);
}
```

- 로더: `HostWifiLoader.CreateForCurrentOs(ILogger)` → `IHostWifi`(1.1절).
- `HostWifi.Runtime.linux`·`osx`는 모든 메서드가 `NotImplementedException`을 던진다.
- Windows `RestoreAsync`는 WLAN 연결에 프로필 이름이 필요하므로 사용 가능 네트워크 목록에서 SSID가 `previousSsid`와 같고 프로필 이름이 있는 항목을 찾아 그 프로필로 연결한다. 찾지 못하면 경고 로그를 남기고 연결을 해제한다.
- 장치 AP 접속 대기는 설정 `Device.ApConnectTimeoutSeconds`(기본 30)다. 현재 SSID가 장치 AP와 같고 어댑터에 IPv4 게이트웨이가 생길 때까지 기다린다(DHCP 대기 중 APIPA 주소에서 끝나지 않도록). 초과하면 `TimeoutException`으로 실패한다(5절 6단계 복구 후 종료 코드 3).

## 3. 선택 인터페이스 (`DeviceConfig`)

무선 어댑터·장치 AP·공유기 Wi‑Fi 선택은 `DeviceConfig`의 인터페이스로 분리하고, App이 콘솔 번호 선택으로 구현한다. 이후 MCP 등으로 교체할 수 있다.

```csharp
public interface IDeviceConfigSelector
{
    Task<WifiAdapter> SelectAdapterAsync(IReadOnlyList<WifiAdapter> adapters, CancellationToken cancellationToken);
    Task<WifiNetwork> SelectDeviceApAsync(IReadOnlyList<WifiNetwork> deviceAps, CancellationToken cancellationToken);
    // 등록 목록 중 장치 apscan 결과에 있는 것만 넘긴다.
    Task<WifiProfile> SelectWifiProfileAsync(IReadOnlyList<WifiProfileCandidate> candidates, CancellationToken cancellationToken);
}
```

## 4. 장치 접속 주소

장치 AP 접속 뒤 선택한 무선 어댑터의 IPv4 기본 게이트웨이를 장치 주소로 쓴다. 접속 대기 시간 안에 게이트웨이를 얻지 못하면 `TimeoutException`으로 실패한다(2절). PC Wi‑Fi 호출이 `NotImplementedException`(미구현 OS)으로 실패하면 경고를 남기고 `Device.Host`로 접속한다(방안 결정). 그 밖의 PC Wi‑Fi 실패(장치 AP 없음, 권한 거부, 접속 시간 초과 등)는 원인을 보이도록 그대로 실패 처리한다(6절).

## 5. 실행 흐름 (`DeviceConfigRunner`, `DeviceConfig`)

전체 흐름은 `DeviceConfig`의 `DeviceConfigRunner`에 둔다. `IHostWifi`·`IDeviceConfigSelector`·설정을 받아 실행한다. App은 설정 읽기·로그·콘솔 선택 구현만 맡는다.

1. `IHostWifi.ListAdaptersAsync` → `SelectAdapterAsync`
2. `GetCurrentSsidAsync`로 접속 전 SSID 기억
3. `ScanAsync`에서 SSID가 `Device.ApPrefix`로 시작하는 AP → `SelectDeviceApAsync`
4. `ConnectOpenAsync` → `GetGatewayAsync`로 장치 주소 결정(4절)
5. `Commands`를 순서대로 실행. **명령마다** TCP 연결 → 핸드셰이크 → 명령 → 연결 종료
6. `ConnectOpenAsync`를 호출했으면 성공·실패·취소와 관계없이 `RestoreAsync(adapter, 접속 전 SSID)`

1~4단계의 PC Wi‑Fi 호출이 `NotImplementedException`이면 경고를 남기고 `Device.Host`로 5단계를 진행한다. 그 밖의 실패는 실행을 멈춘다. `ConnectOpenAsync` 호출 전에 실패했으면 6단계 복구는 하지 않는다.

### 5.1 핸드셰이크

연결마다 `[DUT<-PC] START\n` → `hello tcp SUCCESS_CONNECT\n` → `[DUT->PC] START_OK:<mac>#` 수신. MAC은 설정 JSON·`connectap`에 쓴다. 공장검사 `START`는 핸드셰이크로만 보내고 별도 명령으로 두지 않는다.

수신 프레임은 `#` 프레임(공장검사 응답)과 JSON(중괄호 짝)으로 구분한다. `[DUT->PC] BUTTON_…#` 이벤트 프레임은 #2처럼 건너뛴다. 알려진 한계: JSON 프레임을 중괄호 짝으로 끊으므로 `apscan` 결과의 SSID에 `{`·`}`가 있으면 프레임이 깨진다(#2와 같음).

### 5.2 명령

| 명령 | 핸드셰이크 뒤 송신 | 응답 |
|---|---|---|
| `ConnectAp` | `{"command":"apscan"}` → Wi‑Fi 선택 → 설정 JSON → `{"mac":"<mac>","command":"connectap"}` (한 연결 안에서) | 각각 `apscan` 목록, `respone` `OK`, `respone` `OK` |
| `ApScan` | `{"command":"apscan"}` | `{"SCAN_AP_LIST":{...}}` |
| `Reboot` | `{"command":"reboot"}` | 미확정(방안 미확인 항목) |
| `FactoryReset` | `{"command":"factoryreset"}` | `respone` `OK` |
| `ScanAp` | `[DUT<-PC] SCAN_AP` | `{"SCAN_AP_LIST":{...}}` |
| `OnCmd` | `[DUT<-PC] ON_CMD` | `ON_CMD_OK#` |
| `OffCmd` | `[DUT<-PC] OFF_CMD` | `OFF_CMD_OK#` |
| `Measurement` | `[DUT<-PC] MEASUREMENT` | `MEASUREMENT_W:<값>#` |
| `OverLoad` | `[DUT<-PC] OVERLOAD` | `#` 프레임 |
| `TestFactoryReset` | `[DUT<-PC] FACTORY_RESET` | 미확정(방안 미확인 항목) |

응답 대기는 명령마다 `Device.TimeoutSeconds`(기본 10)다. 응답이 표의 형식과 다르면 실패다(`OverLoad`는 `#` 프레임인지만 본다).

## 6. 설정 (YAML, `AppConfiguration`)

키는 속성 이름 그대로(PascalCase). 괄호는 기본값이다.

```yaml
ContentRootPath: .
OsKms:
  CredentialService: https://deviceconfig.powermanager.local   # 절대 URI
  CredentialAccount: deviceconfig
  Salt:                     # 선택, 최초 기동 시 생성·기록
  Iterations: 600000        # 선택
  SecretsDekFilePath: deviceconfig.dek
Device:
  ApPrefix: DWD-            # (DWD-)
  Host: 192.168.244.1       # (192.168.244.1)
  Port: 5000                # (5000)
  TimeoutSeconds: 10        # (10)
  ApConnectTimeoutSeconds: 30  # (30)
Commands: [ApScan, ConnectAp]  # 1개 이상 필수, 순서 검사 없음
WifiProfiles:               # ConnectAp가 있으면 필수
  - Ssid: MyRouter
    Pass: plain             # 기동 시 DEKENC:... 로 암호화해 다시 기록
Settings:                   # ConnectAp가 있으면 필수
  ApiServerAddr: api.example.local   # string 필수
  ApiServerPort: 443                 # int 필수
  ServerAddr: mqtt.example.local     # string 필수
  ServerPort: 8883                   # int 필수
  SslSupport: true                   # bool (false) → yes/no
  UserId: user                       # string 필수
  Company: DAWONDNS                  # (DAWONDNS)
  Model: B540_W                      # (B540_W)
  Lati: ""                           # ("")
  Long: ""                           # ("")
  Topic: dwd                         # (dwd)
```

- OsKms 섹션은 #2와 같고 항상 필수다. 암호화 대상은 `WifiProfiles[].Pass`뿐이다(`mqtt_key`는 설정에 없음). `WifiProfiles`가 비어 있으면 KMS 처리(자격 증명·DEK 파일)를 건너뛴다.
- 설정 JSON 전송 시 모든 값을 문자열로 바꾼다(`SslSupport` → `yes`/`no`). `mqtt_key`는 실행마다 임의 생성한다.
- 검증 실패는 종료 코드 2(콘솔 템플릿).

## 7. `mqtt_key`·Wi‑Fi 대조·로그

- `mqtt_key`: 실행마다 `RandomNumberGenerator.GetString`으로 `[A-Za-z0-9/]`에서 16자를 뽑는다. 근거: 덤프 설정 섹터 `0x7c062`·`0x7d062`(두 벌 동일)의 값이 16자 `[A-Za-z0-9/]`(Base64 12바이트 형태로 추정), 뒤는 NUL, 필드 칸 33바이트(최대 32자). 설정 파일에 기록하지 않는다.
- Wi‑Fi 대조: `WifiProfiles[].Ssid`와 장치 `apscan` SSID가 대소문자까지 완전히 같은 것만 후보로 하고 RSSI 높은 순으로 `SelectWifiProfileAsync`에 넘긴다. 후보가 없으면 장치에 설정을 보내지 않고 실패한다(공유기가 장치 SoftAP와 같은 채널이면 빠질 수 있으니 채널을 바꿔 다시 시도하라는 로그를 남긴다).
- 인코딩: 장치 송수신은 UTF-8로 한다. 한글 등 비ASCII SSID는 지원하지 않는다(장치가 보내는 바이트 형식 미확인).
- 로그: 송수신 메시지를 Information으로 남긴다. 설정 JSON의 `pass`·`mqtt_key` 값만 `***`로 가린다.

## 8. 테스트

테스트 프로젝트는 `Biz.Bizadm.PowerManager.DeviceConfig.Tests` 하나(MSTest).

| 종류 | 대상 |
|---|---|
| 단위 | 핸드셰이크·응답 파서(`START_OK`, `#` 프레임, `respone`, 비정식 `SCAN_AP_LIST`), 설정 JSON 직렬화·마스킹, `mqtt_key` 형식, Wi‑Fi 대조 |
| App exe | 설정 검증·종료 코드 시나리오(테스트 프로젝트는 App을 참조하지 않음) |
| 모의 장치 | 로컬 TCP 모의 장치로 명령별 송수신, 명령마다 재연결, 실패 시 중단. `IHostWifi`·`IDeviceConfigSelector` 모의 객체로 `DeviceConfigRunner` 흐름(미구현 OS → `Device.Host`, 복구 호출) |
| 실장비(`Device` 카테고리, 기본 실행 제외) | Windows `HostWifi.Runtime.win`으로 실제 장치 AP 접속·복구와 **전체 명령**(상태 변경 명령 포함). 방안 미확인 항목(`Reboot`·`TestFactoryReset` 응답)도 여기서 확인해 처리 방식을 확정한다 |

## 9. 실패 처리

- 명령 하나가 실패하면(응답 타임아웃, `respone`이 `OK`가 아님, 연결 오류) 남은 명령을 실행하지 않는다.
- PC Wi‑Fi를 접속 전 상태로 복구한 뒤 종료한다. 종료 코드는 `3`(호스트 실행 중 예외)이다.
- 이를 위해 App은 `BackgroundService`를 쓰지 않는다. `Program`이 `host.StartAsync()` 뒤 `DeviceConfigRunner.RunAsync()`를 직접 호출하고 `host.StopAsync()`로 끝낸다. 예외는 콘솔 템플릿의 기존 catch(`LogCritical`, 종료 코드 3)로 처리한다.
