---
status: done
category: cli
feature: device-config
phase: 설계
---

# device-config 설계

선행: [0001-device-config-방안](0001-device-config-방안.md) · GitHub: SeongSikChae/Biz.Bizadm.PowerManager#2

## 1. 솔루션 구조

규칙 `dotnet/project-creation.md`, `dotnet/console-app-template.md`, `dotnet/kms-config-secrets.md`를 따른다.

```
Biz.Bizadm.PowerManager.slnx
Directory.Build.props / Directory.Packages.props   (없으면 생성, CPM, net10.0)
src/
  Biz.Bizadm.PowerManager.DeviceConfig/       프로토콜: 메시지 모델, TCP 채널, 세션, 명령
  Biz.Bizadm.PowerManager.DeviceConfig.App/   실행 진입점: 콘솔 템플릿(Generic Host), YAML, KMS, Serilog
tests/
  Biz.Bizadm.PowerManager.DeviceConfig.Tests/ MSTest, loopback TcpListener 모의 장치
```

- 참조: `DeviceConfig.App` → `DeviceConfig`, `DeviceConfig.Tests` → `DeviceConfig`.
- `DeviceConfig`는 Hosting·Serilog·YamlDotNet·KMS에 의존하지 않는다(로그는 `Microsoft.Extensions.Logging.Abstractions`만). 나중에 서버(#1) 관리 기능에서 재사용할 수 있게 한다.
- `Directory.*.props`는 서버(#1) 계획과 공유한다. 먼저 만드는 쪽이 생성하고, 나중 쪽은 항목만 추가한다.

## 2. 실행 흐름

```
Main ─ CommandLineParser(--config, --log)
     ─ YAML → AppConfiguration → ConfigurationValidator.Validate      (실패: 2)
     ─ OsKms: KEK → DEK → (MqttKey 비면 생성) → WifiProfiles[].Pass / MqttKey 암호화·YAML 갱신·복호화 (실패: 2)
     ─ Host 생성 (Serilog Console + 일별 File)                       (실패: 3)
     ─ DeviceConfigService(BackgroundService)
         장치 AP 탐색·PC 접속(3.3절) → 연결 → 핸드셰이크 → Commands 순서 실행
         → 연결 종료 → PC Wi‑Fi 복구 → StopApplication()
```

- 실행당 장치 1대. PC가 장치 AP를 찾으면 프로그램이 접속하고, 찾지 못하면 `Device.Host`로 접속한다(사용자가 미리 접속해 둔 경우).
- 통신 실패(연결·타임아웃·응답 불일치)는 예외로 올린다. 템플릿 기본 동작대로 호스트가 `BackgroundService failed`를 기록하고 정지하며 종료 코드는 `0`이다(템플릿 규칙 7). 성공·실패는 로그로 판단한다.
- KMS 처리 위치: 규칙상 순서는 설정 로드 → 검증 → KEK → DEK다. 호스트 생성 전에 처리하고, 실패는 종료 코드 `2`(설정 오류)로 본다.

## 3. 설정 스키마 (`AppConfiguration`, YAML, PascalCase)

```yaml
ContentRootPath: .
OsKms:
  CredentialService: https://deviceconfig.powermanager.local   # OS 자격 증명 금고(GCM) 서비스, 절대 URI
  CredentialAccount: kek
  Salt:                 # 최초 기동 시 생성·기록
  Iterations: 600000
  SecretsDekFilePath: secrets.dek
Device:
  ApSsidPrefix: DWD-    # PC Wi‑Fi 스캔에서 장치 AP로 볼 SSID 접두사
  ApConnectTimeoutSeconds: 30
  Host: 192.168.244.1     # 장치 AP를 찾지 못했거나 게이트웨이를 못 얻을 때 사용
  Port: 5000
  TimeoutSeconds: 5
  ApScanTimeoutSeconds: 15  # 장치 apscan 응답 대기(스캔이 일반 응답보다 오래 걸릴 수 있음)
Commands:               # 한 연결에서 순서대로 실행
  - ConnectAp
WifiProfiles:           # 등록 Wi‑Fi 목록. ConnectAp 때 ApScan 결과와 대조해 사용자가 선택
  - Ssid: HomeAP
    Pass: "********"    # 기동 시 KMS로 암호화(DEKENC:)
  - Ssid: OfficeAP
    Pass: "********"
Settings:               # ConnectAp가 있을 때 필수
  Mac:                  # 비우면 START_OK 응답의 MAC 사용
  UserId: user@example.com
  Company: DAWONDNS
  Model: B540_W
  Latitude: "0"
  Longitude: "0"
  ApiServerAddr: dwapi.dawonai.com
  ApiServerPort: "18443"
  MqttServerAddr: dwmqtt.dawonai.com
  MqttServerPort: "8883"
  SslSupport: "yes"     # yes | no
  Topic: dwd
  MqttKey:              # 비우면 기동 시 자동 생성. KMS 암호화 후 mqtt_key로 전송
  Fota:                 # 선택. 있으면 LFota_* 키로 전송
    Url:
    Addr:
    Port:
    Version:
```

- `Device.Port` 기본 5000, `TimeoutSeconds` 기본 5.
- `Commands`는 1개 이상 필수. 연결을 끝내는 명령(`ConnectAp`, `Reboot`, `FactoryReset`, `TestFactoryReset`)은 장치가 SoftAP를 떠나거나 재부팅하므로 목록의 마지막에 1개만 올 수 있다. 위반하면 설정 오류(종료 코드 2)다.
- `ConnectAp`가 있으면 `WifiProfiles` 1개 이상(각 `Ssid`·`Pass` 필수, `Ssid` 중복 금지·ASCII)과 `Settings`의 `UserId`·`Model`·`ApiServerAddr`·`ApiServerPort`·`MqttServerAddr`·`MqttServerPort`·`Topic`이 필수다(조건부 검증).
- 민감 필드: `WifiProfiles[].Pass`, `Settings.MqttKey`. 로그·예외에 평문을 남기지 않는다.

### 3.2 Wi‑Fi 선택

`ConnectAp`는 설정 JSON을 보내기 전에 다음을 한다.

1. 같은 연결에서 `{"command":"apscan"}`을 보내 `SCAN_AP_LIST`를 받는다.
2. 스캔 항목(`"<ssid>:<rssi>"`)을 마지막 `:` 기준으로 나눈다(SSID에 `:`가 있을 수 있음).
3. `WifiProfiles`와 SSID가 정확히 같은 항목만 후보로 남기고 RSSI 내림차순으로 정렬한다. 같은 SSID가 여러 번 나오면 가장 강한 RSSI만 쓴다.
4. 후보를 번호와 함께 콘솔에 출력하고 사용자가 번호를 입력한다. 후보가 1개여도 입력을 받는다.
5. 후보가 없거나 입력이 잘못되면 예외로 끝낸다(로그에 스캔된 SSID 목록 기록).

- 대조·정렬 로직(`WifiProfileMatcher`)과 선택 인터페이스(`IWifiProfileSelector`)는 `DeviceConfig` 라이브러리에 둔다. App은 콘솔 구현(`ConsoleWifiProfileSelector`)만 둔다. 나중에 로컬 MCP 등이 같은 인터페이스로 선택을 넘길 수 있다.
- 콘솔 입력이 리다이렉트된 상태(`Console.IsInputRedirected`)에서도 표준 입력의 한 줄을 읽는다.
- 위험: `apscan` 응답 형식과, `apscan` 뒤 같은 연결에서 설정·`connectap`을 이어 보낼 수 있는지는 미검증이다(12절).

### 3.3 장치 AP 탐색·PC 접속

근거(덤프 관찰): SoftAP SSID 형식 `DWD-LS120_%02x%02x%02x`(`0xdefc0`), SDK 파라미터 `0xfd0b4`/`0xfe0b4`의 SoftAP 비밀번호는 빈 값이다. 개방형 AP로 추정한다(인증 모드 필드는 미확인).

1. PC Wi‑Fi로 주변 AP를 스캔한다. 무선 어댑터가 없거나 OS 구현이 실패하면 경고 로그 후 `Device.Host`를 쓴다.
2. SSID가 `ApSsidPrefix`로 시작하는 AP만 후보로 둔다. 후보가 없으면 `Device.Host`를 쓴다.
3. PC가 이미 후보 AP 중 하나에 접속해 있으면 그 AP를 쓰고 접속·복구를 하지 않는다.
4. 후보를 RSSI 순으로 출력하고 사용자가 고른다(`IDeviceApSelector`, 후보가 1개여도 입력).
5. 접속 전 현재 연결(프로필 이름)을 기억하고, 개방형 임시 프로필로 장치 AP에 접속한다.
6. `ApConnectTimeoutSeconds` 안에 IPv4를 받을 때까지 기다린 뒤 해당 인터페이스의 기본 게이트웨이를 `Host`로 쓴다. 게이트웨이를 얻지 못하면 `Device.Host`를 쓴다. 접속 자체가 시간 초과면 예외로 끝낸다.
7. 장치 세션이 끝나면(성공·실패 모두, `finally`) 임시 프로필을 지우고 기억한 연결로 다시 접속한다. 복구 실패는 경고 로그만 남긴다.

OS별 구현(`IHostWifi`, 실행 OS로 선택):

| OS | 스캔 | 접속 | 복구·정리 |
|---|---|---|---|
| Windows | Native Wifi API `WlanScan` → `WlanGetAvailableNetworkList`(P/Invoke `wlanapi.dll`) | 개방형 프로필 XML `WlanSetProfile` → `WlanConnect` | `WlanConnect`(기억한 프로필) / `WlanDeleteProfile` |
| Linux | `nmcli -t -f SSID,SIGNAL,IN-USE dev wifi list --rescan yes` | `nmcli dev wifi connect <ssid> name <임시>` | `nmcli con up <기억한 연결>` / `nmcli con delete <임시>` |
| macOS | `system_profiler SPAirPortDataType -json` | `networksetup -setairportnetwork <if> <ssid>` | `networksetup -setairportnetwork <if> <기억한 SSID>`(키체인 사용) / `networksetup -removepreferredwirelessnetwork` |

- 외부 명령은 `ProcessStartInfo.ArgumentList`로 인수를 넘기고(셸 미사용) 종료 코드·stderr를 로그로 남긴다.
- 게이트웨이는 `NetworkInterface.GetIPProperties().GatewayAddresses`로 얻는다. 지원하지 않는 OS(macOS 등)에서는 `Device.Host`를 쓴다.
- Windows 외 구현은 이 PC에서 실행 검증할 수 없다. 단위 테스트는 명령 출력 파서와 인수 구성까지만 한다.

### 3.1 `mqtt_key` 자동 생성 (임시)

근거:

- 참고 구현에서 `mqtt_key`는 `POST /api/v1/devices/register/create` 응답(`{"verify":..,"mqtt_key":..}`)으로 받는 값이다. 참고 V3(B540 v1.0.30/v1.0.32)는 TCP로 보내지 않았고, V1(v1.0.26)은 TCP로 보냈다.
- 덤프(관찰): `mqtt_key`는 TCP 설정 키와 등록 응답 키(`status`, `REGISTERED`, `reason`)가 섞인 JSON 키 문자열 묶음(슬롯 A `0x5f097`, 슬롯 B `0xdf1b7`)에 있다. 설정 섹터 `+0x062`의 저장 키는 16자이고 영문 대소문자·숫자와 특수문자 1개로 구성된다. 다음 필드가 `+0x083`이라 최대 32자(+NUL)로 추정한다.
- 장치는 MQTT 접속 시 이 키를 비밀번호로 쓰는 것으로 추정한다. 서버(#1) 내장 브로커는 아직 인증하지 않는다.

결정:

- 서버(#1)가 키를 발급하기 전까지 CLI가 생성한다. 서버가 `register/create`에서 키를 발급하게 되면 생성 책임을 서버로 옮기고 이 절을 다시 검토한다.
- `ConnectAp` 실행이고 `MqttKey`가 비어 있으면 KMS 단계에서 16자 키를 생성한다. 문자 집합은 JSON·MQTT 비밀번호에 안전한 `[A-Za-z0-9]`이고, `RandomNumberGenerator.GetString`을 쓴다.
- 생성한 키는 바로 `DEKENC:`로 암호화해 YAML에 기록한다. 같은 YAML로 다시 실행하면 같은 키를 재사용한다.
- 로그에는 "생성함/기존 키 사용"만 남기고 값은 남기지 않는다.

위험(수용):

- 펌웨어 1.01.3x가 TCP로 받은 `mqtt_key`를 저장하는지 미확인이다.
- 장치가 이후 `register/create` 응답으로 키를 덮어쓸 수 있다.
- 키 값은 YAML에 암호문으로만 남는다. 서버(#1)에 등록하는 방법은 서버 작업에서 정한다.

## 4. 명령 (`DeviceCommand`)

연결 직후 항상 핸드셰이크를 한다: `[DUT<-PC] START\n` → `hello tcp SUCCESS_CONNECT\n` → `[DUT->PC] START_OK:<mac>#` 수신. 이후 `Commands`를 순서대로 실행한다.

| 값 | 송신 | 응답 종료 판정 |
|---|---|---|
| `ConnectAp` | `{"command":"apscan"}\n` → 응답 → Wi‑Fi 선택(3.2절) → 설정 JSON `\n` → 응답 → `{"mac":"<mac>","command":"connectap"}\n` | JSON |
| `ApScan` | `{"command":"apscan"}\n` (결과 SSID·RSSI를 로그로 출력) | JSON |
| `Reboot` | `{"command":"reboot"}\n` | JSON |
| `FactoryReset` | `{"command":"factoryreset"}\n` | JSON |
| `ScanAp` | `[DUT<-PC] SCAN_AP\n` | `#` |
| `OnCmd` | `[DUT<-PC] ON_CMD\n` | `#` |
| `OffCmd` | `[DUT<-PC] OFF_CMD\n` | `#` |
| `Measurement` | `[DUT<-PC] MEASUREMENT\n` | `#` |
| `OverLoad` | `[DUT<-PC] OVERLOAD\n` | `#` |
| `TestFactoryReset` | `[DUT<-PC] FACTORY_RESET\n` | `#` |

- 참고 구현이 검증한 것은 핸드셰이크와 `ConnectAp`뿐이다. 나머지 송신 형식은 정적 분석 문자열에서 추정했다(방안 미확인 항목).
- `Reboot`·`FactoryReset` 뒤에는 장치가 연결을 끊을 수 있다. 응답 없이 연결이 끊기면 경고 로그만 남기고 성공으로 본다.

## 5. 설정 JSON (`DeviceSettingsMessage`)

`System.Text.Json`, `JsonPropertyName`, null 값 생략. 키 순서는 참고 구현과 같게 둔다.

| JSON 키 | 설정 | JSON 키 | 설정 |
|---|---|---|---|
| `mac` | `Mac` 또는 핸드셰이크 MAC | `user_id` | `UserId` |
| `api_server_addr` | `ApiServerAddr` | `company` | `Company` |
| `api_server_port` | `ApiServerPort` | `model` | `Model` |
| `server_addr` | `MqttServerAddr` | `lati` | `Latitude` |
| `server_port` | `MqttServerPort` | `long` | `Longitude` |
| `ssl_support` | `SslSupport` | `topic` | `Topic` |
| `ssid` | 선택한 `WifiProfiles[].Ssid` | `mqtt_key` | `MqttKey`(자동 생성 가능) |
| `pass` | 선택한 `WifiProfiles[].Pass` | `LFota_Url`·`LFota_Addr`·`LFota_Port`·`LFota_Version` | `Fota.*`(선택) |

`mqtt_key`는 3.1절에 따라 `ConnectAp`에서 항상 보낸다(설정값 또는 자동 생성값).

## 6. 수신 (구분자 + 타임아웃)

`DeviceChannel.ReceiveAsync(DeviceFrame frame, CancellationToken)`가 버퍼에 누적하며 프레임 하나를 돌려준다.

| 프레임 | 완료 조건 |
|---|---|
| `Test`(`[DUT->PC] ...`) | `#` 수신 |
| `Json` | `{`부터 중괄호 깊이가 0이 되는 `}` 또는 `\n` |

- 남은 바이트는 다음 수신에 쓴다(조각·병합 응답 대응).
- 대기 중 `[DUT->PC] BUTTON_ON_EVT#`/`BUTTON_OFF_EVT#` 같은 비요청 이벤트는 로그로 남기고 건너뛴다.
- `TimeoutSeconds` 안에 완료되지 않으면 받은 내용을 로그로 남기고 `TimeoutException`을 던진다.
- 인코딩은 ASCII(참고 구현과 같음). SSID에 비ASCII가 있으면 설정 검증에서 거부한다.

## 7. 컴포넌트

### DeviceConfig (`Biz.Bizadm.PowerManager.DeviceConfig`)

| 타입 | 역할 |
|---|---|
| `DeviceCommand` | 4절 명령 enum |
| `DeviceSettings` | 설정 값(라이브러리 입력 모델, YAML과 무관) |
| `DeviceSettingsMessage`, `DeviceCommandMessage` | 5절 JSON 직렬화 모델 |
| `IDeviceChannel` / `TcpDeviceChannel` | 연결·송신·6절 프레임 수신 |
| `DeviceSession` | 핸드셰이크(MAC 보관), 명령별 송수신, 결과 `DeviceResponse`(원문·프레임) 반환 |
| `WifiProfile` | `Ssid`, `Pass` (복호화된 값) |
| `ScannedAp`, `ApScanParser` | `SCAN_AP_LIST` 응답 → `(Ssid, Rssi)` 목록 |
| `WifiProfileMatcher` | 3.2절 대조·중복 제거·RSSI 정렬 → 후보 목록 |
| `IWifiProfileSelector` | 후보 목록 → 선택 1개(`SelectAsync`). 콘솔·MCP 등 구현 교체 지점 |
| `HostWifi/IHostWifi` | PC Wi‑Fi: `ScanAsync`, `GetCurrentAsync`, `ConnectOpenAsync`, `RestoreAsync`, `RemoveTemporaryAsync` |
| `HostWifi/WindowsHostWifi`, `LinuxHostWifi`, `MacHostWifi`, `HostWifiFactory` | 3.3절 OS별 구현(`[SupportedOSPlatform]`)과 실행 OS 선택 |
| `DeviceApLocator` | 3.3절 1~7 흐름: 후보 필터·선택·접속·게이트웨이 결정, `IAsyncDisposable`로 복구 |
| `IDeviceApSelector` | 장치 AP 후보 → 선택 1개 |

### DeviceConfig.App (`Biz.Bizadm.PowerManager.DeviceConfig.App`)

| 타입 | 역할 |
|---|---|
| `Program`, `ExitCode` | 콘솔 템플릿 그대로 + KMS 단계 |
| `AppConfiguration` (+`DeviceSection`, `WifiProfileSection`, `SettingsSection`, `FotaSection`, `OsKmsSection`) | 3절 스키마·검증 |
| `ConsoleWifiProfileSelector`, `ConsoleDeviceApSelector` | 선택 인터페이스 콘솔 구현: 후보 번호 출력, 표준 입력 한 줄 읽기 |
| `DeviceConfigService : BackgroundService` | 2절 실행, 응답 원문을 Information 로그, 완료 후 `StopApplication()` |

## 8. 로그

- Serilog Console + 일별 File(`Biz.Bizadm.PowerManager.DeviceConfig.App.log`, 12개 보존), `Enrich.WithCaller()`.
- 송수신 원문을 Information으로 남긴다. 설정 JSON은 `pass`·`mqtt_key` 값을 `***`로 바꿔 기록한다.

## 9. 테스트

`DeviceConfig.Tests`(MSTest)에서 loopback `TcpListener` 모의 장치로 검증한다. 실장비 검증은 9.1절을 따른다.

- 핸드셰이크 송신 바이트와 MAC 파싱
- `ConnectAp` 송신 순서(apscan → 설정 → connectap)·JSON 키·null 생략·`LFota_*` 포함
- `ApScanParser`: `:` 포함 SSID, 빈 목록, 형식 오류
- `WifiProfileMatcher`: 대조·중복 SSID 최대 RSSI·정렬·후보 없음
- 고정 선택 구현으로 선택한 프로필의 `ssid`·`pass` 전송 확인
- `DeviceApLocator`: 모의 `IHostWifi`로 접두사 필터·후보 없음 → `Device.Host`·이미 접속 시 접속 생략·예외 시에도 복구 호출
- nmcli `-t` 출력(`\:` 이스케이프 포함)·system_profiler JSON 파서, 외부 명령 인수 구성
- Windows: 실제 무선 어댑터로 스캔 결과 조회만 수동 확인(장치 AP 접속은 실장치 필요)
- 조각난 응답·두 응답이 한 번에 온 경우·`BUTTON_*_EVT` 건너뛰기
- 타임아웃 예외, 연결 종료 처리
- 공장검사 명령 송신 문자열

App은 콘솔 템플릿 검증 표(종료 코드 0/1/2/3)와 KMS 첫 기동 암호화·재기동 유지를 빌드된 exe로 확인한다.

### 9.1 실장비 테스트

모의 장치 테스트는 참고 구현과 정적 분석에서 추정한 프로토콜만 검증한다. 12절 미확인 항목은 실장비(B540, 펌웨어 1.01.3x)로 확인한다.

- 방식: 기본 테스트 실행에서 제외되는 MSTest `Device` 카테고리 테스트(환경 변수로 장치 주소를 줄 때만 실행)와 빌드된 exe의 수동 실행을 함께 쓴다. 송수신 원문 로그와 장치 상태(LED·앱·공유기 DHCP·서버 #1 로그)를 증거로 남긴다.
- 순서: 장치 상태를 덜 바꾸는 것부터 한다. ① PC Wi‑Fi 장치 AP 탐색·접속·복구 → ② 핸드셰이크 → ③ 공장검사 읽기(`Measurement`, `ScanAp`) → ④ `OnCmd`/`OffCmd` → ⑤ `ApScan`(응답 형식·소요 시간) → ⑥ `ConnectAp`(테스트 공유기·서버 #1 대상) → ⑦ `Reboot` → ⑧ `FactoryReset`/`TestFactoryReset`(마지막, 복구 절차 확인 후)
- 각 단계 결과로 12절 항목을 확정·정정하고, 프로토콜이 다르면 해당 단계(설계·개발)로 회귀한다.
- 안전: 실장비 테스트 전 원본 펌웨어 덤프(`docs/.tmp/b540-dump1.bin`)를 보관해 두고, 플러그 부하는 분리하거나 저부하로 둔다. `FactoryReset` 계열은 사용자 확인 후에만 실행한다.
- `ConnectAp` 실장비 테스트는 장치를 제조사 클라우드가 아닌 테스트용 서버(#1)와 공유기로 보낸다. 서버(#1)가 준비되지 않았으면 ⑥ 이후는 장치가 공유기에 접속하는지(DHCP 임대)까지만 확인한다.

## 10. 의존성

- `DeviceConfig`: `Microsoft.Extensions.Logging.Abstractions`
- `DeviceConfig.App`: 콘솔 템플릿 패키지 + `Biz.Bizadm.KMS`, `Biz.Bizadm.KMS.Credentials`
- `DeviceConfig.Tests`: MSTest
- 버전은 개발 단계에서 `nuget-package-selection` 규칙으로 정한다.

## 11. 개발 단계에서 확인할 것

- `Biz.Bizadm.System.Configuration.Extensions`의 `ConfigurationValidator`가 중첩 섹션·목록·조건부 필수를 지원하는지. 지원하지 않으면 조건부 검증을 `Program`의 검증 단계에 둔다.

## 12. 미확인 (실장비 테스트 9.1절에서 확인)

- 덤프(관찰): `server_addr`·`server_port`는 `api_server_addr`(`0x5f054`/`0xdf174`)·`api_server_port`(`0x5f064`/`0xdf184`)의 끝부분으로만 존재한다. 문자열 끝부분 공유로 파서가 참조하는지, 펌웨어가 이 키를 쓰지 않는지(MQTT 주소를 `device/connect` 응답으로 받는지)는 구분하지 못했다. 참고 V3가 보냈던 키라 그대로 보낸다.
- 3.1절의 `mqtt_key` TCP 수용 여부.
- `apscan` 응답 형식(정적 분석 문자열 `{"SCAN_AP_LIST":{"<ssid>:<rssi>",...}}`)과 스캔 소요 시간. 별도 대기 시간 `Device.ApScanTimeoutSeconds`(기본 15)를 두고, 실장비 ⑤단계 결과로 기본값을 조정한다.
- `apscan` 뒤 같은 연결에서 설정·`connectap`을 이어 보낼 수 있는지.
- 장치 SoftAP 인증 모드(비밀번호 빈 값만 관찰).
- Windows 11 24H2 이후 데스크톱 앱의 Native Wifi 스캔은 위치 서비스 권한이 없으면 접근 거부로 실패할 수 있다. 실패하면 3.3절대로 `Device.Host`로 넘어간다.
- Linux(NetworkManager 필요)·macOS 구현의 실제 동작. macOS는 Wi‑Fi 스캔에 위치 권한이 필요할 수 있고, `system_profiler` 출력 키가 OS 버전마다 다를 수 있다.
