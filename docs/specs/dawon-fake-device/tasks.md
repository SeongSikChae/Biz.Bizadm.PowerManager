# 다원 가짜 장비 클라이언트 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

경로 줄임: `App/` = `src/Biz.Bizadm.PowerManager.FakeDevice.App/`, `Tests/` = `tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/`

## 단계 1: 프로젝트와 설정

- [x] T001 실행 프로젝트 파일을 만든다. `Exe`이고 Core를 참조한다. plan의 패키지 11개(버전은 `Directory.Packages.props`)와 테스트 프로젝트 `InternalsVisibleTo`를 두고, `config.example.yaml`을 출력으로 복사한다 (App/Biz.Bizadm.PowerManager.FakeDevice.App.csproj) — C1
- [x] T002 [P] 테스트 프로젝트 파일을 만든다. MSTest(`EnableMSTestRunner`, `OutputType Exe`), `FrameworkReference Microsoft.AspNetCore.App`, `MQTTnet`·`MQTTnet.Server`·`MSTest`, 실행 프로젝트 참조 (Tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests.csproj) — C12
- [x] T003 두 프로젝트를 `/src/`·`/tests/` 폴더에 더한다 (Biz.Bizadm.PowerManager.slnx) — C12
- [x] T004 `AppConfiguration`을 만든다 (App/AppConfiguration.cs) — C1
  - 구성: plan 인터페이스 절의 섹션·키·기본값과 `[Property]`
  - `Validate()`: plan 검사 항목 전부. 대상은 mac·포트·QoS·주기·keepalive, 기본값을 채운 `VerifyServerAddr`의 치환표 검사, 토픽 접두·접미·Will의 빈 값·와일드카드, `UserAgent`의 CR·LF, `NotifyElements[].N`, 비밀 값이 있을 때 `OsKms` 필수와 `CredentialService` 절대 URI다.
- [x] T005 `FakeDeviceOptions`를 만든다 (App/FakeDeviceOptions.cs) — C1, C10
  - 담는 값: 검사·복호화를 마친 실행 값. 정규화 mac, ClientId·사용자명·비밀번호(기본 `mqtt_key`)·`VerifyServerAddr` 기본값, 장치 키 `{회사}-{모델}-{mac}`, T007이 넘겨주는 기준 경로로 상대 경로를 푼 기록 폴더
  - 시간 제한: REST·MQTT 10초, FOTA 60초, 크기 상한 4 MiB를 기본값으로 두고 테스트에서 바꿀 수 있게 한다.
  - `PrintMembers`는 `MqttKey`·`Password`를 `***`로 가린다.
- [x] T006 `ConfigSecretProtector`를 만든다 (App/Security/ConfigSecretProtector.cs) — C1, C10
  - `DeviceConfig.App` 같은 이름 클래스와 구조를 맞춘다. DEK 파일 경로는 T007이 넘겨주는 기준 경로 기준이다(설정 모델의 `ContentRootPath`를 그대로 쓰지 않는다).
  - `HasSecrets`: `Register.MqttKey`·`Mqtt.Password` 가운데 하나라도 비어 있지 않으면 참이다.
  - 첫 기동 Salt 생성, Salt가 비었는데 DEK 파일이 있으면 실패, 평문이면 암호화하고 `Rewrite`, 복호화 값 반환까지 같다.
- [x] T007 `ExitCode`(0/1/2/3)와 `Program`을 만든다 (App/ExitCode.cs, App/Program.cs) — C1, C11
  - 인자: `--config`·`--log`(필수). 인자 오류는 1, `--help`·`--version`은 0이다.
  - `internal static Task<int> RunAsync(string[] args)`의 흐름:
    1. 설정 파일을 읽고 YAML을 역직렬화한다. 검사 전에 기준 경로를 정한다: `ContentRootPath`가 비어 있으면 `AppContext.BaseDirectory`, 아니면 그 값이다. 설정 모델의 `ContentRootPath`는 바꾸지 않고, 이 기준 경로를 KMS DEK 경로·Host·기록 폴더에 넘긴다. 그다음 검사한다. 실패하면 표준 오류에 쓰고 2를 낸다.
    2. `ConfigSecretProtector`를 적용하고, `Rewrite`이면 설정 파일을 다시 쓴다. 실패하면 2다.
    3. Host를 만든다. Serilog는 Console과 일별 File `Biz.Bizadm.PowerManager.FakeDevice.App.log`, 12개 보존, `Enrich.WithCaller()`다. 만들지 못하면 3이다.
    4. `StartAsync` 뒤 `FakeDeviceRunner.RunAsync(ApplicationStopping)`의 결과가 성공이면 0, 실패면 3이다. 실행 중 예외는 Critical 로그를 남기고 3이다.
  - 이 단계에서는 실행기 호출 자리를 두고, 실행기는 T018에서 만든다.
- [x] T008 [P] 가짜 값만 넣은 예시 설정을 만든다. mac `000000000000`, 빈 `UserId`·`MqttKey`·`Password`, `OsKms`는 주석으로 안내한다 (App/config.example.yaml) — C1
- [x] T009 `AppConfigurationTests`와 `ProgramTests`를 쓴다 (Tests/AppConfigurationTests.cs, Tests/ProgramTests.cs) — C1, C10
  - `AppConfigurationTests`: 기본값과 T004 검사 항목마다 실패 한 건씩
  - `ProgramTests`
    - 없는 설정 파일, 깨진 YAML, 틀린 mac, 치환표 밖 `VerifyServerAddr`에서 `RunAsync`가 2를 내고 표준 오류에 원인이 쓰인다. 무작위 포트에 둔 수신기(`TcpListener`)로 연결이 오지 않는지도 본다.
    - 비밀 값(`MqttKey`·`Password`)이 든 설정에서 다른 항목이 틀렸을 때, 표준 오류에 비밀 값이 없다.
    - `ContentRootPath`를 생략하면 기준이 실행 폴더(`AppContext.BaseDirectory`)다.
    - 설정 파일 다시 쓰기 시험은 실행기가 있어야 끝까지 돌므로 T023에서 쓴다.

체크포인트: `dotnet build`가 경고 없이 끝나고 `AppConfigurationTests`·`ProgramTests`가 통과한다.

## 단계 2: 기록·페이로드·명령·URL 후보

- [x] T010 [P] `TopLevelJson`을 만든다. `Server.Spa`의 같은 이름 클래스 가운데 `TryFindValue`·`Replace`·`Mask`만 둔다 (App/Recording/TopLevelJson.cs) — C10
- [x] T011 `FakeDeviceRecorder`를 만든다 (App/Recording/FakeDeviceRecorder.cs) — C9, C10
  - 파일: `dawon-fake-device-yyyyMMdd.jsonl`(UTC)
  - 형식: 공통 필드, `SemaphoreSlim`으로 한 줄씩 쓰기, `WriteBytes`(UTF-8·Base64)
  - plan 기록 항목 표의 종류마다 쓰는 메서드를 둔다.
  - REST 응답 본문은 최상위 `mqtt_key`·`verify`를 가리고, 접속 항목에는 비밀번호 유무만 남긴다.
  - 쓰기 실패는 로그만 남기고 계속한다.
- [x] T012 [P] `DevicePayloads`를 만든다. `rg`·`tisyn`·`n` 페이로드를 plan의 정확한 한 줄 JSON으로 만든다(`Utf8JsonWriter`, 키 순서 고정) (App/Mqtt/DevicePayloads.cs) — C6
- [x] T013 [P] `ConsoleCommand`를 만든다 (App/Console/ConsoleCommand.cs) — C7, C15
  - plan 콘솔 명령 표대로 해석한다: `pub`·`up`·`fota`·`get`·`quit`/`exit`·`help`
  - 앞뒤 공백을 무시하고 명령어는 대소문자를 무시한다. 페이로드는 나머지 줄 전체다.
  - URL은 `http`·`https` 절대 URI만 받는다. 알 수 없는 명령이나 인자가 모자라면 사용법 결과를 낸다.
- [x] T014 [P] `FotaUrlFinder`를 만든다. `JsonDocument`로 객체·배열 안까지 모든 문자열 값을 돌며, `http://`·`https://`(대소문자 무시)로 시작하고 절대 URI인 값을 나온 순서대로 고른다. JSON이 아니면 빈 목록이다 (App/Fota/FotaUrlFinder.cs) — C14
- [x] T015 단위 테스트를 쓴다 (Tests/FakeDeviceRecorderTests.cs, Tests/DevicePayloadsTests.cs, Tests/ConsoleCommandTests.cs, Tests/FotaUrlFinderTests.cs) — C6, C7, C9, C10, C14
  - `FakeDeviceRecorderTests`: 파일 이름, 공통 필드, UTF-8·Base64, 큰 본문을 자르지 않음, 동시 기록 200줄이 모두 올바른 JSON, `mqtt_key`·`verify` 가림
  - `DevicePayloadsTests`: 세 페이로드의 정확한 문자열
  - `ConsoleCommandTests`: 명령 표 전체
  - `FotaUrlFinderTests`: 중첩 객체·배열, 대소문자, 절대 URI 아님, JSON 아님, 나뉜 값(호스트·포트만)

체크포인트: 단계 2 테스트가 통과한다.

## 단계 3: 테스트용 가짜 서버

- [x] T016 가짜 서버와 시험 콘솔을 만든다 (Tests/Fakes/TestCertificate.cs, Tests/Fakes/FakeDawonHttp.cs, Tests/Fakes/FakeDawonBroker.cs, Tests/TestConsole.cs) — C12
  - `TestCertificate`: localhost 자체 서명 인증서
  - `FakeDawonHttp`(Kestrel)
    - HTTPS·HTTP 포트를 무작위로 연다.
    - 받은 요청(메서드·경로·헤더·본문)을 차례로 기록한다.
    - 응답 함수(상태·`Content-Type`·본문·지연·청크 본문)와 느린 본문 보내기를 설정할 수 있다.
  - `FakeDawonBroker`(MQTTnet 서버, TLS 끝점)
    - 기록: 접속(프로토콜 버전·ClientId·사용자명·비밀번호·keepalive·clean session), 구독(필터·QoS), 발행(ClientId·토픽·QoS·retain·페이로드), 끊김 종류
    - 설정: 접속 거부 코드, 접속 처리(CONNACK) 지연, 구독 결과 코드, 구독 처리 지연
    - 동작: 장치 토픽으로 보내기, 클라이언트 끊기
  - `TestConsole`: `Channel` 기반 입력 `TextReader`(줄 넣기·입력 끝 알리기)와 출력 수집 `StringWriter`

체크포인트: 빌드되고, 가짜 서버가 시작·종료되는 단순 테스트가 통과한다(T019의 첫 테스트로 대신해도 된다).

## 단계 4: REST

- [x] T017 `DawonRestClient`를 만든다 (App/Rest/DawonRestClient.cs) — C2, C3, C10
  - `register/create` 요청(HTTPS, 헤더 `Content-Type: application/json`·`X-HIT-Version: 1.0`, spec C2 본문)과 성공 판정
  - `verify` 판정: `DeviceVerify.Compute`가 같음·다름·`ArgumentException`(계산 불가)
  - `device/connect/{회사}-{모델}-{mac}` 요청
  - 시간 제한: `FakeDeviceOptions`의 REST 값
  - 인증서 검증 끄기 설정을 적용한다.
  - 요청·응답·오류·판정을 기록하고, 콘솔에는 상태 코드·판정만 쓴다.
- [x] T018 `FakeDeviceRunner`를 만들고 REST 단계를 넣는다 (App/FakeDeviceRunner.cs, App/Program.cs) — C2, C3
  - 생성자: 옵션, 기록기, 입력 `TextReader`, 출력 `TextWriter`, `TimeProvider`, 로거
  - 흐름: 설정 키가 없을 때만 등록하고, 실패면 실패 결과를 낸다. 접속 조회는 결과와 관계없이 계속한다.
  - `Program`에 실행기를 등록하고 호출한다.
- [x] T019 `RunnerRestTests`를 쓴다 (Tests/RunnerRestTests.cs) — C2, C3, C10
  - 요청 경로·헤더·본문
  - `verify` 판정 세 가지가 기록·콘솔에 남는지
  - 409·JSON 아님·`mqtt_key` 없음·빈 값·연결 실패·지연(줄인 제한)에서 실패 결과를 내고, MQTT 접속이 없는지(가짜 브로커 접속 기록 없음)
  - 설정 키가 있으면 등록 요청이 없는지
  - `device/connect` 경로, 404·지연이어도 다음 단계로 가는지
  - 설정 키를 준 뒤 닫힌 REST 포트(연결 실패)로 실행해도 `error`를 기록하고 MQTT 접속까지 가는지
  - 시험 키·`verify` 문자열이 기록·콘솔 출력에 없는지
  - `FakeDeviceOptions.ToString`이 키·비밀번호 값을 내지 않는지

체크포인트: `RunnerRestTests`가 통과한다.

## 단계 5: MQTT

- [x] T020 `DeviceMqttClient`를 만든다 (App/Mqtt/DeviceMqttClient.cs) — C4, C5, C8, C11
  - 옵션 만들기(정적, 테스트용 공개): V310, clean session, keepalive, 10초 제한, TLS 1.2·대상 호스트·검증 끄기, 사용자명·비밀번호 UTF-8, Will(QoS 0, retain 없음)
  - 접속: 결과 코드를 3.1 값으로 바꾼다(중계와 같은 표). `MqttConnectingFailedException`이면 그 결과 코드를 쓰고, 결과가 없거나 그 밖의 예외·시간 초과면 오류로 본다.
  - 구독: 10초 제한. 허용 QoS(0~2)가 아닌 결과는 실패로 보고, 받은 정수 값을 돌려준다.
  - 발행
  - 받은 메시지·끊김 이벤트(스스로 끊었는지 구분)를 실행기에 넘긴다.
- [x] T021 실행기에 MQTT 단계를 넣는다 (App/FakeDeviceRunner.cs) — C4, C5, C6, C7, C8, C11
  - 접속·구독: 접속(거부·실패면 실패 결과), 구독(무응답이면 끊고 실패 결과, 실패 값이면 기록·콘솔 후 계속)
  - 정해진 발행: `rg` → `tisyn` → `n`. 주기 발행은 `PeriodicTimer`(`TimeProvider`)로 하고, 발행 예외는 기록·콘솔 후 계속한다.
  - 콘솔 명령 루프: `pub`·`up`·`quit`·`help`·사용법. EOF면 명령만 멈춘다.
  - 수신 처리: 기록·콘솔. 응답하지 않는다.
  - 종료
    - 명령·정지 토큰: DISCONNECT, `disconnect`(`clean: true`), 성공 결과
    - 다원 쪽 끊김: `disconnect`(`clean: false`), 실패 결과
  - MQTT 접속·구독·발행·종료를 모두 기록한다.
- [x] T022 [P] `DeviceMqttClientOptionsTests`를 쓴다. Will 토픽·페이로드·QoS 0·retain 없음, 프로토콜 V310, TLS 1.2·대상 호스트, 자격 증명을 본다 (Tests/DeviceMqttClientOptionsTests.cs) — C4
- [x] T023 `RunnerMqttTests`와 `ProgramTests`의 다시 쓰기 시험을 쓴다 (Tests/RunnerMqttTests.cs, Tests/ProgramTests.cs) — C1, C4, C5, C6, C7, C8, C9, C10, C11
  - C4: 접속 값(기본값과 설정값), 거부 4·5와 닫힌 포트·CONNACK 지연(시간 초과)에서 실패 결과·`connack`/`error` 기록
  - C9: `publish`(`device-to-dawon`) 항목의 토픽·페이로드·`qos`·`retain`
  - C5: 구독 필터·QoS 0, 실패 값이면 계속, 지연이면 실패 결과
  - C6: 발행 순서·토픽·QoS·retain, 발행 끄기, 주기 1초에서 2.5초 안에 두 번 이상
  - C7: `pub`·`up`·알 수 없는 명령·`quit`
  - C8: 수신 기록·콘솔. 기록된 뒤 1초 동안 그 밖의 클라이언트 발행이 없음
  - C11: `quit`·정지 토큰 취소에서 정상 DISCONNECT·`clean: true`·성공, 브로커가 끊으면 `clean: false`·실패
  - C10: 시험 비밀번호·키가 기록·콘솔 출력에 없음
  - C1(`ProgramTests`): 비밀 값이 든 설정을 처음 실행해 설정 파일을 다시 쓴 뒤에도, 설정 파일의 `ContentRootPath`가 비어 있다.
    - 이 시험은 OS 자격 증명 금고를 쓰므로 시험용 `CredentialService`·`CredentialAccount`를 쓴다.
    - `OsKms.SecretsDekFilePath`와 `RecordDirectory`는 시험 임시 폴더의 절대 경로다(`Path.Combine`은 둘째 인자가 절대 경로면 그 값을 쓴다).
    - Dawon REST·MQTT 주소는 `127.0.0.1`의 비어 있는 포트다. 실행은 C4 연결 실패로 3을 내며 끝나고, 시험은 3과 설정 파일 다시 쓰기(암호문, 빈 `ContentRootPath`)를 본다.
    - 끝나면 임시 폴더와 시험용 자격 증명을 지운다.

체크포인트: `DeviceMqttClientOptionsTests`·`RunnerMqttTests`·`ProgramTests`(다시 쓰기 시험 포함)가 통과한다.

## 단계 6: FOTA

- [x] T024 `FotaDownloader`를 만든다 (App/Fota/FotaDownloader.cs) — C14, C15, C16
  - 요청 만들기(테스트용 공개): plan 고정 헤더를 순서대로 넣는다. `User-Agent`는 설정값이다.
  - 처리기: `SocketsHttpHandler`, 자동 리디렉션·압축 해제 끔, 인증서 검증 끄기 설정
  - 요청 종류: 버전(끝 `/`를 지우고 `/version`), URL, 단일 GET. 두 요청은 버전 결과와 관계없이 차례로 보낸다.
  - 받기 제한: 요청마다 60초. `Content-Length`나 읽은 바이트가 4 MiB를 넘으면 그 요청을 멈춘다.
  - 저장: 200이면 `fota/<UTC 시각>-<순번>-<종류>.bin`에 `.part`로 쓴 뒤 이름을 바꾼다. 실패하면 `.part`를 지운다. SHA-256과 바이트 수를 낸다.
  - 기록: 요청·응답(버전 본문 포함)·오류(`timeout`·`too-large`·`cancelled`·연결 오류)
- [x] T025 실행기에 FOTA를 잇는다 (App/FakeDeviceRunner.cs) — C11, C14, C15, C16
  - 자동 받기: 받은 메시지에서 URL 후보를 찾으면 `candidate` 기록과 콘솔 출력을 하고, 자동 받기 설정이 켜져 있으며 처음 보는 URL이면 `Task.Run`으로 두 요청을 보낸다.
  - 콘솔 명령: `fota`(두 요청)·`get`(한 요청)을 매번 보낸다.
  - 종료: 진행 중 받기를 취소하고 끝날 때까지 기다린다.
- [x] T026 FOTA 테스트를 쓴다 (Tests/FotaDownloaderTests.cs, Tests/RunnerFotaTests.cs) — C9, C14, C15, C16
  - `FotaDownloaderTests`: 만든 요청의 헤더 순서·값, 버전 URL 만들기
  - `RunnerFotaTests`(C14): `candidate` 기록(`topic`·`url`)과 콘솔 출력, 자동 두 요청과 헤더·값, 같은 URL 한 번만, JSON 아님·나뉜 값·자동 끄기에서 요청 없음
  - `RunnerFotaTests`(C15): `fota`·`get`, 같은 URL 다시 받기
  - `RunnerFotaTests`(C16): 200 저장 내용·SHA-256·바이트 수, 버전 404 뒤 URL 요청, 지연에서 `timeout`, 닫힌 포트에서 연결 오류 기록과 실행 계속, 큰 본문에서 `too-large`이고 파일·`.part` 없음, HTTP·HTTPS, 느린 받기 도중 `up` 발행
  - `RunnerFotaTests`(C9): `response`의 `contentType`·`contentLength`·`file`·버전 `body` 필드

체크포인트: FOTA 테스트가 통과한다.

## 단계 7: 기준 spec 반영

- [x] T027 기준 spec을 새로 만든다 (docs/features/dawon-fake-device.md) — C1~C16
  - 요구: spec C1~C16을 R1~R16으로 옮긴다. 같은 번호이고, 이 작업의 C번호를 가리키지 않고 요구를 직접 적는다.
  - `파일:` 줄: 이 작업에서 만든 실행·테스트 파일
  - 이력: `| 2026-11-20 | dawon-fake-device | R1~R16 처음 작성(새 기능) | docs/specs/dawon-fake-device/ |`

체크포인트: 기준 spec의 R1~R16이 spec C1~C16과 같은 내용이고, `파일:` 줄이 실제 파일과 맞는다.

## 단계 8: 전체 검증과 실제 확인

- [x] T028 전체 검증을 하고 명령·결과를 작업 문서에 적는다 (저장소 전체) — C1, C12
  - `dotnet build`(경고 0)와 `dotnet test` 전체
  - 빌드한 exe로 종료 코드 확인: `--help`(0), 인자 없음(1), 틀린 설정(2)
- [x] T029 실제 다원으로 실행하고 spec C13 항목을 작업 문서에 적는다 (작업 문서) — C13
  - 실행 전에 사용자에게 확인한다: 쓸 mac(실장비를 끌지, 계정에서 지운 mac인지), 다원 키 유무, user_id
  - 기본 실행과 `FirmwareVersion: 1.01.30` 실행을 한다. 접속에 성공하면 사용자가 다원 앱에서 켜기·끄기와 펌웨어 업데이트를 조작한다.
  - 기록 파일·받은 펌웨어·실제 값은 저장소에 넣지 않는다.

체크포인트: 작업 문서에 C12 명령·결과와 C13 결과가 있다.

## 의존 관계

- 단계 1 → 나머지: 프로젝트·설정·옵션이 있어야 다른 코드가 빌드된다. T001 다음에 T004~T008, T002 다음에 T009다. T003은 T001·T002 뒤다.
- 단계 2: T011은 T010(가림)을 쓴다. T012~T014는 서로 독립이다. T015는 T010~T014 뒤다.
- 단계 3은 단계 1(테스트 프로젝트) 뒤에 하고, 단계 2와 동시에 할 수 있다.
- 단계 4 → 5 → 6: 같은 파일(`FakeDeviceRunner.cs`)에 흐름을 차례로 넣는다. T018은 T007·T011·T017, T021은 T020·T012·T013, T025는 T024·T014 뒤다.
- T022는 T020 뒤면 T021과 동시에 할 수 있다.
- `ProgramTests`의 다시 쓰기 시험은 실행기가 C4까지 돌아야 끝나므로 T021 뒤(T023)에 쓴다. 같은 파일의 나머지 시험은 T009에서 쓴다.
- 단계 7은 구현·테스트로 확인한 동작을 적으므로 단계 6 뒤다(spec-driven 3절 2번).
- 단계 8은 기준 spec 반영 뒤에 한다. T029는 T028이 통과한 빌드로 한다.
- 구현 후 검토(spec-driven 5절)는 단계 8 뒤에 기준점(`01655ef`)부터의 diff로 한다.
