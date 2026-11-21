# MQTT 접속 인증 강제 작업

형식: `- [x] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 테스트 도우미와 인증 테스트

- [x] T001 `TestDevice` record와 `TestDevices.RegisterAsync(app, mac)`(소문자 mac 등록, ClientId `DAWONDNS-{mac}`, 사용자명 `DAWONDNS-B540_W-{mac}`, 비밀번호는 발급 `mqtt_key`), `WithDevice(builder, device)` 확장 메서드 작성 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TestDevices.cs) — C1
- [x] T002 `MqttBrokerTests`의 `BuildMqtt31Connect`에 사용자명·비밀번호 인자(없으면 플래그 없음)를 더하고, `ValidatingConnection_LogsPasswordCheckAndAcceptsAll`을 인증 테스트로 바꾼다 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs) — C1, C2, C3, C6
  - `Match`(ClientId 대문자 mac) → `Success`
  - 틀린 비밀번호·비밀번호 없음(V310) → `BadUserNameOrPassword`, 틀린 비밀번호(V311) → `BadUserNameOrPassword`
  - 미등록 mac, `client-not-device`, `power-manager` → `BadUserNameOrPassword`, 미등록 mac(V311) → `BadUserNameOrPassword`
  - 원시 TCP MQTT 3.1 CONNECT: 틀린 비밀번호, 미등록 mac → CONNACK 넷째 바이트 `0x04`
  - 경우마다 판정 로그 한 줄(`passwordCheck` 값 포함), 로그 전체에 발급 키·틀린 비밀번호 문자열 없음
- [x] T003 예외 테스트 추가: 장치를 등록하고 `DROP TABLE Devices` 뒤 접속하면 MQTTnet 결과 `ServerUnavailable`(V310·V311 모두), 원시 TCP CONNACK 넷째 바이트 `0x03`, 로그에 `MQTT connection credential check failed`와 그 ClientId의 `passwordCheck="Unknown"` 판정 로그 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs) — C4, C6

체크포인트: `dotnet test --filter MqttBrokerTests`에서 T002·T003의 거부 경우가 실패한다(아직 구현 전이므로 접속이 성공함). 실패가 구현 전 동작 때문인지 확인한다.

## 단계 2: 브로커 구현

- [x] T004 `OnValidatingConnectionAsync`에서 `Match`가 아니면 `e.ReasonCode = BadUserNameOrPassword`, 예외면 Error 로그 뒤 `ServerUnavailable`. 판정 로그 형식은 그대로. 클래스 주석·메서드 주석·catch 주석(관찰 로그 실패로 접속을 막지 않는다)을 새 동작으로 고친다. `AppConfiguration`은 바꾸지 않는다 (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs) — C1, C2, C3, C4, C5, C6

체크포인트: T002·T003 테스트가 통과한다.

## 단계 3: 기존 테스트를 등록 장치 접속으로

- [x] T005 `MqttBrokerTests`의 나머지 테스트 접속을 등록 장치로 바꾼다. `ClientOptions`가 `TestDevice`를 받게 하고, 로그 문자열의 ClientId(`unknown-op-test`, `lwt-raw-test`, `lwt-test` 등)를 등록 장치 ClientId로, 원시 Will CONNECT에 자격 증명을 넣는다. `ClientNamedPowerManager_PublishToDownlink_LogsUnparsedTopic`은 등록 장치 클라이언트가 서버 → 장치 토픽에 PUBLISH하면 R7 로그가 남는 테스트로 바꾼다. TLS 1.2 테스트도 등록 장치로 접속한다 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs) — C1
- [x] T006 [P] 구독·전달 로그 테스트의 클라이언트(`sub-client`, `unsub-client`, `sub-a`, `sub-b`, `writer`, `uplink-watcher`, `publisher`, 장치)를 등록 장치로 바꾸고 로그 문자열의 ClientId를 맞춘다 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttSubscribeLogTests.cs) — C1
- [x] T007 [P] `SendTimeSyncAsync`가 `WebApplication`을 받게 하고 `subscriber`·`late-subscriber`·장치 클라이언트를 등록 장치로 바꾼다. 큐 넣음 로그의 받는 ClientId를 맞춘다 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TimeSyncResponseTests.cs) — C7
- [x] T008 [P] 원시 TLS 클라이언트(`fragtest-raw`)와 장치 클라이언트를 등록 장치로 바꾸고 `BuildMqtt31Connect`에 사용자명·비밀번호를 넣는다 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttPacketFragmentationTests.cs) — C7

체크포인트: `dotnet build` 경고 0, `dotnet test` 전체 실패 없음.

## 단계 4: 기준 spec 반영

- [x] T009 R1(접속 인증 문구), R9(거부·반환 코드 4·3, 예외 때 판정 `Unknown`, 거부한 접속도 로그), 머리말 한 문장, `파일:` 줄에 `TestDevices.cs` 추가, 이력에 이번 작업 한 줄 (docs/features/mqtt-broker.md) — C9

체크포인트: 기준 spec diff가 C9 항목과 맞는다.

## 단계 5: 실장비

- [x] T010 Release 빌드를 서버 로컬 배포 폴더에 배포(서버 중지·시작은 사용자, 백업·복사는 에이전트, 설정·DB·`secrets.dek`·인증서·log 덮어쓰지 않음). 서버 로그에서 B540 실장비 ClientId의 `passwordCheck="Match"` 판정 로그, 그 장치 키의 `Time sync response sent` 로그, 받는 쪽이 그 ClientId인 `Downlink message enqueued` 로그를 찾아 작업 문서에 적는다 — C8

체크포인트: 세 로그를 작업 문서에 적었다.

## 의존 관계

- T001이 단계 1~3 모든 테스트 작업보다 먼저다(도우미를 씀).
- T002·T003은 같은 파일이라 차례로 한다. 단계 1(테스트 먼저) 뒤 단계 2 구현.
- T005는 T002·T003과 같은 파일이라 그 뒤에 한다. T006~T008은 T001 뒤라면 서로·T005와 동시에 할 수 있다.
- 단계 3까지 끝나야 전체 테스트가 통과하므로 T009 전에 단계 3을 마친다.
- T010은 모든 코드·문서 작업과 4절 자동 검증 뒤에 한다(배포본이 최종 코드여야 함).
