# 다원 가짜 장비 클라이언트 계획

## 요약

새 콘솔 실행 프로그램 `Biz.Bizadm.PowerManager.FakeDevice.App`을 만든다. 구조는 저장소 규칙 `dotnet/console-app-template.md`와 기존 `DeviceConfig.App`을 따른다. Generic Host, `--config`·`--log`, YAML 설정 검증, Serilog, 종료 코드 0/1/2/3을 쓰고, OsKms로 비밀 값을 암호화한다.

실행 흐름은 실행기 `FakeDeviceRunner` 하나가 맡는다. 다원 REST(등록·접속 조회), MQTT 3.1 TLS 접속·구독·발행, 콘솔 명령, FOTA 받기, JSONL 기록을 다음 순서로 한다.

1. `register/create`(설정에 키가 없을 때)
2. `device/connect`
3. MQTT 접속
4. 구독
5. 정해진 발행(`rg`·`tisyn`·`n`)
6. 주기 발행과 콘솔 명령, 수신 처리를 함께 돌림
7. 종료

Core의 `DeviceVerify`(verify 계산·치환표 검사), `DeviceIdentity`(mac 정규화), `DeviceTopic`(서버 → 장치 토픽)을 그대로 쓴다. 기록기와 JSON 가림 도우미는 중계 모드와 같은 형식으로 이 프로젝트에 따로 둔다(D018).

자동 테스트는 새 테스트 프로젝트에 둔다. 가짜 다원 REST·FOTA HTTP 서버(Kestrel)와 가짜 MQTT 브로커(MQTTnet 서버, TLS)를 이 프로젝트 안에 새로 만들고, 실행기를 프로세스 안에서 돌려 확인한다.

## 바꿀 파일·구조

새 실행 프로젝트 `src/Biz.Bizadm.PowerManager.FakeDevice.App/`:

- `Biz.Bizadm.PowerManager.FakeDevice.App.csproj`: 새 파일. `Exe`이고 Core를 참조한다. 패키지는 `Biz.Bizadm.KMS`, `Biz.Bizadm.KMS.Credentials`, `Biz.Bizadm.Serilog.Enrichers.Caller`, `Biz.Bizadm.Serilog.Sinks.File.Extensions`, `Biz.Bizadm.System.Configuration.Extensions`, `CommandLineParser`, `Microsoft.Extensions.Hosting`, `MQTTnet`, `Serilog.Extensions.Hosting`, `Serilog.Sinks.Console`, `YamlDotNet`이며 모두 `Directory.Packages.props`에 이미 있다. 테스트 프로젝트에 `InternalsVisibleTo`를 준다.
- `Program.cs`: 새 파일. 진입점이다. 하는 일은 다음과 같다.
  - CommandLineParser로 `--config`·`--log`(둘 다 필수)를 받는다.
  - YAML을 역직렬화하고 `ConfigurationValidator.Validate` + `AppConfiguration.Validate`로 검사한다.
  - `ConfigSecretProtector`로 비밀 값을 암호화·복호화한다.
  - Host(Serilog Console + 일별 File `Biz.Bizadm.PowerManager.FakeDevice.App.log`, 12개 보존)를 만들고 시작한다.
  - `FakeDeviceRunner.RunAsync(ApplicationStopping)`를 직접 실행하고 결과로 종료 코드를 정한다.
  - `RunAsync(string[] args)`는 테스트에서 부를 수 있게 `internal`로 둔다.
- `ExitCode.cs`: 새 파일. `Success`=0, `InvalidArguments`=1, `ConfigurationError`=2, `RuntimeError`=3이다.
- `AppConfiguration.cs`: 새 파일. YAML 설정 모델과 검사(인터페이스·데이터 절)다.
- `Security/ConfigSecretProtector.cs`: 새 파일. OsKms로 `Register.MqttKey`·`Mqtt.Password`를 암호화(`DEKENC:`)·복호화한다. `DeviceConfig.App`의 같은 이름 클래스와 구조가 같다(D020).
- `FakeDeviceOptions.cs`: 새 파일. 검사와 복호화를 마친 실행 값이다. 기본값을 채운 ClientId·사용자명, 정규화한 mac, 장치 키, 시간 제한 값을 담는다. `ToString`(record `PrintMembers`)은 비밀 값을 `***`로 가린다.
- `FakeDeviceRunner.cs`: 새 파일. C2~C8·C11·C14~C16의 흐름을 맡는다(인터페이스·데이터 절).
- `Rest/DawonRestClient.cs`: 새 파일. `register/create`·`device/connect` 요청, 성공 판정, `verify` 판정을 한다.
- `Mqtt/DeviceMqttClient.cs`: 새 파일. MQTTnet 클라이언트의 옵션 만들기, 접속·구독·발행·수신·끊김 처리를 맡는다.
- `Mqtt/DevicePayloads.cs`: 새 파일. `rg`·`tisyn`·`n` 페이로드를 만든다(`Utf8JsonWriter`로 키 순서 고정).
- `Fota/FotaUrlFinder.cs`: 새 파일. 페이로드 JSON의 모든 문자열 값 가운데 URL 후보를 고른다.
- `Fota/FotaDownloader.cs`: 새 파일. 버전·URL·단일 GET 요청과 고정 헤더, 60초·4 MiB 제한, 파일 저장, SHA-256을 맡는다.
- `Recording/FakeDeviceRecorder.cs`: 새 파일. JSONL 기록기다. 파일 이름이 `dawon-fake-device-yyyyMMdd.jsonl`인 것만 다르고 형식은 `DawonRelayRecorder`와 같다(D018).
- `Recording/TopLevelJson.cs`: 새 파일. 최상위 `mqtt_key`·`verify` 값을 가린다. `Server.Spa`의 같은 이름 클래스 가운데 가림에 필요한 부분만 둔다(D018).
- `Console/ConsoleCommand.cs`: 새 파일. 콘솔 한 줄을 명령으로 해석한다.
- `config.example.yaml`: 새 파일. 가짜 값만 넣은 예시 설정이며 빌드 출력으로 복사한다.

새 테스트 프로젝트 `tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/`:

- `Biz.Bizadm.PowerManager.FakeDevice.App.Tests.csproj`: 새 파일. MSTest(MTP, `EnableMSTestRunner`)다. `FrameworkReference Microsoft.AspNetCore.App`(가짜 HTTP 서버용)을 두고, 패키지는 `MQTTnet`·`MQTTnet.Server`, 참조는 실행 프로젝트다.
- `Fakes/TestCertificate.cs`, `Fakes/FakeDawonHttp.cs`, `Fakes/FakeDawonBroker.cs`: 새 파일. 가짜 서버들이다(검증 전략 절).
  - `FakeDawonHttp`: REST와 FOTA를 한 서버로 받는다. 요청 기록, 응답 함수, 지연, HTTP·HTTPS 포트가 있다.
  - `FakeDawonBroker`: TLS 끝점이 있다. 접속·구독·발행 기록, 접속 거부 코드·접속 처리(CONNACK) 지연·구독 실패·SUBACK 지연 설정, 장치 토픽으로 보내기, 클라이언트 끊기를 한다.
- `TestConsole.cs`: 새 파일. 실행기에 줄을 넣는 입력(`Channel` 기반 `TextReader`)과 출력 수집기(`StringWriter`)다.
- `AppConfigurationTests.cs`, `DevicePayloadsTests.cs`, `FotaUrlFinderTests.cs`, `FotaDownloaderTests.cs`, `FakeDeviceRecorderTests.cs`, `ConsoleCommandTests.cs`, `DeviceMqttClientOptionsTests.cs`, `RunnerRestTests.cs`, `RunnerMqttTests.cs`, `RunnerFotaTests.cs`, `ProgramTests.cs`: 새 파일. 검증 전략 절의 테스트다.

기존 파일:

- `Biz.Bizadm.PowerManager.slnx`: 두 프로젝트를 `/src/`·`/tests/` 폴더에 더한다.
- `docs/features/dawon-fake-device.md`: 새 파일. 기준 spec이다(새 기능). R번호 요구, 파일 목록, 이력을 담는다.

## 결정

- **기록기와 가림 도우미는 이 프로젝트에 같은 형식으로 따로 둔다**(D018).
  - 이유: 중계 코드를 바꾸지 않아 spec 기능 범위가 그대로다.
  - 검토한 대안: Core로 옮겨 공유. dawon-relay 기능 파일을 바꾸게 되어 spec 의도 변경이 된다.
- **설정의 `Register.MqttKey`·`Mqtt.Password`는 OsKms로 암호화한다**(D020, D019 대체).
  - 비밀 값이 하나도 없으면 KMS(자격 증명·DEK 파일)를 건너뛴다. `DeviceConfig.App`의 `HasSecrets`와 같다. 지킬 값이 없을 때 KMS 초기화를 강제하지 않을 뿐, 평문 비밀 값을 받는 우회 경로는 없다.
  - 이유: 저장소 규칙 `dotnet/kms-config-secrets.md`를 따르고, 같은 저장소의 방식과 같다.
  - 검토한 대안: 평문 유지. 규칙 1·6번과 충돌한다.
- **콘솔 앱 템플릿 구조를 따른다**(규칙 `dotnet/console-app-template.md`). 실행 중 실패는 모두 종료 코드 3(`RuntimeError`)이다.
  - 실행 중 실패: 등록 실패(C2), 접속 거부·실패(C4), SUBACK 무응답(C5), 다원 쪽 끊김(C11)
  - 템플릿 2·7번과 달리 `WaitForShutdownAsync`·`BackgroundService` 대신 실행기를 직접 실행한다. 실행 결과로 종료 코드를 내기 위해서이고, `DeviceConfig.App`과 같다.
  - 검토한 대안: 실패마다 다른 코드(4 이상). 템플릿 표에 없는 코드가 생긴다. spec은 "0이 아닌 종료 코드"만 요구하고 원인은 기록·콘솔에 남는다.
- **설정 상대 경로의 기준은 `ContentRootPath`이고, 생략하면 실행 폴더(`AppContext.BaseDirectory`)다.**
  - 이유: spec C1의 기록 폴더 기본값이 "실행 폴더 아래 `dawon-fake-device`"다. OsKms DEK 파일도 같은 기준으로 둔다.
- **FOTA 요청에는 장치 펌웨어의 고정 헤더 묶음 전체를 같은 순서로 보낸다.** `User-Agent` 값만 설정으로 바꿀 수 있다.
  - 헤더 순서: `Connection: keep-alive`, `Cache-Control: no-cache`, `User-Agent`, `Accept: */*`, `Accept-Encoding: gzip,deflate`, `Accept-Language: zh-CN,eb-US;q=0.8`
  - 근거: B540 플래시 덤프 슬롯 B `0xdf6ec` 부근의 형식 문자열(이 작업에서 확인). 문자열 `eb-US`는 펌웨어에 있는 그대로다.
  - 이유: spec C16은 `Connection`·`User-Agent`를 요구한다. 다원이 다른 헤더로 거를 가능성까지 장치와 같게 맞춘다.
  - 헤더는 이 순서로 요청 메시지에 넣는다. 자동 테스트는 만든 요청 메시지(`HttpRequestMessage`)의 헤더 순서까지만 본다. HTTP 처리기가 실제 회선에 쓰는 순서는 확인하지 않는다. 가짜 서버(Kestrel)는 받은 헤더의 순서를 보존하지 않기 때문이다.
  - 자동 압축 해제와 자동 리디렉션은 끈다. 받은 바이트를 그대로 저장하고 `Content-Encoding`·`Location`을 기록한다. ESP8266 SDK `upgrade_lib`도 둘 다 하지 않는 것으로 보기 때문이다.
- **`User-Agent` 기본값은 덤프의 전체 문자열이다**(D013).
  - 값: `Mozilla/5.0 (Windows NT 5.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/30.0.1599.101 Safari/537.36`
  - 근거: 슬롯 A `0x5f594`, 슬롯 B `0xdf6ec`
  - 펌웨어 문자열 끝의 공백 하나는 HTTP 헤더 값 앞뒤 공백 규칙에 따라 빼고 둔다.
- **시간 제한(REST·MQTT 10초, FOTA 60초)과 FOTA 크기 상한(4 MiB)은 YAML 설정에 두지 않는다.** `FakeDeviceOptions`의 값으로 두고 테스트에서만 줄인다.
  - 이유: spec은 고정값을 정한다. 테스트가 10초·60초를 기다리지 않게 하려는 것이다.
- **주기 발행은 `PeriodicTimer`(`TimeProvider` 주입)로 한다.** 테스트는 1초 주기로 실제 시간을 쓴다. 새 패키지(`Microsoft.Extensions.TimeProvider.Testing`)를 더하지 않기 위해서다.
- **콘솔 입력이 끝나면(EOF) 명령만 더 받지 않고 실행은 계속한다.** 입력을 돌려 실행한 경우에도 Ctrl+C까지 수신·기록을 이어 가려는 것이다.
- **Will은 MQTTnet 옵션 객체(`MqttClientOptions.WillTopic` 등)로 단위 테스트한다.** MQTTnet 5 브로커 이벤트는 Will을 공개 속성으로 주지 않기 때문이다. 중계 모드는 비공개 필드를 읽었다.

## 인터페이스·데이터

### 명령줄

```
Biz.Bizadm.PowerManager.FakeDevice.App --config <설정 YAML> --log <로그 폴더>
```

### 설정 YAML(키는 속성 이름 그대로, PascalCase)

```yaml
ContentRootPath: ""            # 생략하면 실행 폴더. 상대 경로의 기준
RecordDirectory: dawon-fake-device
OsKms:                         # Register.MqttKey 또는 Mqtt.Password가 있을 때만 필수
  CredentialService: ...       # 절대 URI
  CredentialAccount: ...
  Salt: ""                     # 첫 기동 때 생성·기록
  Iterations: 600000
  SecretsDekFilePath: ...
Dawon:
  ApiServerHost: dwapi.dawonai.com
  ApiServerPort: 18443
  MqttServerHost: dwmqtt.dawonai.com
  MqttServerPort: 8883
  SkipCertificateValidation: false   # REST·MQTT·FOTA
Device:
  Mac: 000000000000           # 필수, 12자리 16진수
  Model: B540_W
  Company: DAWONDNS
  UserId: ""
  Lati: "0"
  Long: "0"
  FirmwareVersion: 1.01.36
Register:
  MqttKey: ""                  # 있으면 등록 생략. 기동 때 암호화
  VerifyServerAddr: ""         # 생략하면 Dawon.MqttServerHost. 치환표 문자만
Mqtt:
  ClientId: ""                 # 생략하면 {Company}-{mac}
  UserName: ""                 # 생략하면 {Company}-{Model}-{mac}
  Password: ""                 # 생략하면 mqtt_key. 기동 때 암호화
  KeepAliveSeconds: 60
  TopicPrefix: dwd
  WillTopic: /lwt
  WillPayload: offline
Publish:
  SendRegister: true
  SendTimeSync: true
  RegisterSuffix: json
  QoS: 0
  TimeSyncIntervalSeconds: 0
  NotifyIntervalSeconds: 0
  NotifyElements:              # n·sv 쌍
    - N: /100/0/31
      Sv: "false"
Fota:
  AutoDownload: true
  UserAgent: "Mozilla/5.0 (Windows NT 5.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/30.0.1599.101 Safari/537.36"
```

검사 항목(실패하면 표준 오류에 원인을 쓰고 종료 코드 2로 끝나며, 네트워크에 연결하지 않는다):

- 형식
  - `Device.Mac`: `DeviceIdentity.TryNormalizeMac`
  - 포트: 1~65535
  - `Publish.QoS`: 0~2
  - 주기: 0 이상
  - `Mqtt.KeepAliveSeconds`: 0~65535
- 값의 내용
  - `Register.VerifyServerAddr`(기본값을 채운 뒤): `DeviceVerify.IsValidServerAddr`
  - `Mqtt.TopicPrefix`, `Publish.RegisterSuffix`, `Mqtt.WillTopic`: 비어 있지 않고 MQTT 와일드카드(`+`·`#`)가 없음
  - `Fota.UserAgent`: 비어 있지 않고 CR·LF가 없음
  - `NotifyElements[].N`: 비어 있지 않음
- 조건부: `OsKms`는 비밀 값이 있을 때 필수이고, `CredentialService`는 절대 URI여야 함
- 설정 파일 읽기 실패와 YAML 역직렬화 실패도 같은 처리다.
- 종료 코드 1은 명령줄 인자 오류에만 쓴다.

### 실행기 흐름과 종료

| 단계 | 성공 | 실패 처리 |
|---|---|---|
| 등록(C2) | `mqtt_key` 확정, `verify` 판정 기록 | 기록·콘솔 후 3 |
| 접속 조회(C3) | 기록 | 기록 후 계속 |
| MQTT 접속(C4) | CONNACK 0 | CONNACK 코드(3.1 값, 중계와 같은 변환) 또는 오류 기록·콘솔 후 3 |
| 구독(C5) | SUBACK 기록 | 10초 무응답: 기록 후 접속을 끊고 3. 실패 값: 기록·콘솔 후 계속 |
| 정해진 발행(C6) | `rg` → `tisyn` → `n`(주기 > 0일 때) | 발행 예외는 기록·콘솔 후 계속 |
| 실행 중 | 주기 발행, 콘솔 명령, 수신 처리, FOTA 받기 | — |
| 종료(C11) | 종료 명령·Ctrl+C: 진행 중 FOTA 받기 취소(오류 `cancelled` 기록), DISCONNECT, 종료 항목(`clean: true`), 0 | 다원 쪽 끊김: 종료 항목(`clean: false`), 3 |

- MQTT 접속 옵션
  - `WithProtocolVersion(V310)`, `WithCleanSession(true)`, keepalive, `WithTimeout(10초)`
  - TLS: `UseTls`, `TargetHost`=브로커 호스트, `SslProtocols.Tls12`, 검증 끄기 설정이면 검증 처리기 `_ => true`
  - 자격 증명: 사용자명, 비밀번호 UTF-8 바이트
  - Will: 설정 토픽·페이로드, QoS 0, retain 없음
- 받은 메시지 처리는 이벤트 처리기에서 기록·콘솔·FOTA 후보 찾기까지만 한다. 받기는 따로 띄운 작업(`Task.Run`)에서 해서 MQTT 처리를 막지 않는다.

### 콘솔 명령(한 줄, 앞뒤 공백 무시, 명령어는 대소문자 무시)

| 명령 | 동작 |
|---|---|
| `pub <토픽> <페이로드>` | 토픽 그대로 발행(QoS 설정값, retain 없음). 페이로드는 토픽 뒤 첫 공백 다음의 나머지 줄 전체 |
| `up <접미> <페이로드>` | `{접두}.v1/{장치 키}/iot-server/{접미}`로 발행 |
| `fota <URL>` | C14의 두 요청(버전·URL) |
| `get <URL>` | URL 하나만 `GET` |
| `quit` / `exit` | C11 정상 종료 |
| `help` | 사용법 |
| 그 밖·인자 모자람 | 사용법을 쓰고 무시 |

URL 인자는 `http`·`https` 절대 URI여야 한다. 아니면 사용법을 쓴다.

### 페이로드

- `rg`: `{"sid":"0","msg":{"o":"rg","e":[{"n":"/3/0/0","sv":"<회사>"},{"n":"/3/0/1","sv":"<모델>"},{"n":"/3/0/2","sv":"<mac>"},{"n":"/3/0/3","sv":"<펌웨어>"},{"n":"/3/0/997","sv":"1"},{"n":"/3/0/998","sv":"1.0"},{"n":"/3/0/999","sv":"1"}]}}`
- `tisyn`: `{"sid":"0","msg":{"o":"tisyn","e":[]}}`
- `n`: `{"sid":"1","msg":{"o":"n","e":[{"n":"<N>","sv":"<Sv>","ti":""},...]}}`

공백 없는 한 줄 JSON이다. 실장비는 줄바꿈을 넣지만, spec이 형식을 위처럼 정했고 MQTT 처리 쪽은 JSON 파서라 공백은 의미가 없다.

### FOTA

- URL 후보
  - 페이로드를 `JsonDocument`로 읽어 객체·배열을 모두 돈다.
  - 문자열 값 가운데 `http://`·`https://`(대소문자 무시)로 시작하고 `Uri.TryCreate(Absolute)`가 되는 값을 나온 순서대로 고른다.
  - JSON이 아니면 후보가 없다.
  - 같은 실행에서 이미 자동으로 받은 URL(문자열 그대로 비교)은 건너뛴다.
- 요청
  - 버전 요청: URL 끝의 `/`를 모두 지우고 `/version`을 붙인다.
  - URL 요청과 단일 GET: URL 그대로 보낸다.
  - 요청마다 고정 헤더 묶음을 보낸다(결정 절).
  - 두 요청은 차례로 보내며, 버전 결과와 관계없이 둘째를 보낸다.
- 받기
  - 요청마다 60초 제한이다.
  - `Content-Length`가 4 MiB를 넘거나 읽은 바이트가 넘으면 그 요청을 멈춘다.
  - 200이면 `fota/<UTC yyyyMMddTHHmmssfffZ>-<순번 4자리>-<version|url|get>.bin`으로 저장한다. `.part`에 쓴 뒤 이름을 바꾸고, 실패하면 `.part`를 지운다.
  - 200이 아니면 저장하지 않는다.

### 기록 항목(공통 `ts`·`channel`·`direction`·`kind`, 바이트 값은 `…`·`…Encoding`(`utf8`·`base64`))

| channel | direction | kind | 필드 |
|---|---|---|---|
| `rest` | `device-to-dawon` | `request` | `method`·`path`·`body` |
| `rest` | `dawon-to-device` | `response` | `method`·`path`·`status`·`body`(최상위 `mqtt_key`·`verify` 가림) |
| `rest` | `dawon-to-device` | `error` | `method`·`path`·`target`·`error` |
| `rest` | `dawon-to-device` | `verify` | `result`(`match`·`mismatch`·`uncomputable`) |
| `mqtt` | `device-to-dawon` | `connect` | `clientId`·`userName`·`hasPassword`·`keepAlive`·`cleanSession`·`will`(`topic`·`payload`·`qos`·`retain`) |
| `mqtt` | `dawon-to-device` | `connack` | `clientId`·`returnCode` |
| `mqtt` | `device-to-dawon` | `subscribe` | `clientId`·`filters`(`topic`·`qos`) |
| `mqtt` | `dawon-to-device` | `suback` | `clientId`·`results`(받은 값) |
| `mqtt` | 양방향 | `publish` | `clientId`·`topic`·`qos`·`retain`·`payload` |
| `mqtt` | 끝낸 쪽 | `disconnect` | `clientId`·`clean` |
| `mqtt` | `dawon-to-device` | `error` | `clientId`·`target`·`error`(`timeout`, 예외 메시지 등) |
| `fota` | `dawon-to-device` | `candidate` | `topic`·`url` |
| `fota` | `device-to-dawon` | `request` | `id`·`purpose`(`version`·`url`·`get`)·`url` |
| `fota` | `dawon-to-device` | `response` | `id`·`url`·`status`·`contentType`·`contentLength`·`contentEncoding`·`location`·`bytes`·`sha256`·`file`(200일 때)·`body`(버전 요청일 때만) |
| `fota` | `dawon-to-device` | `error` | `id`·`url`·`error`(`timeout`·`too-large`·`cancelled`·연결 오류) |

비밀 값(C10)의 처리:

- `mqtt_key`·`verify`·비밀번호 값은 기록·콘솔·로그 어디에도 쓰지 않는다.
- 콘솔에는 REST 상태 코드와 판정만 쓰고, 받은 MQTT 메시지는 토픽과 페이로드를 쓴다.
- `ConfigSecretProtector`가 설정 파일에 다시 쓰는 값은 암호문뿐이다.

## 오류·위험

- **실장비와 ClientId가 겹침:** 실제 장비 mac을 쓰면 다원 쪽에서 같은 ClientId의 실장비 접속이 밀려난다. C13 실행 동안 실장비를 꺼 두거나, 다원 계정에서 지운 mac을 쓴다. 작업 문서 C13 절차에 적는다.
- **MQTTnet 5의 CONNACK 거부 처리:** 거부를 결과 코드로 돌려줄 수도, 예외로 던질 수도 있다. 중계는 결과 코드로 받아 처리했고, 2026-11-20 실기록에서 CONNACK 4가 결과로 남았다. 예외(`MqttConnectingFailedException`)도 잡아 그 안의 결과 코드를 쓰고, 없으면 오류 항목으로 남긴다. 둘 다 테스트한다(가짜 브로커 거부).
- **SUBACK 실패 값의 표현:** MQTT 3.1 SUBACK 실패 값(0x80)이 MQTTnet 결과 코드로 어떻게 바뀌는지에 따라 판정이 달라진다. 허용 QoS(0~2)가 아닌 모든 값을 실패로 보고, 기록에는 받은 정수 값을 남긴다.
- **FOTA URL이 다원 밖 주소를 가리킴:** 다원이 보낸 값만 받고, 4 MiB·60초로 제한하며, 실행하지 않는다. 자동 받기는 설정으로 끌 수 있다.
- **콘솔 입력과 로그 출력이 섞임:** 표시 문제일 뿐 기록 파일에는 영향이 없다.
- **OsKms 첫 기동:** Salt를 만들 때 OS 자격 증명 금고에 쓴다. `DeviceConfig.App`과 같은 동작이며, Salt가 비었는데 DEK 파일이 있으면 기존 KEK를 덮어쓰지 않고 실패(종료 코드 2)한다.
- **덤프 기반 기본값:** 기본값(`User-Agent`, 헤더 순서)은 슬롯 B 정적 문자열에서 왔다. 실장비 펌웨어 `1.01.36`과 다를 수 있다. 설정으로 바꿀 수 있는 것은 `User-Agent`뿐이다.

## 검증 전략

자동 테스트는 모두 `tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests`에 둔다. 실행기 테스트는 가짜 서버와 줄인 시간 제한으로 프로세스 안에서 돈다. 가짜 서버는 무작위 포트, TLS 시험 인증서, `SkipCertificateValidation: true`를 쓴다.

| 조건 | 확인 방법 |
|---|---|
| C1 | `AppConfigurationTests`: 기본값과 형식 검사마다의 실패를 본다. 검사 대상은 mac·포트·QoS·주기·keepalive, 치환표 밖 `VerifyServerAddr`, 와일드카드 토픽, CR·LF 든 `UserAgent`, 비밀 값이 있는데 `OsKms`가 없는 경우다. `ProgramTests`: 없는 설정 파일, 깨진 YAML, 틀린 mac에서 `Program.RunAsync`가 2를 돌려주고 표준 오류에 원인이 쓰이며, 가짜 서버에 요청이 오지 않는다. |
| C2 | `RunnerRestTests`: 요청 경로·헤더(`Content-Type`·`X-HIT-Version`)·본문이 맞는지 본다. 성공했을 때 `verify` 판정 세 가지(같음·다름·치환표 밖 키 = 계산 불가)가 기록·콘솔에 남는지 본다. 409·JSON 아님·`mqtt_key` 없음·빈 값·연결 실패·시간 초과(지연 응답)에서 MQTT에 접속하지 않고 3을 돌려주는지 본다. 설정 키가 있으면 등록 요청을 보내지 않는지도 본다. |
| C3 | `RunnerRestTests`: `GET /api/v1/device/connect/{회사}-{모델}-{mac}`이 기록된다. 404·시간 초과여도 MQTT 접속까지 간다. 설정 키를 준 뒤 닫힌 REST 포트(연결 실패)로 실행해도 `error`를 기록하고 MQTT 접속까지 간다. |
| C4 | `RunnerMqttTests`: 가짜 브로커에 남은 접속 값을 본다. 프로토콜 3.1, ClientId·사용자명·비밀번호 기본값과 설정값, keepalive, clean session이다. 거부 코드 4·5에서 `connack` 기록과 3을 확인하고, 연결 실패(닫힌 포트)와 CONNACK 지연(줄인 제한, 시간 초과)에서 `error` 기록과 3을 확인한다. `DeviceMqttClientOptionsTests`: Will 토픽·페이로드·QoS 0·retain 없음, TLS 1.2, 대상 호스트를 확인한다. |
| C5 | `RunnerMqttTests`: 구독 필터·QoS 0과 `subscribe`·`suback` 기록을 본다. 구독 실패 값이면 기록하고 이어서 발행하는지, SUBACK 지연이면 3을 돌려주는지 본다. |
| C6 | `DevicePayloadsTests`: 세 페이로드의 정확한 문자열을 본다. `RunnerMqttTests`: 브로커가 받은 순서(`rg` → `tisyn` → `n`), 토픽, QoS, retain 없음을 본다. 발행 끄기 설정을 확인한다. 주기 1초에서 `tisyn`·`n`이 2.5초 안에 두 번 이상 오는지 본다. |
| C7 | `ConsoleCommandTests`: 명령 해석 표 전체를 본다. `RunnerMqttTests`: `pub`·`up`가 발행되는지, 알 수 없는 명령에 사용법이 쓰이는지, `quit`이 C11로 가는지 본다. |
| C8 | `RunnerMqttTests`: 브로커가 장치 토픽으로 보낸 메시지가 `publish`(`dawon-to-device`)로 기록되고 콘솔에 쓰인다. 기록될 때까지 기다린 뒤 1초 동안, 클라이언트가 정해진 발행 밖의 메시지를 보내지 않는지 본다. |
| C9 | `FakeDeviceRecorderTests`: 파일 이름, 공통 필드, UTF-8·Base64 판정, 자르지 않음, 동시 기록 200줄이 모두 올바른 JSON인지 본다. 실행기 테스트에서 표의 종류가 모두 나오는지도 본다. |
| C10 | `RunnerRestTests`·`RunnerMqttTests`: 알려진 시험 키·비밀번호·`verify` 문자열이 기록 파일, 콘솔 출력, 표준 오류에 없는지 찾아본다. `FakeDeviceOptions.ToString`도 본다. |
| C11 | `RunnerMqttTests`: `quit`과 정지 토큰 취소(Ctrl+C와 같은 경로)에서 브로커에 정상 DISCONNECT가 남는지, `disconnect`(`clean: true`)와 0을 본다. 브로커가 클라이언트를 끊으면 `clean: false`와 3이다. |
| C14 | `RunnerFotaTests`: 장치 토픽 메시지 안의 URL(중첩 객체·배열 안 포함)로 `/version`과 URL 요청이 차례로 오는지, 고정 헤더가 모두 있고 값(`User-Agent` 포함)이 맞는지 본다. `FotaDownloaderTests`: 만든 요청 메시지의 헤더 순서를 본다. 같은 URL을 두 번 보내도 자동은 한 번이다. JSON이 아니거나 나뉜 값(호스트·포트만)이면 요청이 없다. 자동 받기를 끄면 요청이 없다. `FotaUrlFinderTests`: 후보 고르기를 본다. |
| C15 | `RunnerFotaTests`: `fota <URL>`은 두 요청, `get <URL>`은 한 요청이고, 같은 URL을 다시 받는다. |
| C16 | `RunnerFotaTests`: 200 저장 파일 내용과 SHA-256·바이트 수 기록을 본다. 버전 404일 때 URL 요청이 오는지, 지연(줄인 제한) 때 `timeout`이 남는지, 닫힌 포트(연결 실패) 때 연결 오류가 기록되고 실행이 계속되는지 본다. 큰 본문(줄인 상한)이면 `too-large`가 남고 파일·`.part`가 없다. HTTP와 HTTPS를 모두 받는다. 느린 받기 도중 콘솔 `up`이 발행되는지 본다. |
| C12 | `dotnet build`(경고 0)와 `dotnet test` 전체가 통과한다. 빌드한 exe로 `--help`(0), 인자 없음(1), 틀린 설정(2)을 확인한다(템플릿 규칙 9번). |
| C13 | 수동이다. 사용자가 실장비 정보를 확인한 뒤 실제 다원으로 실행하고, spec C13 항목을 작업 문서에 적는다. 접속에 성공하면 사용자가 다원 앱에서 켜기·끄기와 펌웨어 업데이트를 조작한다. `FirmwareVersion: 1.01.30` 실행도 넣는다. 기록 파일·받은 펌웨어는 저장소에 넣지 않는다. |

## 원칙 점검

- 적용 규칙: `workflow/spec-driven.md`, `git/github-issue.md`(#24), `workflow/repository-content.md`, `dotnet/console-app-template.md`, `dotnet/project-creation.md`, `dotnet/kms-config-secrets.md`, `dotnet/nuget-package-selection.md`, 전역 규칙(응답 언어, 포터블 Python은 쓰지 않음)
- 충돌: `console-app-template` 2·6·7번과 다르다. 템플릿은 `StartAsync` 뒤 `WaitForShutdownAsync`로 기다리고 `BackgroundService` 기본 동작을 두며, 종료 코드 3을 호스트 생성·실행 예외에 쓴다. 이 계획은 실행기를 직접 실행하고, 실행기 결과(등록 실패·접속 거부·SUBACK 무응답·다원 쪽 끊김)로 3을 낸다.
  - 따르지 않는 이유: spec C2·C4·C5·C11이 실행 결과로 0이 아닌 종료 코드를 요구한다. 같은 저장소 `DeviceConfig.App`도 같은 구조다(`Program.cs` 주석 "설계 9절").
  - 그 밖의 규칙과는 충돌이 없다.
- 규칙별 점검:
  - `console-app-template`: 위 차이 말고는 템플릿 구조(Generic Host, `--config`·`--log`, YAML 검증, Serilog, `ExitCode`)를 따른다. 프로젝트 이름은 이 솔루션의 기존 방식(`DeviceConfig.App`)대로 역할을 붙인 `FakeDevice.App`이다. 템플릿의 `{Solution}.App`은 이미 다른 도구가 많은 솔루션에서 구분하려고 앞에 역할 이름을 둔 것이다.
  - `kms-config-secrets`: OsKms를 쓴다(D020). 비밀 값이 없을 때 KMS를 건너뛰는 것은 지킬 값이 없어서이며, 평문 비밀 값을 받는 경로는 없다.
  - `nuget-package-selection`: 새 패키지 없이 `Directory.Packages.props`에 있는 버전만 쓴다.
  - `repository-content`: 예시 설정에는 가짜 mac·빈 계정만 넣는다. 덤프 파일 경로·실장비 값·기록 파일은 저장소에 넣지 않는다. 문서에는 덤프 안 오프셋만 적는다.
