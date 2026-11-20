# 다원 중계 모드 제거

기능: dawon-relay(기준 spec docs/features/dawon-relay.md), device-register(기준 spec docs/features/device-register.md), mqtt-broker(기준 spec docs/features/mqtt-broker.md), dawon-fake-device(기준 spec docs/features/dawon-fake-device.md)
이슈: GitHub #20

## 무엇을, 왜

#20(커밋 3bac44e)에서 장치와 다원 서버 사이의 실제 REST·MQTT 주고받음을 분석하려고 Server.Spa에 중계 모드(설정 `DawonRelay.Enabled`)를 두었다. 서버가 장치 요청을 다원으로 전달하고, 장치 MQTT 접속마다 다원 브로커에 대신 접속해 양방향 원문을 JSONL로 기록한다(dawon-relay 기준 spec R1~R13).

중계 모드는 실제로 제대로 동작하지 않았고, 실장비 확인(dawon-relay R13)도 하지 못했다. 그 사이 #24에서 다원 서버와 직접 주고받으며 원문을 기록하는 가짜 장비 클라이언트(dawon-fake-device)를 만들었으므로, 실서버의 데이터 확인은 가짜 장비로 한다. 그래서 중계 모드를 제거한다.

제거하면 각 기능은 중계 모드가 꺼져 있을 때(지금 기본값)의 동작만 남는다.

- device-register: register·connect는 늘 서버가 직접 처리한다(R1·R2·R6·R8의 중계 단서가 빠짐).
- mqtt-broker: 접속은 다원 결과로 거부되지 않고(R1·R9의 중계 단서가 빠짐), 기본 핸들러는 `tisyn`마다 늘 시간 동기 응답을 보낸다(R5의 "중계 모드가 꺼져 있으면" 조건과 "켜져 있으면 응답하지 않음"이 빠짐).
- dawon-relay: 기능이 없어지므로 기준 spec의 요구 R1~R13을 모두 뺀다. 파일은 이력과 함께 남겨 제거 사실을 기록한다(규칙상 뺀 요구는 목록에서 지우고 이력에 남김).
- dawon-fake-device: 동작은 바뀌지 않는다. 기록 형식을 설명하며 dawon-relay를 가리키는 문장(머리말, R9)과, 중계 코드를 가리키는 주석만 가짜 장비 안에서 뜻이 통하게 고친다. R9에는 공통 필드·방향·인코딩은 직접 적혀 있지만 채널 값과 kind 값은 dawon-relay R9 참조로만 정해져 있으므로, R9가 dawon-relay에 기대던 채널 값(`rest`·`mqtt`·`fota`)과 kind 값(지금 `FakeDeviceRecorder`가 쓰는 값)을 R9에 직접 적는다. 기록 형식(동작)은 바뀌지 않는다.

`docs/specs/dawon-relay/`(#20의 변경 문서)는 끝난 작업 기록이므로 고치지 않는다.

`AppConfiguration`의 `DawonRelay` 속성을 지우므로, 설정 YAML에 `DawonRelay` 섹션이 남아 있으면 서버는 설정 오류로 시작하지 않는다(D001). 그 경우 사용자가 그 섹션을 지운다.

하지 않는 것:

- 중계 모드 기록 파일(`dawon-relay-yyyyMMdd.jsonl`)이 이미 만들어진 폴더를 지우는 것(실행 환경의 파일이며 저장소에 없음).
- 가짜 장비의 동작·기록 형식 변경.
- 중계 모드와 함께 손댔지만 중계와 상관없이 쓰이는 것(`MqttServerAccessor`, `tisyn` 응답, MQTTnet 패키지 참조처럼 다른 코드가 쓰는 것)의 제거.

## 완료 조건

- C1. Server.Spa에 중계 모드 코드가 없다. `src/Biz.Bizadm.PowerManager.Server.Spa/Relay/` 폴더가 없고, `src`·`tests`의 C# 파일에서 `DawonRelay`·`DawonMqttRelay`·`DawonRestRelay`·`DawonRelayRecorder`·`StoreRelayedKeyAsync`·`BufferRequestBody`·`IsInjected` 검색 결과가 없다. `src/Biz.Bizadm.PowerManager.Server.Spa`와 `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests`의 C# 파일에서 `중계`·`dawon-relay` 검색 결과도 없다.
- C2. 중계 모드 전용 테스트(중계 REST·MQTT·기록기 테스트, 가짜 다원 서버·브로커, 중계용 JSON 값 교체 테스트, 중계 설정 테스트)가 없다.
- C3. 설정에 `DawonRelay` 섹션이 없으면 서버가 지금처럼 시작하고, 있으면 설정 오류(종료 코드 2)로 시작하지 않는다(D001).
- C4. 서버 동작이 지금의 중계 모드 꺼짐 동작과 같다. register·connect는 서버가 직접 처리하고(device-register R1~R8), 브로커는 접속을 거부하지 않으며(mqtt-broker R1·R9), 기본 핸들러는 `tisyn`마다 시간 동기 응답을 보낸다(mqtt-broker R5). 기존 자동 테스트(장치 등록·브로커·시간 동기 응답)가 그대로 통과한다.
- C5. 솔루션(`Biz.Bizadm.PowerManager.slnx`) 빌드가 성공하고, 솔루션의 테스트 프로젝트 6개(Core, DeviceConfig.Mcp, DeviceConfig, FakeDevice.App, Server.Core, Server.Spa의 `.Tests`) 테스트가 모두 통과한다.
- C6. 기준 spec이 제거를 반영한다.
  - dawon-relay: 요구 목록이 비고, 이력에 이번 작업과 "R1~R13 뺌(중계 모드 제거)"이 있으며, 머리말이 제거된 기능임을 밝힌다. `파일:` 줄은 `없음(중계 모드 제거로 모든 파일을 지웠거나 다른 기능에만 속함)`이다.
  - device-register: 머리말과 R1·R2·R6·R8에서 중계 모드 문장이 빠지고, `파일:` 줄은 그대로이며 이력에 이번 작업이 있다.
  - mqtt-broker: 머리말과 R1·R5·R9에서 중계 모드 문장이 빠지고, R5는 `tisyn`마다 응답한다는 문장이 되며, 이력에 이번 작업이 있다.
  - dawon-fake-device: 머리말과 R9가 dawon-relay를 가리키지 않고, R9에 채널 값(`rest`·`mqtt`·`fota`)과 kind 값이 `FakeDeviceRecorder`가 쓰는 값 그대로 직접 적혀 있으며, 이력에 이번 작업이 "R9 바꿈(형식 값을 직접 적음, 동작은 그대로)"으로 있다.
- C7. 가짜 장비 코드의 주석이 없어진 중계 코드·dawon-relay 요구를 가리키지 않는다(`src/Biz.Bizadm.PowerManager.FakeDevice.App`에서 `중계`·`dawon-relay` 검색 결과가 없다).
