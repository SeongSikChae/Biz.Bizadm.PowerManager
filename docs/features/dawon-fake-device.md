# dawon-fake-device

파일: Biz.Bizadm.PowerManager.slnx, src/Biz.Bizadm.PowerManager.FakeDevice.App/Biz.Bizadm.PowerManager.FakeDevice.App.csproj, src/Biz.Bizadm.PowerManager.FakeDevice.App/Program.cs, src/Biz.Bizadm.PowerManager.FakeDevice.App/ExitCode.cs, src/Biz.Bizadm.PowerManager.FakeDevice.App/AppConfiguration.cs, src/Biz.Bizadm.PowerManager.FakeDevice.App/FakeDeviceOptions.cs, src/Biz.Bizadm.PowerManager.FakeDevice.App/FakeDeviceRunner.cs, src/Biz.Bizadm.PowerManager.FakeDevice.App/Security/ConfigSecretProtector.cs, src/Biz.Bizadm.PowerManager.FakeDevice.App/Rest/DawonRestClient.cs, src/Biz.Bizadm.PowerManager.FakeDevice.App/Mqtt/DeviceMqttClient.cs, src/Biz.Bizadm.PowerManager.FakeDevice.App/Mqtt/DevicePayloads.cs, src/Biz.Bizadm.PowerManager.FakeDevice.App/Fota/FotaUrlFinder.cs, src/Biz.Bizadm.PowerManager.FakeDevice.App/Fota/FotaDownloader.cs, src/Biz.Bizadm.PowerManager.FakeDevice.App/Recording/FakeDeviceRecorder.cs, src/Biz.Bizadm.PowerManager.FakeDevice.App/Recording/TopLevelJson.cs, src/Biz.Bizadm.PowerManager.FakeDevice.App/Console/ConsoleCommand.cs, src/Biz.Bizadm.PowerManager.FakeDevice.App/config.example.yaml, tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests.csproj, tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/Fakes/TestCertificate.cs, tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/Fakes/FakeDawonHttp.cs, tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/Fakes/FakeDawonBroker.cs, tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/TestConsole.cs, tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/AppConfigurationTests.cs, tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/DevicePayloadsTests.cs, tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/FotaUrlFinderTests.cs, tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/FotaDownloaderTests.cs, tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/FakeDeviceRecorderTests.cs, tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/ConsoleCommandTests.cs, tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/DeviceMqttClientOptionsTests.cs, tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/RunnerRestTests.cs, tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/RunnerMqttTests.cs, tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/RunnerFotaTests.cs, tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/ProgramTests.cs

장치(B540)를 흉내 내어 실제 다원 서버와 직접 주고받는 가짜 장비 콘솔 프로그램이다. 다원이 장치에 보내는 메시지의 실제 형식(`tisyn` 응답, 앱 조작 때의 메시지, FOTA)을 관찰하려고 만들었다.

- 다원 REST 서버(`dwapi.dawonai.com:18443`)에 등록하고, 다원 브로커(`dwmqtt.dawonai.com:8883`)에 장치와 같은 방식(MQTT 3.1, TLS 1.2)으로 접속한다.
- 정해진 장치 메시지와 콘솔에서 입력한 메시지를 보내고, 받은 메시지에는 응답하지 않는다.
- 받은 메시지에 FOTA URL이 있으면 버전과 URL을 받아 파일로 저장한다. 받은 파일은 실행하지 않는다.
- 보내고 받은 원문은 dawon-relay와 같은 형식의 JSONL로 기록한다.

실행: `Biz.Bizadm.PowerManager.FakeDevice.App --config <설정 YAML> --log <로그 폴더>`. 종료 코드는 0(정상 종료), 1(명령줄 인자 오류), 2(설정 오류), 3(실행 중 실패)이다. 설계(콘솔 앱 템플릿과 다른 점, FOTA 헤더 근거, 시험 구조)는 `docs/specs/dawon-fake-device/plan.md`에 있다.

범위 밖: 받은 메시지에 대한 자동 응답(FOTA 진행·결과 보고 포함), 자동 재접속, 여러 장치 동시 실행, 다원이 보내지 않은 FOTA 경로를 추측한 요청, `register/create`·`device/connect`·FOTA 받기 밖의 HTTP 요청.

## 요구(완료 조건)

### 실행과 설정

- R1. 프로그램은 명령줄로 받은 설정 파일 하나를 읽어 실행한다.
  - 다원 서버
    - REST 서버 호스트·포트(기본 `dwapi.dawonai.com`·18443)
    - MQTT 브로커 호스트·포트(기본 `dwmqtt.dawonai.com`·8883)
    - TLS 인증서 검증을 끄는 선택(기본은 검증함, REST·MQTT·FOTA 모두에 적용)
  - 장치
    - mac(필수, 12자리 16진수, 소문자로 정규화)
    - 모델(기본 `B540_W`), 회사(기본 `DAWONDNS`)
    - 등록 요청의 user_id·lati·long(기본 빈 값·`0`·`0`)
    - 펌웨어 버전(기본 `1.01.36`)
  - 등록
    - `mqtt_key`(선택. 주면 등록을 건너뛴다)
    - `verify` 확인에 쓰는 서버 주소(기본: MQTT 브로커 호스트). device-register R3 치환표 문자로만 이뤄져야 한다.
  - MQTT 접속
    - ClientId(기본 `{회사}-{mac}`), 사용자명(기본 `{회사}-{모델}-{mac}`), 비밀번호(기본 `mqtt_key`)
    - keepalive 초(기본 60)
    - 토픽 접두(기본 `dwd`)
    - Will 토픽·페이로드(기본 `/lwt`·`offline`, QoS 0, retain 없음)
  - 발행
    - 접속 직후 `rg`·`tisyn`을 보낼지(기본 둘 다 보냄)
    - `rg` 토픽 접미(기본 `json`)
    - 발행 QoS(기본 0)
    - `tisyn` 주기 초(기본 0 = 접속 직후 한 번만)
    - 상태 보고 주기 초(기본 0 = 보내지 않음)와 상태 보고 요소 목록(`n`·`sv` 쌍)
  - FOTA
    - 받은 메시지의 URL로 자동으로 받을지(기본 받음, R14)
    - 요청의 `User-Agent`(기본: B540 펌웨어 덤프에 있는 FOTA 요청 `User-Agent` 전체 문자열)
  - 기록 폴더(기본: 실행 폴더 아래 `dawon-fake-device`)

  설정 파일이 없거나, mac 형식이 틀리거나, `verify` 확인 서버 주소에 치환표 밖 문자가 있거나, 값의 형식이 틀리면 원인을 표준 오류에 쓰고 다원 서버에 연결하지 않은 채 0이 아닌 종료 코드로 끝난다.

### REST

- R2. 설정에 `mqtt_key`가 없으면 다원 REST 서버에 HTTPS `POST /api/v1/devices/register/create`를 보낸다.
  - 헤더: `Content-Type: application/json`, `X-HIT-Version: 1.0`
  - 본문: `{"account":{"user_id":…},"device":{"mac":…,"model_id":…,"company":…,"lati":…,"long":…,"verify":"true"}}`(`docs/reference/b540-rest-api.md` 2절 첫 형식, 값은 설정)
  - 성공 조건: 200, 본문이 JSON 객체, 최상위 `mqtt_key`가 비어 있지 않은 문자열
  - 성공하면 그 `mqtt_key`를 이번 실행의 키로 쓴다. 최상위 `verify`가 문자열이면 device-register R3 계산식(설정의 `verify` 확인 서버 주소, mac, 키)으로 계산한 값과 같은지 판정해 기록과 콘솔에 남긴다. 같지 않아도 계속한다. 키에 치환표 밖 문자가 있으면 판정을 "계산 불가"로 기록·콘솔에 남기고 계속한다.
  - 성공 조건을 채우지 못하거나, 연결에 실패하거나, 10초 안에 응답이 없으면 그 결과를 기록하고 콘솔에 상태 코드(또는 오류)를 쓴 뒤, MQTT에 접속하지 않고 0이 아닌 종료 코드로 끝난다.
- R3. 키가 정해지면(등록 성공 또는 설정) `GET /api/v1/device/connect/{회사}-{모델}-{mac}`을 보내고 응답을 기록한다. 응답 내용, 연결 실패, 10초 시간 초과와 관계없이 MQTT 접속으로 넘어간다.

### MQTT

- R4. MQTT 3.1(`MQIsdp`)로 다원 브로커에 TLS 1.2로 접속한다. R1의 ClientId·사용자명·비밀번호·keepalive·Will을 쓰고, clean session을 켠다.
  - 다원이 거부하면 CONNACK 반환 코드를 기록하고 콘솔에 쓴 뒤 0이 아닌 종료 코드로 끝난다.
  - 연결 실패, 10초 시간 초과도 같은 방식으로 끝난다.
- R5. 접속하면 `{접두}.v1/iot-server/{회사}-{모델}-{mac}/#`를 QoS 0으로 구독한다. 구독 요청과 SUBACK 결과를 기록한다.
  - 10초 안에 SUBACK이 없으면 기록하고 0이 아닌 종료 코드로 끝난다.
  - SUBACK이 실패 값이면 기록하고 콘솔에 쓴 뒤 R6으로 넘어간다.
- R6. 구독 결과를 받은 뒤 정해진 메시지를 보낸다. 모든 발행 토픽은 `{접두}.v1/{회사}-{모델}-{mac}/iot-server/{접미}`이고, QoS는 설정값, retain은 없음이다.
  - `rg`(설정이 켜져 있으면 접속 직후 한 번)
    - 접미: 설정값
    - 페이로드: `{"sid":"0","msg":{"o":"rg","e":[…]}}`
    - 요소: `/3/0/0`=회사, `/3/0/1`=모델, `/3/0/2`=mac, `/3/0/3`=펌웨어 버전, `/3/0/997`=`1`, `/3/0/998`=`1.0`, `/3/0/999`=`1`(각각 `{"n":…,"sv":…}`)
  - `tisyn`
    - 설정이 켜져 있으면 `rg` 뒤에 한 번 보낸다. 주기가 0보다 크면 그 뒤로 주기마다 보낸다.
    - 접미 `timesync/json`, 페이로드 `{"sid":"0","msg":{"o":"tisyn","e":[]}}`
  - 상태 보고
    - 주기가 0보다 크면 정해진 `rg`·`tisyn` 뒤 바로 한 번 보내고, 그 뒤 주기마다 보낸다.
    - 접미 `notify/json`, 페이로드 `{"sid":"1","msg":{"o":"n","e":[…]}}`
    - 요소: 설정의 요소 목록을 순서대로 `{"n":…,"sv":…,"ti":""}`로 넣는다.
- R7. 실행 중 콘솔에서 한 줄 명령을 받는다.
  - 토픽과 페이로드를 받아 그대로 발행한다(QoS는 설정값, retain 없음). 장치 발행 접두 `{접두}.v1/{장치 키}/iot-server/`를 줄여 접미만 쓰는 방법도 있다. 페이로드는 JSON인지 따지지 않는다.
  - FOTA 명령은 R15·R16대로 처리한다.
  - 종료 명령을 받으면 R11대로 끝낸다.
  - 알 수 없는 명령은 사용법을 콘솔에 쓰고 무시한다.
- R8. 다원에서 받은 메시지는 토픽·QoS·retain·페이로드를 기록하고 콘솔에 토픽과 페이로드를 쓴다. 응답 메시지는 보내지 않는다.

### 기록과 종료

- R9. 보내고 받은 모든 주고받음을 기록 폴더의 UTC 날짜별 파일 `dawon-fake-device-yyyyMMdd.jsonl`에 한 줄에 JSON 하나로 덧붙인다.
  - 형식은 dawon-relay R9·R10과 같은 형식을 쓴다.
    - 공통 필드: `ts`·`channel`·`direction`·`kind`
    - `device-to-dawon`은 가짜 장비가 보낸 것, `dawon-to-device`는 다원에서 받은 것이다.
    - 본문·페이로드는 자르지 않는다. 올바른 UTF-8이 아니면 Base64로 기록하고 인코딩을 표시한다.
  - REST는 요청 항목과 응답 항목으로 남긴다(메서드·경로·본문, 응답은 상태 코드까지).
  - MQTT는 접속(ClientId·사용자명·비밀번호 유무·keepalive·clean session·Will), CONNACK, 구독, SUBACK, 발행(양방향), 종료를 남긴다.
  - FOTA(R14~R16)는 요청 항목과 응답 항목으로 남긴다(채널 `fota`). 응답 항목에는 다음을 담는다.
    - URL·상태 코드·`Content-Type`·`Content-Length`(없으면 없음)
    - 받은 바이트 수, SHA-256, 저장한 파일 이름
    - 버전 요청(`/version`)의 응답 본문. 그 밖의 요청의 본문은 기록에 넣지 않고 파일로만 둔다.
  - R2의 `verify` 판정, 연결 실패·시간 초과도 각각 항목으로 남긴다.
  - 동시에 기록해도 줄이 섞이지 않는다.
- R10. 기록, 콘솔, 표준 오류에 비밀 값이 남지 않는다.
  - REST 응답 본문(JSON 객체)의 최상위 `mqtt_key`·`verify`는 `***`로 가린다.
  - MQTT 비밀번호와 설정의 `mqtt_key`는 있는지 여부만 남긴다.
- R11. 종료 명령이나 Ctrl+C를 받으면 DISCONNECT를 보내 정상 종료하고, 종료 항목을 기록한 뒤 종료 코드 0으로 끝난다. 다원 쪽에서 접속이 끊기면 종료 항목(비정상 끊김)을 기록하고 0이 아닌 종료 코드로 끝난다.

### FOTA

- R14. 다원에서 받은 메시지(R8)의 페이로드가 JSON이고, 그 안의 문자열 값 가운데 `http://` 또는 `https://`로 시작하는 값이 있으면 그 값을 FOTA URL 후보로 본다.
  - 후보를 찾으면 콘솔에 쓰고 기록에 표시한다.
  - 자동 받기 설정이 켜져 있으면 두 요청을 차례로 보낸다.
    - 버전 요청: `GET {URL}/version`(URL 끝의 `/`는 하나로 맞춘다)
    - URL 요청: `GET {URL}`(URL이 이미지 자체일 수도 있어 그대로 받아 둔다)
  - 다원이 주소·포트·경로를 나눠 보내 URL 후보가 없으면 FOTA 요청을 보내지 않는다(원문은 R8대로 기록된다).
  - 한 번 실행하는 동안 같은 URL은 한 번만 자동으로 받는다.
- R15. 콘솔 명령으로 URL을 직접 받는다.
  - URL 하나를 주면 R14의 두 요청(버전·URL)을 보낸다.
  - URL 하나를 주고 그 URL만 `GET`하는 명령도 있다.
  - 같은 URL이라도 명령마다 다시 받는다.
- R16. R14·R15의 받기는 다음 규칙을 따른다.
  - 모든 요청은 `Connection: keep-alive`와 R1 설정의 `User-Agent`를 보낸다(근거 문서 `docs/reference/b540-fota.md` 3절의 고정 헤더).
  - 두 요청(버전·URL)을 보낼 때는 버전 요청의 결과와 관계없이 두 번째 요청도 보낸다.
  - HTTP와 HTTPS를 모두 받는다. R1의 TLS 인증서 검증 설정을 따른다.
  - 200이면 응답 본문을 기록 폴더 아래 `fota` 폴더에 요청마다 새 파일로 저장한다. 받은 파일은 실행하지 않는다.
  - 200이 아니거나, 연결에 실패하거나, 60초 안에 끝나지 않거나, 본문이 4 MiB를 넘으면 그 요청의 받기를 멈추고 그 결과를 기록·콘솔에 남긴다. 받은 데까지의 파일은 남기지 않는다.
  - 받기가 실패해도 프로그램은 계속 실행한다. MQTT 처리와 정해진 발행은 받는 동안에도 멈추지 않는다.

### 확인

- R12. R1~R11과 R14~R16을 확인하는 자동 테스트가 있고 통과한다. 이 테스트는 실제 다원 서버 대신 테스트 안의 가짜 REST 서버, 가짜 MQTT 브로커, 가짜 FOTA HTTP 서버를 쓴다.
- R13. 실제 다원 서버로 한 번 실행한다(수동). 등록 또는 설정 키로 접속을 시도하고, `rg`·`tisyn` 발행, 다원에서 받은 메시지를 확인한다. 다음 결과를 작업 기록에 적는다.
  - 쓴 설정 항목(mac 등 실제 값은 빼고 종류만)
  - 등록·`device/connect` 응답 상태
  - CONNACK 반환 코드
  - 받은 메시지 종류(`tisyn`에 다원이 응답했는지 포함)
  - FOTA 시도 결과
    - 펌웨어 버전을 기본값보다 낮게(예: `1.01.30`) 설정해 실행했을 때 다원이 FOTA 메시지를 보냈는지
    - 접속에 성공했으면, 사용자가 다원 앱에서 그 장치의 펌웨어 업데이트를 눌렀을 때 받은 메시지
    - FOTA URL을 받았으면 받은 파일의 종류(버전·URL·명령으로 받은 파일)·크기·SHA-256

  다원이 FOTA 메시지를 보내지 않으면 그 사실을 적으면 FOTA 시도는 완료로 본다. FOTA URL을 받았지만 받기가 실패하면 요청마다 실패 원인(상태 코드·오류)을 적으면 FOTA 시도는 완료로 본다. FOTA로 보이는 메시지가 왔지만 URL 후보가 없으면 받은 메시지를 적고, 다원이 보낸 값으로 URL을 이을 수 있으면 R15로 받아 그 결과까지 적는다. 이을 수 없으면 메시지만 적으면 FOTA 시도는 완료로 본다. 받은 펌웨어 파일은 저장소에 넣지 않는다. 등록이 거부되면 응답 상태를 적고, 설정으로 줄 다원 키가 있으면 그 키로 다시 실행해 접속 결과까지 적는다. 줄 키가 없으면 등록 결과만으로 완료로 본다. 접속이 거부되면 CONNACK 코드까지 적으면 완료로 본다. 발행·수신 항목은 접속에 성공했을 때만 적는다. 기록 파일과 실제 값은 저장소에 넣지 않는다.

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-20 | dawon-fake-device | R1~R16 처음 작성(새 기능) | docs/specs/dawon-fake-device/ |
