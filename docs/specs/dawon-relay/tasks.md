# 다원 서버 중계 모드 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

경로 줄임: `Spa/` = `src/Biz.Bizadm.PowerManager.Server.Spa/`, `Tests/` = `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/`

## 단계 1: 설정·공통 도구

- [x] T001 [P] `MQTTnet` PackageReference 추가(버전은 중앙 관리 5.2.0.1603) (`Spa/Biz.Bizadm.PowerManager.Server.Spa.csproj`) — C5
- [x] T002 [P] `DawonRelayConfiguration`: 다음 속성과 기본값을 두고, 켜짐이면 호스트 비어 있지 않음·포트 1~65535를 `Validate`로 검증한다 (`Spa/Relay/DawonRelayConfiguration.cs`) — C1
  - `Enabled`(false)
  - `ApiServerHost`·`ApiServerPort`(`dwapi.dawonai.com`·18443)
  - `MqttServerHost`·`MqttServerPort`(`dwmqtt.dawonai.com`·8883)
  - `RecordDirectory`(`dawon-relay`)
  - `SkipCertificateValidation`(false)
- [x] T003 `AppConfiguration`에 `DawonRelay` 속성 추가, `Validate`에서 있으면 `ConfigurationValidator.Validate` (`Spa/AppConfiguration.cs`) — C1
- [x] T004 설정 테스트: 섹션 없음 → null, 섹션만 → 기본값, 켜짐+빈 호스트 → 검증 실패 (`Tests/AppConfigurationTests.cs`) — C1, C12
- [x] T005 [P] `TopLevelJson`: `TryFindValue`(최상위 객체의 첫 속성 값 범위·토큰 종류, 객체·배열은 끝까지), `Replace`, `Mask`(JSON 객체가 아니면 원문) (`Spa/Relay/TopLevelJson.cs`) — C2, C11
- [x] T006 `TopLevelJson` 테스트: 문자열·숫자·null·객체·배열 값, 이스케이프된 문자열, 공백·키 순서 보존, 중첩 객체 안 같은 이름 무시, JSON 아님·배열 최상위 (`Tests/TopLevelJsonTests.cs`) — C2, C11, C12
- [x] T007 [P] `DawonRelayRecorder`: 다음과 같이 기록한다 (`Spa/Relay/DawonRelayRecorder.cs`) — C9, C10, C11
  - 공통 필드 `ts`·`channel`·`direction`·`kind`와, plan 인터페이스 표의 종류별 필드를 `Utf8JsonWriter`로 한 줄에 쓴다.
  - 파일은 UTC 날짜별 `dawon-relay-yyyyMMdd.jsonl`이다.
  - `SemaphoreSlim`으로 직렬화하고, 항목마다 `FileMode.Append`로 연다.
  - 본문·페이로드는 UTF-8이 올바르면 `utf8`, 아니면 `base64`로 쓴다.
  - REST 응답 본문은 `TopLevelJson.Mask(mqtt_key, verify)`로 가린다.
  - 쓰기 실패는 Error 로그만 남긴다.
- [x] T008 기록기 테스트: 날짜별 파일 이름, 종류별 필드, Base64 표시, 응답 본문 가림, 병렬 1000줄 쓰기 후 모든 줄 JSON 파싱 성공, 쓰기 실패가 예외로 번지지 않음 (`Tests/DawonRelayRecorderTests.cs`) — C9, C10, C11, C12

체크포인트: `dotnet build`가 성공하고 T004·T006·T008 테스트가 통과한다.

## 단계 2: REST 중계

- [x] T009 [P] `BufferRequestBodyAttribute`: 모델 바인딩 전에 `Request.EnableBuffering()`을 부르는 resource filter (`Spa/Relay/BufferRequestBodyAttribute.cs`) — C2
- [x] T010 [P] `DeviceRegistrationService.StoreRelayedKeyAsync`: 다음 규칙으로 저장한다 (`Spa/Devices/DeviceRegistrationService.cs`) — C2
  - 없으면 생성(빈 값은 빈 문자열), 있으면 키 교체와 비어 있지 않은 값만 갱신, 마지막 요청 시각 갱신
  - `DbUpdateException`이면 한 번 다시 시도한다.
- [x] T011 `DawonRestRelay`: 다음을 처리한다 (`Spa/Relay/DawonRestRelay.cs`) — C2, C3, C4, C9, C11
  - **다원 요청:** 이름 있는 `HttpClient`로 `https://{ApiServerHost}:{ApiServerPort}`에 요청하고, 시간 제한은 10초다.
  - **등록:** 원문 본문, `Content-Type: application/json`, `X-HIT-Version: 1.0`으로 같은 경로에 보낸다.
    - 성공 조건 판정: 200, JSON 객체, 최상위 `mqtt_key`가 비어 있지 않은 치환표 문자열
    - 성공하면 `StoreRelayedKeyAsync`로 저장한다. 최상위 `verify`가 문자열이면 그 값 자리만 이스케이프 없이 다시 계산한 값으로 바꾸고, 다원의 `Content-Type`과 함께 원문 바이트로 응답한다.
    - 실패하면 다원 상태 코드·`Content-Type`·본문을 그대로 응답하고 DB를 바꾸지 않는다.
  - **connect:** 같은 경로로 보내고 응답을 그대로 돌려준다.
  - **연결 실패·시간 초과:** 502로 응답하고 `error` 항목을 기록한다.
  - **기록:** `request`·`response` 항목을 남긴다.
  - **로그:** `mqtt_key`·`verify`는 남기지 않는다.
- [x] T012 `DevicesController`: `Register`에 `[BufferRequestBody]`를 붙인다. mac 검증 뒤 켜짐이면 본문을 되감아 원문으로 `DawonRestRelay`에 맡긴다. `Connect`도 켜짐이면 `DawonRestRelay`에 맡긴다. 꺼짐 경로는 그대로 둔다 (`Spa/Devices/DevicesController.cs`) — C1, C2, C3, C4
- [x] T013 `Program.CreateWebApplication`: `DawonRelay?.Enabled == true`이면 다음을 등록한다 (`Spa/Program.cs`) — C1, C2, C4
  - `DawonRelayRecorder`
  - `DawonRestRelay`
  - 이름 있는 `HttpClient`: TLS 1.2, `SkipCertificateValidation`이면 인증서 검증 끔
- [x] T014 [P] `FakeDawon` REST 부분: 다음을 하는 Kestrel HTTPS 서버(자체 서명 인증서, 빈 포트)다 (`Tests/FakeDawon.cs`) — C12
  - 시나리오별 응답(상태·`Content-Type`·원문 바이트)을 돌려준다.
  - 응답 지연과 받은 요청(경로·헤더·본문 바이트) 기록을 지원한다.
- [x] T015 REST 중계 테스트: 다음을 확인한다 (`Tests/DawonRelayRestTests.cs`) — C1, C2, C3, C4, C9, C11, C12
  - **성공:** 요청 원문·헤더 전달, 응답 바이트(공백·키 순서·이스케이프·다른 필드 보존, `verify`만 교체), 다원 `Content-Type`, DB 저장·교체
  - **`verify` 처리:** 없음이면 본문 그대로, 문자열 아님이면 본문 그대로
  - **실패 조건:** 비200, `mqtt_key` 없음·빈 값·치환표 밖·문자열 아님, JSON 아님 → 그대로 전달, DB 불변
  - **연결 실패와 시간 초과:** 닫힌 포트와 응답 지연은 502이고, `error` 항목이 남는다.
  - **mac 오류:** 400이고 다원 요청은 0건이다.
  - **connect:** 그대로 전달되고, 연결 실패면 502다.
  - **기록:** `request`·`response` 항목과 가림을 확인한다.
  - **비밀 값:** 기록 파일·로그 폴더에 테스트 키·`verify` 문자열이 없다.
  - **꺼짐:** 기록 폴더에 파일이 없다.
  - **인증서 검증 기본값:** 위 테스트는 `SkipCertificateValidation`을 켜고 한다. 설정을 끈 상태(기본)에서는 자체 서명 인증서 가짜 서버로 보낸 등록·connect가 502이고, `error` 항목이 남으며, DB가 바뀌지 않는다.

체크포인트: T015가 통과하고, 기존 `DeviceEndpointTests`가 그대로 통과한다.

## 단계 3: MQTT 중계

- [x] T016 `FakeDawon` 브로커 부분: 다음을 하는 MQTTnet TLS 브로커(빈 포트, 3.1 허용)다 (`Tests/FakeDawon.cs`) — C12
  - 접속 결과를 지정할 수 있다(거부 코드).
  - 받은 CONNECT·SUBSCRIBE·UNSUBSCRIBE·PUBLISH·Will 발행과 종료를 기록한다.
  - 필터별 SUBACK 결과는 토픽으로 지정한다. 받은 SUBSCRIBE 필터 순서를 수신 패킷 이벤트에서 기록하고, 나가는 SUBACK을 `InterceptingOutboundPacketAsync`에서 그 순서로 다시 배열한다.
  - retained 메시지를 미리 둘 수 있고, 특정 클라이언트에 발행하거나 접속을 끊을 수 있다.
- [x] T017 가짜 브로커 사전 검사: 테스트 클라이언트가 직접 A(QoS 0)·B(QoS 1)를 SUBSCRIBE 하나로 보내면 지정 결과가 받은 순서로 돌아온다 (`Tests/DawonRelayMqttTests.cs`) — C12
- [x] T018 DISCONNECT 없는 끊기 확인: MQTTnet 클라이언트(3.1, Will 지정)로 가짜 브로커에 접속한 뒤 `DisconnectAsync` 없이 `Dispose`하면, 가짜 브로커가 Will을 발행한다 (`Tests/DawonRelayMqttTests.cs`) — C8
  - 실패하면 plan 결정대로 소켓을 직접 닫는 스트림 공급자로 바꾸는 작업을 T020에 넣는다(구현 결함 처리).
- [x] T019 [P] `MqttConnectPacketReader`: `ValidatingConnectionEventArgs`의 비공개 필드 `_connectPacket`에서 Will(토픽·페이로드·QoS·retain·플래그)을 읽는다. 필드가 없으면 실패를 돌려준다 (`Spa/Relay/MqttConnectPacketReader.cs`) — C5
- [x] T020 `DawonMqttRelay`: plan 결정·인터페이스 표대로 다음을 구현한다 (`Spa/Relay/DawonMqttRelay.cs`) — C5, C6, C7, C8, C9, C11
  - **세션:** ClientId별 현재 세션 지도, 세션 고유 ID, `RemoteEndPoint`, 교체 중 표시
  - **접속(C5):** 다원 접속은 3.1, TLS 1.2, 같은 자격 증명(`RawPassword`)·keepalive·clean session·Will로 한다. `SkipCertificateValidation`이 켜져 있으면 TLS 인증서 검증을 끈다. 결과는 다음과 같다.
    - CONNACK 반환 코드 대응표와 `ReasonCode` 설정
    - 연결 실패·시간 초과면 3
    - Will을 읽지 못하면 3과 Error 로그
  - **발행·구독 해제(C7·C6):** 수신 PUBLISH는 다원에 발행하고, UNSUBSCRIBE는 다원에서 해제한다. 해제가 실패하면 클라이언트를 끊는다.
  - **구독(C6):**
    - 수신 SUBSCRIBE를 대기 SUBSCRIBE에 넣고, 패킷 단위로 다원에 구독해 결과를 저장한다(실패면 실패 표시).
    - 구독 가로채기에서는 미적용 항목을 찾아 결과를 적용한다.
    - 나가는 SUBACK은 결과 코드를 원래 순서로 재배열하고, 같은 식별자의 대기 SUBSCRIBE를 지운다.
  - **다원 → 클라이언트 전달(C6):** 대기 SUBSCRIBE가 있는 동안 다원 메시지를 보관한다. 주입은 `dawon-relay/<세션 ID>` 발신자와 `Retain=false`로 하고, 대상 클라이언트에만 전달하며, 나가는 PUBLISH에서 retain을 복원한다.
  - **종료(C8):**
    - `Clean`이면 다원 정상 종료, `NotClean`이면 `Dispose`로 끊고, `Takeover`는 무시한다.
    - 다원이 끊기면 현재 세션이고 교체 중이 아닐 때 `DisconnectClientAsync`, 교체 중이면 표시만 한다.
    - 교체가 실패했고 옛 다원이 이미 끊겼으면 옛 클라이언트를 끊는다.
  - **기록(C9·C11):** `connect`·`connack`·`subscribe`·`suback`·`unsubscribe`·`publish`·`disconnect`·`error`를 남긴다. 비밀번호는 있는지 여부만 남긴다.
- [x] T021 `MqttBrokerHostedService`: 켜짐이면 다음을 한다 (`Spa/Mqtt/MqttBrokerHostedService.cs`) — C5, C6, C7, C8
  - `ValidatingConnectionAsync`(기존 R9 로그 뒤), `InterceptingInboundPacketAsync`, `InterceptingSubscriptionAsync`, `InterceptingClientEnqueueAsync`, `InterceptingOutboundPacketAsync`, `ClientDisconnectedAsync`를 `DawonMqttRelay`에 연결한다.
  - `ClientId`가 `dawon-relay/`로 시작하는 주입 메시지는 해석되는 토픽이어도 `OnPublishAsync`의 핸들러 분배에서 뺀다. 해석하지 못한 토픽의 R7 로그는 그대로 남긴다.
  - 해제할 때 이벤트를 떼고 세션을 정리한다.
- [x] T022 `Program.CreateWebApplication`: 켜짐이면 `DawonMqttRelay`를 등록하고 `MqttBrokerHostedService`에 선택 의존성으로 넘긴다 (`Spa/Program.cs`) — C1, C5
- [x] T023 MQTT 중계 테스트: 테스트 클라이언트는 MQTTnet `V310`이다 (`Tests/DawonRelayMqttTests.cs`) — C5, C6, C7, C8, C9, C10, C11, C12
  - **C5:**
    - Will 읽기 단위 테스트
    - 자격 증명·keepalive·clean session·Will 전달
    - 다원 거부 코드 4·5 전달, 연결 실패 시 3
  - **C6:**
    - 허용 QoS 전달(1로 낮춤)과 실패 값 0x80
    - 다중 필터: A QoS 0·B QoS 1을 보내고 다원이 A에 1·B에 0x80을 줄 때, 다원 SUBSCRIBE가 하나·순서 A·B이고 클라이언트 SUBACK이 [1, 0x80]
    - 구독 직후 retained(20회)
    - 잇단 구독(둘째 SUBSCRIBE 필터 둘, 20회)
    - 구독 해제: 필터 둘을 한 번에 보내고, 해제 뒤 메시지가 오지 않음
    - 다원 메시지가 그 클라이언트에만 가고, retain이 보존되며, 로컬 retained에 남지 않음
  - **C7:** 다원 수신과 로컬 핸들러 호출을 함께 확인한다.
  - **주입 메시지 로컬 처리:** 다원이 해석되는 토픽으로 보낸 메시지는 핸들러가 호출되지 않고, 서버 → 장치 토픽으로 보낸 메시지는 R7 로그가 남는다.
  - **인증서 검증 기본값:** 위 테스트는 `SkipCertificateValidation`을 켜고 한다. 설정을 끈 상태(기본)에서 자체 서명 인증서 가짜 브로커로 접속하면 반환 코드 3으로 거부되고 `error` 항목이 남는다.
  - **C8:**
    - 정상 종료면 다원 Will이 없다.
    - 소켓을 끊으면 다원 Will이 있고, 로컬 Will은 다원에 가지 않는다.
    - 다원 측에서 끊으면 클라이언트가 끊긴다.
  - **접속 단위 구분:** 같은 ClientId·다른 끝점의 종료 이벤트가 현재 세션에 적용되지 않는다(단위 테스트).
  - **takeover:** 새 접속이 유지되고, 다원 거부 시 옛 접속이 끊긴다.
  - **C9·C10:** MQTT 기록 항목의 종류별 필드를 확인한다.
  - **C11:** 기록 파일·로그 폴더에 테스트 비밀번호가 없다.

체크포인트: T017·T018·T023이 통과하고, 기존 `MqttBrokerTests`가 그대로 통과한다.

## 단계 4: 기준 spec 반영

- [x] T024 [P] 새 기준 spec 작성 (`docs/features/dawon-relay.md`) — C1~C13
  - 파일 줄: 이번에 만든 `Spa/Relay/` 파일들, 테스트 파일들
  - R1~R13: spec C1~C13을 요구로 옮긴다.
  - 이력 한 줄
- [x] T025 [P] device-register 기준 spec 반영 (`docs/features/device-register.md`) — C2, C3, C4
  - `DevicesController.cs`·`DeviceRegistrationService.cs`에 중계 분기가 생긴다는 설명
  - R1·R2·R6·R8에 "중계 모드 켜짐이면 dawon-relay 기준" 단서
  - 이력 한 줄
- [x] T026 [P] mqtt-broker 기준 spec 반영: R1·R9에 "중계 모드 켜짐이면 dawon-relay 기준" 단서, 이력 한 줄 (`docs/features/mqtt-broker.md`) — C5, C7

체크포인트: 세 기준 spec이 spec.md "기준 spec에 미치는 변경" 절과 맞는다.

## 단계 5: 검증

- [x] T027 전체 테스트 실행(`dotnet test`)과 결과 기록 — C12
- [ ] T028 실장비 확인(수동, 사용자와 함께): 다음 순서로 확인하고, 실행 내용과 기록 항목 종류를 작업 문서에 적는다(기록 파일·실제 값은 저장소에 넣지 않음) — C13
  1. 중계를 켠 서버를 배포한다.
  2. 대상 장치 SSID를 사용자에게 확인한 뒤, B540 실장비 1을 device-config로 설정한다.
  3. 다원 앱에서 켜기·끄기를 한 번 한다.
  4. 기록 파일에 `request`·`response`·`connect`·`connack`·양방향 `publish`가 있는지 확인한다.

체크포인트: C1~C13 확인 결과를 작업 문서에 적는다.

## 의존 관계

- 단계 1 → 단계 2·3: 설정(T002·T003), `TopLevelJson`(T005), 기록기(T007)를 두 중계가 쓴다.
- 단계 2 안의 순서:
  - T009·T010·T011은 T012보다 앞서야 한다(컨트롤러가 쓰는 부품).
  - T013은 T011 뒤다.
  - T014는 T015보다 앞서야 한다.
- 단계 3 안의 순서:
  - T016은 T017·T018·T023보다 앞서야 한다.
  - T018은 T020보다 앞서야 한다(끊기 방식 확정).
  - T019는 T020보다 앞서야 한다.
  - T020은 T021·T022보다 앞서야 한다.
  - T023은 T021·T022 뒤다.
- 단계 2와 단계 3은 서로 의존하지 않는다. 다만 둘 다 `Program.cs`를 고치므로(T013·T022) 차례로 한다.
- 단계 4는 단계 2·3의 실제 파일 목록이 정해진 뒤다.
- 단계 5는 모든 단계 뒤다. T028은 배포와 사용자 참여가 필요해 마지막이다.
