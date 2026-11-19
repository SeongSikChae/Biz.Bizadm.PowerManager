# dawon-relay

파일: src/Biz.Bizadm.PowerManager.Server.Spa/Relay/DawonRelayConfiguration.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Relay/DawonRelayRecorder.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Relay/TopLevelJson.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Relay/DawonRestRelay.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Relay/DawonMqttRelay.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Relay/MqttConnectPacketReader.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Relay/BufferRequestBodyAttribute.cs, src/Biz.Bizadm.PowerManager.Server.Spa/AppConfiguration.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Program.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DevicesController.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceRegistrationService.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Biz.Bizadm.PowerManager.Server.Spa.csproj, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/AppConfigurationTests.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TopLevelJsonTests.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DawonRelayRecorderTests.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DawonRelayRestTests.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DawonRelayMqttTests.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/FakeDawon.cs

장치와 다원 서버 사이의 실제 MQTT·REST 주고받음을 분석하기 위한 중계 모드다.

- 서버는 장치의 REST 요청과 MQTT 접속을 지금처럼 받는다.
- 같은 내용을 다원 서버(`dwapi.dawonai.com:18443`, `dwmqtt.dawonai.com:8883`)로 전달하고, 다원의 응답·메시지를 장치에 돌려준다.
- 양방향 원문은 JSONL로 기록한다.

설정 `DawonRelay.Enabled`로 모든 장치에 대해 켜고 끄며, 꺼져 있으면 device-register·mqtt-broker 기준 spec 그대로다. 설계(MQTTnet 5.2 브로커 이벤트 사용, 비공개 필드로 Will 읽기, 구독 직후 메시지 보관 등)는 `docs/specs/dawon-relay/plan.md`에 있다.

알려진 한계: 장치의 QoS 확인 응답(PUBACK 등)과 PINGREQ는 로컬 브로커와 다원 쪽 클라이언트가 따로 처리해 다원 직접 통신과 1:1로 대응하지 않는다. 메시지 내용과 순서는 같다.

## 요구(완료 조건)

- R1. `AppConfiguration.DawonRelay` 설정은 다음과 같다. 섹션이 없거나 꺼져 있으면 device-register R1~R8·mqtt-broker R1~R9 그대로이고 기록 파일을 만들지 않는다.
  - `Enabled`(기본 false)
  - `ApiServerHost`·`ApiServerPort`(기본 `dwapi.dawonai.com`·18443)
  - `MqttServerHost`·`MqttServerPort`(기본 `dwmqtt.dawonai.com`·8883)
  - `RecordDirectory`(`ContentRootPath` 기준 상대 또는 절대 경로, 기본 `dawon-relay`)
  - `SkipCertificateValidation`(기본 false, REST·MQTT 모두에 적용)
- R2. 켜짐이면 `POST /api/v1/devices/register/create`는 장치 본문을 바꾸지 않고 다원 REST 서버(HTTPS, 같은 경로, `Content-Type: application/json`, `X-HIT-Version: 1.0`)로 보낸다.
  - 성공 조건: 200, JSON 객체, 최상위 `mqtt_key`가 비어 있지 않은 device-register R3 치환표 문자열
  - 성공하면 다원 키를 그 mac의 등록 키로 저장(새로 만들거나 교체)한다. 마지막 요청 시각과 user_id·model_id·company는 device-register R5·R6 규칙대로 다룬다.
  - 장치에는 200과 다원의 `Content-Type`, 다원 응답 원문 바이트를 돌려준다. 최상위 `verify`가 문자열이면 그 값 자리만 device-register R3 계산식(`DeviceMqttServerAddr`, 요청 mac, 다원 키)으로 다시 계산한 값으로 이스케이프 없이 바꾼다. 없거나 문자열이 아니면 그대로 둔다.
  - 응답은 요청마다 그 요청의 다원 본문을 따르며, 동시 요청이면 저장되는 키는 마지막에 저장된 다원 키다.
- R3. mac이 올바르지 않으면 다원으로 보내지 않고 400이다. 다원이 응답했지만 성공 조건을 채우지 못하면 다원의 상태 코드·`Content-Type`·본문을 그대로 돌려주고 DB를 바꾸지 않는다. 다원에 연결하지 못하거나 시간 초과(10초)면 502이고 DB를 바꾸지 않는다.
- R4. 켜짐이면 `GET /api/v1/device/connect/{deviceKey}`는 다원의 같은 경로로 보내고 다원의 상태 코드·`Content-Type`·본문을 그대로 돌려준다. 연결 실패·시간 초과면 502다.
- R5. 켜짐이면 브로커에 접속하는 모든 클라이언트를 중계한다.
  - 클라이언트의 ClientId·사용자명·비밀번호·keepalive·clean session·Will(토픽·페이로드·QoS·retain)을 그대로 써서 MQTT 3.1로 다원 브로커에 TLS 1.2로 접속한다.
  - 다원이 받아들인 뒤에만 클라이언트 접속을 받아들인다.
  - 다원이 거부하면 그 CONNACK 반환 코드로 거부한다.
  - 연결 실패·시간 초과(10초)·Will 읽기 실패면 서버 사용 불가(3)로 거부한다.
- R6. 구독과 다원 메시지 전달은 다음과 같다.
  - SUBSCRIBE 패킷은 같은 필터 순서로 다원에 한 번에 구독하고, 다원 SUBACK의 필터별 허용 QoS(또는 실패 값)를 원래 필터 순서로 클라이언트에 돌려준다.
  - 다원 구독이 연결 오류·시간 초과로 실패하면 클라이언트 접속을 끊는다.
  - UNSUBSCRIBE는 같은 필터 목록으로 다원에서 해제한다.
  - 다원에서 받은 메시지는 그 클라이언트의 접속에만 같은 토픽·페이로드·QoS·retain으로 전달하고 다원으로 되돌려 보내지 않는다.
  - 구독 처리 중(SUBACK 전) 받은 다원 메시지는 보관했다가 SUBACK 뒤에 순서대로 전달한다.
  - 로컬 retained 저장소에는 남기지 않는다.
- R7. 클라이언트가 발행한 메시지는 같은 토픽·페이로드·QoS·retain으로 다원에 발행한다. 장치 메시지의 로컬 처리(mqtt-broker R2~R9)도 지금처럼 한다. 다원에서 주입한 메시지는 핸들러 분배(mqtt-broker R4)에서 빼며, 해석하지 못한 토픽 로그(R7·R8)는 남긴다.
- R8. 접속 종료를 양쪽에 맞춘다.
  - 클라이언트가 정상 종료하면 다원 접속도 정상 종료한다.
  - 비정상으로 끊기면 다원 접속을 DISCONNECT 없이 끊어 다원 쪽에서 Will이 발행되게 한다. 로컬 브로커의 Will은 다원으로 보내지 않는다.
  - 다원 접속이 끊기면 클라이언트 접속을 끊는다.
  - 같은 ClientId 재접속(takeover)이면 새 접속을 유지한다.
- R9. 중계한 주고받음을 기록 폴더의 UTC 날짜별 `dawon-relay-yyyyMMdd.jsonl`에 한 줄에 JSON 하나로 덧붙인다.
  - 공통 필드: `ts`·`channel`·`direction`·`kind`
  - REST: `request`·`response`·`error`
  - MQTT: `connect`·`connack`·`subscribe`·`suback`·`unsubscribe`·`publish`·`disconnect`·`error`
  - 종류별 필드는 plan.md 인터페이스 절을 따른다.
- R10. 본문과 페이로드는 자르지 않고 UTF-8 문자열로, 올바른 UTF-8이 아니면 Base64로 기록하고 인코딩을 같은 항목에 표시한다. 동시 기록에도 줄이 섞이지 않는다.
- R11. 기록과 서버 로그에 비밀 값이 남지 않는다.
  - 기록하는 REST 응답 본문(JSON 객체)의 최상위 `mqtt_key`·`verify`는 `***`로 가린다.
  - MQTT 비밀번호는 있는지 여부만 남긴다.
  - device-register R7을 지킨다.
- R12. R1~R11을 확인하는 자동 테스트(가짜 다원 REST 서버·브로커 사용)가 있고 통과한다.
- R13. B540 실장비 1로 실제 다원 서버를 거친 등록·MQTT 주고받음이 기록되는지 수동으로 확인한다(기록 파일과 실제 값은 저장소에 넣지 않음).

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-20 | dawon-relay | R1~R13 처음 작성(새 기능) | docs/specs/dawon-relay/ |
