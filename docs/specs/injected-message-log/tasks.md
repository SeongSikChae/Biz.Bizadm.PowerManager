# 서버 주입 메시지 해석 불가 토픽 로그 제외 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 브로커 판정

- [x] T001 `MqttBrokerHostedService`를 고친다.
  - 필드 `IDictionary? serverSessionItems`를 더하고, `StartAsync`에서 서버를 만든 직후(처리기 등록·`server.StartAsync()` 전) `server.ServerSessionItems`를 담는다.
  - `DisposeAsync`에서 `current.StopAsync()`와 `current.Dispose()`가 끝난 뒤 `serverSessionItems`를 `null`로 비운다.
  - private 메서드 `IsInjected(InterceptingPublishEventArgs e)`를 더한다. `serverSessionItems`가 null이 아니고 `ReferenceEquals(e.SessionItems, serverSessionItems)`이면 `true`. 한국어 주석에 전제(주입 시 `CustomSessionItems`를 주지 않으면 `ServerSessionItems`가 넘어오고, 클라이언트 PUBLISH·Will은 접속마다 만든 세션 항목이 넘어옴)를 적는다.
  - `OnPublishAsync`의 `TryParse` 실패 분기에서 `IsInjected(e)`이면 로그 없이 `return`한다. 처리 여부 속성은 바꾸지 않는다. 주석에 이유(서버가 정한 토픽·페이로드, 응답·전달은 R5·R12·R13 로그에 있음)를 적는다.
  - 클래스 설명 주석에 서버가 주입한 메시지(`CustomSessionItems` 없이 주입한 것)는 해석 불가 토픽 로그를 남기지 않는다는 것을 더한다.

  (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs) — C1, C2

체크포인트: `dotnet build Biz.Bizadm.PowerManager.slnx`가 경고 0으로 끝난다.

## 단계 2: 테스트

- [x] T002 [P] `TimeSyncResponseTests`에 C1 테스트 두 개를 더한다.
  - 구독자 있음: 기존 `SendTimeSyncAsync`로 응답을 받은 뒤, 로그에 `Downlink message enqueued: sender="power-manager", receiver="subscriber", topic="{DownlinkTopic}"`과 `Time sync response sent`가 모두 나타날 때까지 기다리고, 그다음 `Unparsed topic passed through: "{DownlinkTopic}"`이 없는지 본다.
  - 구독자 없음: 장치만 접속해 `tisyn`을 보내고 `Downlink message not consumed: sender="power-manager", topic="{DownlinkTopic}"`과 `Time sync response sent`가 모두 로그에 나타날 때까지 기다리고, 그다음 `Unparsed topic passed through: "{DownlinkTopic}"`이 없는지 본다.
  - 기다림 이유: R5 로그는 주입(`InjectApplicationMessage`)의 await가 끝난 뒤 쓰여 R7·R12·R13보다 늦으므로, R5 줄까지 보인 뒤에 읽으면 R7 로그가 있었다면 이미 쓰여 있다(D001).

  (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TimeSyncResponseTests.cs) — C1
- [x] T003 [P] `MqttBrokerTests`에 C2 테스트 하나를 더한다. ClientId `power-manager`로 접속한 클라이언트가 `dwd.v1/iot-server/{DeviceKey}`로 보내면 `Unparsed topic passed through: "dwd.v1/iot-server/{DeviceKey}", clientId="power-manager", …` 로그가 나타나는지 본다. Will은 기존 `WillOnUnparsedLwtTopic_LogsClientIdAndPayload`로 확인한다 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs) — C2

체크포인트: `dotnet test tests/Biz.Bizadm.PowerManager.Server.Spa.Tests --filter "FullyQualifiedName~TimeSyncResponseTests|FullyQualifiedName~MqttBrokerTests"`가 통과한다.

## 단계 3: 기준 spec 반영

- [x] T004 `docs/features/mqtt-broker.md`를 고친다. R7에 "서버가 브로커에 주입한 메시지(`tisyn` 응답 등)는 남기지 않는다"를 더하고, 머리말에 주입 응답이 R7 로그로 쌓여 서버 주입 메시지를 뺐다는 한 문장을 더하고, 이력에 `injected-message-log` 한 줄(R7 바꿈)을 더한다. `파일:` 줄은 바꾸지 않는다 (docs/features/mqtt-broker.md) — C3

체크포인트: 기준 spec diff에 R7 문장·머리말·이력 줄이 있다.

## 단계 4: 전체 확인

- [x] T005 `dotnet build Biz.Bizadm.PowerManager.slnx`(경고 0)와 `dotnet test Biz.Bizadm.PowerManager.slnx`(실패 0)를 실행한다 — C4

체크포인트: 빌드 경고 0, 테스트 실패 0.

## 의존 관계

- T002·T003은 T001 뒤에 한다(T001 없이는 C1 테스트가 실패한다). T002와 T003은 서로 다른 파일이라 동시에 할 수 있다.
- T004는 마지막 구현 단계로, 구현이 확인받은 대로 된 뒤에 한다.
- T005는 T001~T004가 끝난 뒤에 한다.
