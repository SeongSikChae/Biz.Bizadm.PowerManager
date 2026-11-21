# mqtt-broker

파일: src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/LoggingDeviceMessageHandler.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttDeviceCommandPublisher.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttServerAccessor.cs, src/Biz.Bizadm.PowerManager.Core/Mqtt/DeviceTopic.cs, src/Biz.Bizadm.PowerManager.Core/Mqtt/DeviceTopicKind.cs, src/Biz.Bizadm.PowerManager.Core/Mqtt/MqttEnvelope.cs, src/Biz.Bizadm.PowerManager.Core/Mqtt/MqttOperation.cs, src/Biz.Bizadm.PowerManager.Core/Mqtt/IDeviceMessageHandler.cs, src/Biz.Bizadm.PowerManager.Core/Mqtt/IDeviceCommandPublisher.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TimeSyncResponseTests.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttSubscribeLogTests.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttPacketFragmentationTests.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TestDevices.cs, tests/Biz.Bizadm.PowerManager.Core.Tests/DeviceTopicTests.cs, tests/Biz.Bizadm.PowerManager.Core.Tests/SerializationTests.cs

Server.Spa에 내장한 MQTT 브로커(MQTTnet). 장치 → 서버 메시지를 토픽·SenML 유사 JSON으로 해석해 `IDeviceMessageHandler`로 분배한다. 기본 핸들러는 받은 내용을 로그로 남기고, 시간 동기 요청(`tisyn`)에만 서버 → 장치 응답을 보낸다(R5, 응답 형식의 근거는 `docs/reference/b540-mqtt-topic.md` 「시간 동기」 절). 서버 → 장치 명령 발행(`MqttDeviceCommandPublisher`)은 껍데기다. 상세 설계는 `docs/server/0001`~`0003`에 있으며, 토픽·메시지 형태의 근거는 B540 펌웨어 덤프 분석(`docs/reference/b540-mqtt-topic.md`)이다. 실장비(M130-W)에서 근거와 다른 형태(`msg.o` `n`, 장치 키 없는 `/lwt`)가 관찰되어 R6~R8로 확인용 로그를 더했다(2026-11-19). 그 로그로 M130-W의 `n`이 상태 보고(토픽 접미 `notify/json`)임을 확인해 `notify`와 같이 분배한다(R4, 관찰 내용은 근거 문서 5절). 서버 → 장치 메시지가 장비까지 가는지 가리려고, 클라이언트의 구독·구독 해제와 서버 → 장치 메시지의 큐 넣음·구독자 없음을 로그로 남긴다(R10~R13, 구독·전달 동작은 바꾸지 않음). 그 로그로 구독·큐 넣음이 정상인데도 장비가 반응하지 않아 TLS 포트를 캡처한 결과, 브로커가 PUBLISH 하나를 TLS 레코드 두 개로 나눠 보내고 있어 두 엔드포인트 모두 패킷 분할을 끈다(R14). 서버가 주입한 `tisyn` 응답도 해석 불가 토픽 로그(R7)로 응답마다 쌓여, 서버가 주입한 메시지는 R7 로그에서 뺀다. 접속 자격 증명은 처음에 판정 로그만 남겼으나(R9), B540 실장비의 비밀번호가 등록 `mqtt_key`와 같음을 확인해 등록 장치가 등록 키로 접속할 때만 받도록 인증을 강제한다(R1·R9).

## 요구(완료 조건)

- R1. `MqttPort`에 비TLS 엔드포인트를 열고, `Tls.UseTls`가 `true`이면 `MqttsPort`에 `Tls` 서버 인증서로 TLS 1.2 고정 엔드포인트를 연다. MQTT 3.1(`MQIsdp`) 클라이언트를 받으며, 접속은 R9 판정이 `Match`일 때만 받는다(인증을 끄는 설정은 없다).
- R2. `DeviceTopic.TryParse`는 `{prefix}.v1/{deviceKey}/iot-server/{접미}`(빈 토큰 무시)를 해석하고 끝 토큰이 `lwt`이면 LWT로 본다. 토큰이 4개 미만이거나 접두가 비었거나 `.v1`로 끝나지 않거나 셋째 토큰이 `iot-server`가 아니면 실패한다. 서버 → 장치 토픽은 `{prefix}.v1/iot-server/{deviceKey}`다.
- R3. 해석한 장치 → 서버 메시지가 JSON 오류이거나 `msg.o`·`msg.e`가 없으면 Warning 로그를 남기고 분배하지 않는다.
- R4. `msg.o`가 `rg`·`drg`·`tisyn`·`notify`이면 각각 `RegisterAsync`·`DeregisterAsync`·`TimeSyncAsync`·`NotifyAsync`로, `n`(M130-W가 쓰는 `notify`의 짧은 형태)이면 `NotifyAsync`로(원래 값 `n`을 그대로 넘김), LWT 토픽은 페이로드와 관계없이 `OfflineAsync`로 분배한다. 핸들러 예외는 Error 로그로 남기고 브로커로 번지지 않으며, 브로커 종료 중 취소는 로그 없이 넘긴다.
- R5. 기본 핸들러(`LoggingDeviceMessageHandler`)는 받은 내용을 Information 로그로 남긴다. `tisyn`을 받을 때마다 그 요청 토픽의 접두·장치 키로 만든 서버 → 장치 토픽(`{prefix}.v1/iot-server/{장치 키}`, R2)에 retain 없이 `{"sid":<요청 sid, 없으면 "0">,"msg":{"o":"w","e":[{"n":"/99/0/1","sv":"<값>"}]}}`(값은 응답을 만들 때 서버 시각의 UTC Unix epoch 초를 소수점 아래 버린 10진수 문자열)을 보내고, Information 로그 항목 하나에 장치 키, 토픽, `sid`, 보낸 값을 남긴다. 서버 → 장치 명령 발행(`MqttDeviceCommandPublisher`의 `w`·`chg`·`reb`)은 `NotImplementedException`이다.
- R6. 해석에 성공했지만 `msg.o`가 R4의 다섯 값이 아니면, Warning 로그 항목 하나에 오퍼레이션, 장치 키, 토픽, ClientId, 원본 페이로드(UTF-8 디코딩, 이스케이프하지 않음)를 남긴다.
- R7. `DeviceTopic.TryParse`로 해석하지 못한 토픽(장치 키 없는 `/lwt` Will 포함)의 메시지는 그대로 전달하고, Information 로그 항목 하나에 토픽, ClientId, 원본 페이로드(R6과 같은 형식)를 남긴다. 서버가 브로커에 주입한 메시지(`tisyn` 응답 등)는 남기지 않는다. MQTT 클라이언트가 PUBLISH한 메시지와 브로커가 클라이언트 대신 보내는 Will은 ClientId와 상관없이(ClientId가 `power-manager`여도) 남긴다.
- R8. R6·R7의 원본 페이로드가 4096바이트를 넘으면 앞 4096바이트만 디코딩해 남기고, 잘림 여부와 원래 바이트 수를 같은 로그 항목에 남긴다.
- R9. 클라이언트가 접속할 때마다(거부한 접속 포함) Information 로그 항목 하나에 ClientId, 사용자명, 비밀번호 유무, 비밀번호 판정(`Match`·`Mismatch`·`Unknown`)을 남긴다. ClientId가 `DAWONDNS-{12자리 16진수}`이고 그 mac(대소문자 무관)이 `device-register`에 등록돼 있으면 비밀번호가 등록 `mqtt_key`와 같을 때 `Match`, 다르거나 없으면 `Mismatch`, 그 밖에는 `Unknown`이다. 판정을 확인하는 중 예외가 나면 Error 로그를 남기고 판정은 `Unknown`이다. 비밀번호 값은 어떤 로그에도 남기지 않는다. `Match`이면 접속을 받고, `Mismatch`·`Unknown`(예외 때 빼고)이면 거부해 MQTT 3.1·3.1.1 클라이언트는 CONNACK 반환 코드 4(잘못된 사용자명 또는 비밀번호)를 받으며, 예외 때는 거부해 반환 코드 3(서버 사용 불가)을 받는다(R1).
- R10. 클라이언트가 SUBSCRIBE를 보내면 그 안의 토픽 필터마다 Information 로그 항목 하나에 ClientId, 토픽 필터, 요청 QoS를 남긴다. 구독을 거부하거나 바꾸지 않으며, SUBACK 결과와 그 뒤 메시지 전달은 이 로그가 없을 때와 같다.
- R11. 클라이언트가 UNSUBSCRIBE를 보내면 그 안의 토픽 필터마다 Information 로그 항목 하나에 ClientId와 토픽 필터를 남긴다. 구독 해제를 거부하지 않는다.
- R12. 서버 → 장치 토픽 메시지를 브로커가 구독 클라이언트의 큐에 넣을 때마다, 받는 클라이언트마다 Information 로그 항목 하나에 보낸 ClientId(서버가 주입한 메시지는 `power-manager`), 받는 ClientId, 토픽을 남긴다. 서버 → 장치 토픽은 빈 토큰을 빼고 토큰이 3개 이상이며, 첫 토큰이 `.v1`로 끝나고 그 앞 접두가 비지 않으며, 둘째 토큰이 `iot-server`인 토픽이다(R2의 서버 → 장치 토픽과 그 뒤에 접미가 붙은 형태). 그 밖의 토픽(장치 → 서버 토픽, `/lwt` 등)에는 남기지 않는다.
- R13. 서버 → 장치 토픽(R12) 메시지에 맞는 구독이 없어 아무 클라이언트에게도 가지 않으면 Warning 로그 항목 하나에 보낸 ClientId와 토픽을 남긴다. 서버 → 장치 토픽에만 남긴다.
- R14. 브로커는 비TLS·TLS 엔드포인트 모두 패킷 분할을 끈 설정(`AllowPacketFragmentation = false`)으로 열어, 클라이언트에 보내는 MQTT 패킷마다 한 번에 쓴다. TLS 엔드포인트에서 MQTT 패킷 전체가 16384바이트 이하인 PUBLISH는 TLS 레코드 하나로 나간다(장비 MQTT 라이브러리가 여러 수신에 걸친 패킷을 합치지 않는 것으로 보여서).

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-19 | mqtt-unknown-op-log | R1~R5 처음 작성(기존 구현 기준), R6 추가(알 수 없는 오퍼레이션 경고에 토픽·ClientId·원본 페이로드), R7 추가(해석 불가 토픽 로그에 ClientId·원본 페이로드), R8 추가(4096바이트 자르기) | docs/specs/mqtt-unknown-op-log/ |
| 2026-11-19 | device-register-mqtt-key | R9 추가(CONNECT 자격 증명 관찰 로그) | docs/specs/device-register-mqtt-key/ |
| 2026-11-19 | mqtt-notify-short-op | R4 바꿈(`n` → `NotifyAsync`), R6 바꿈(R4의 다섯 값) | docs/specs/mqtt-notify-short-op/ |
| 2026-11-19 | tisyn-response-doc | 없음(근거 문서 docs/reference/b540-mqtt-topic.md에 시간 동기 응답·서버 → 장치 수신 분기 분석 기록) | docs/specs/tisyn-response-doc/ |
| 2026-11-20 | dawon-relay | R1·R9 바꿈(중계 모드 켜짐이면 dawon-relay R5 기준) | docs/specs/dawon-relay/ |
| 2026-11-20 | tisyn-response | R5 바꿈(tisyn 응답, 중계 켜짐이면 응답 안 함) | docs/specs/tisyn-response/ |
| 2026-11-20 | dawon-relay-removal | R1·R9 바꿈(중계 모드 단서 삭제), R5 바꿈(중계 조건 없이 `tisyn`마다 응답) | docs/specs/dawon-relay-removal/ |
| 2026-11-21 | mqtt-subscribe-log | R10~R13 추가(구독·구독 해제, 서버 → 장치 메시지 큐 넣음·구독자 없음 로그) | docs/specs/mqtt-subscribe-log/ |
| 2026-11-21 | mqtt-no-fragmentation | R14 추가(비TLS·TLS 엔드포인트 패킷 분할 끔, TLS PUBLISH 레코드 하나) | docs/specs/mqtt-no-fragmentation/ |
| 2026-11-21 | injected-message-log | R7 바꿈(서버가 주입한 메시지는 해석 불가 토픽 로그 제외, 클라이언트 PUBLISH·Will은 ClientId와 상관없이 유지) | docs/specs/injected-message-log/ |
| 2026-11-21 | mqtt-connect-auth | R1 바꿈(접속 인증 강제, 끄는 설정 없음), R9 바꿈(Mismatch·Unknown 거부 반환 코드 4, 예외 때 판정 Unknown·거부 반환 코드 3, 거부한 접속도 로그) | docs/specs/mqtt-connect-auth/ |
| 2026-11-21 | device-control-ref | 없음(근거 문서 docs/reference/mqtt-device-control.md 작성: B540·M130-W 서버 → 장치 제어 오퍼레이션·리소스 분석) | docs/specs/device-control-ref/ |

표에 없는 이전 변경: 기록 없음(docs/server/0001~0003 참조. #1 최초 구현)
