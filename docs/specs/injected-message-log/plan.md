# 서버 주입 메시지 해석 불가 토픽 로그 제외 계획

## 요약

`MqttBrokerHostedService.OnPublishAsync`는 `DeviceTopic.TryParse`가 실패하면 R7 로그를 남긴다. 로그를 남기기 전에 그 메시지를 서버가 주입했는지 가려, 주입한 메시지면 로그 없이 돌아간다. 전달은 지금처럼 브로커가 한다(가로채기에서 `ProcessPublish` 등 처리 여부 속성을 바꾸지 않음).

주입 여부는 `InterceptingPublishEventArgs.SessionItems`가 브로커의 `MqttServer.ServerSessionItems`와 같은 인스턴스인지로 가린다. MQTTnet 5.2.0.1603을 디컴파일해 확인한 경로는 다음과 같다.

- `MqttServer.InjectApplicationMessage`: `CustomSessionItems ?? ServerSessionItems`를 `DispatchApplicationMessage`의 세션 항목으로 넘긴다. 서버의 `tisyn` 응답은 `CustomSessionItems`를 주지 않으므로 `ServerSessionItems`가 넘어간다.
- 클라이언트 PUBLISH와 Will: 그 접속의 `Session.Items`를 넘긴다. 세션 항목은 접속 검사 때 접속마다 새로 만든 `ConcurrentDictionary`라 `ServerSessionItems`와 같은 인스턴스가 될 수 없다.
- `DispatchApplicationMessage`는 그 세션 항목으로 `InterceptingPublishEventArgs`를 만들어 `InterceptingPublishAsync`를 부른다.

자동 테스트는 기본 핸들러로 `tisyn` 응답을 주입하는 `TimeSyncResponseTests`에 C1을, R7 테스트가 있는 `MqttBrokerTests`에 C2를 더한다. 끝으로 기준 spec을 반영한다.

## 바꿀 파일·구조

- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs`
  - 필드 `IDictionary? serverSessionItems`를 더한다. `StartAsync`에서 서버를 만든 직후(처리기 등록·`server.StartAsync()` 전) `server.ServerSessionItems`를 담고, `DisposeAsync`에서 `current.StopAsync()`와 `Dispose()`가 끝난 뒤 `null`로 비운다(종료 중 주입된 메시지도 가리도록 `server` 필드와 달리 늦게 비움).
  - `OnPublishAsync`의 `TryParse` 실패 분기에서, `serverSessionItems`가 null이 아니고 `ReferenceEquals(e.SessionItems, serverSessionItems)`이면 로그 없이 `return`한다. 주석에 이유(서버가 정한 토픽·페이로드, 응답·전달은 R5·R12·R13 로그에 있음)를 적는다.
  - 판정은 private 메서드 `IsInjected(InterceptingPublishEventArgs e)`로 둔다.
  - 클래스 설명 주석에 서버가 주입한 메시지는 해석 불가 토픽 로그를 남기지 않는다는 것을 더한다.
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TimeSyncResponseTests.cs`: C1 테스트 두 개를 더한다.
  - 구독자 있음: 기존 `SendTimeSyncAsync`로 응답을 받은 뒤 로그에 `Downlink message enqueued: sender="power-manager", receiver="subscriber", topic="{DownlinkTopic}"`과 `Time sync response sent`가 있고 `Unparsed topic passed through: "{DownlinkTopic}"`이 없는지 본다.
  - 구독자 없음: 장치만 접속해 `tisyn`을 보내고 `Downlink message not consumed: sender="power-manager", topic="{DownlinkTopic}"`가 나타날 때까지 기다린 뒤 `Unparsed topic passed through: "{DownlinkTopic}"`이 없는지 본다.
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs`: C2 테스트 하나를 더한다. ClientId `power-manager`로 접속한 클라이언트가 서버 → 장치 토픽(`dwd.v1/iot-server/{DeviceKey}`)으로 보내면 `Unparsed topic passed through: "dwd.v1/iot-server/{DeviceKey}", clientId="power-manager", …` 로그가 남는지 본다. Will(`/lwt`)은 기존 `WillOnUnparsedLwtTopic_LogsClientIdAndPayload`가 확인하므로 그대로 둔다.
- `docs/features/mqtt-broker.md`: 기준 spec 반영.
  - R7에 "서버가 브로커에 주입한 메시지(`tisyn` 응답 등)는 남기지 않는다"를 더한다(C1·C2를 R7 한 문장으로 옮김).
  - 머리말에 주입 응답이 R7 로그로 쌓여 서버 주입 메시지를 뺐다는 한 문장을 더한다.
  - `파일:` 줄은 바꾸지 않는다(바꾸는 파일이 모두 이미 있음).
  - 이력에 한 줄을 더한다.

## 결정

- 주입 여부를 `e.SessionItems`와 `ServerSessionItems`의 인스턴스 비교로 가린다: 클라이언트 ClientId와 무관하고(C2), 주입하는 쪽 코드를 고치지 않아도 `CustomSessionItems`를 주지 않는 모든 주입에 맞는다. / 검토한 대안:
  - `e.ClientId == LoggingDeviceMessageHandler.SenderClientId`: 가장 짧지만 ClientId `power-manager`로 접속한 클라이언트의 메시지까지 빠져 C2를 어긴다.
  - 주입할 때 `CustomSessionItems`에 표시 키를 넣고 그 키를 본다: 주입하는 곳마다 표시를 넣어야 해 빠뜨리기 쉽고, 지금 주입하는 곳이 하나라 얻는 것이 없다.
  - 주입 메시지에 사용자 속성을 붙인다: 장치에 가는 메시지가 바뀌고 MQTT 3.1에는 사용자 속성이 없다.
- `ServerSessionItems`를 필드에 따로 담는다: `server` 필드는 `DisposeAsync`에서 `StopAsync` 전에 비워지므로, 종료 중에 주입된 메시지를 가리려면 따로 담아 늦게 비워야 한다. / 검토한 대안: `server?.ServerSessionItems`를 그때그때 읽는 것은 종료 중 판정이 틀려 R7 로그가 남을 수 있다.
- C1 테스트는 `TimeSyncResponseTests`에 둔다: 실제 주입은 기본 핸들러(`LoggingDeviceMessageHandler`)가 하고, `MqttBrokerTests`는 핸들러를 기록용으로 바꿔 주입이 일어나지 않는다.

## 인터페이스·데이터

공개 인터페이스·설정·로그 문구는 바뀌지 않는다. R7 로그 문구는 그대로이며 서버 주입 메시지에서만 남지 않는다.

## 오류·위험

- MQTTnet을 올릴 때 주입 경로의 세션 항목 처리가 바뀌면 판정이 틀릴 수 있다. C1 테스트가 주입 응답에 R7 로그가 없음을, C2 테스트와 기존 Will 테스트가 클라이언트 메시지에 R7 로그가 있음을 확인하므로 어느 쪽으로 바뀌어도 테스트가 실패한다.
- 앞으로 `CustomSessionItems`를 주고 주입하는 코드가 생기면 그 메시지는 R7 로그를 남긴다. 클래스 설명·`IsInjected` 주석에 이 전제를 적는다.
- 로그가 "없음"을 확인하는 테스트의 순서: MQTTnet의 `DispatchApplicationMessage`는 `InterceptingPublishAsync`를 부른 뒤에 구독자 큐에 넣거나(R12) 구독자 없음 이벤트(R13)를 낸다. 같은 로거로 순서대로 쓰므로, R12·R13 로그 줄이 파일에 나타난 뒤에 읽으면 R7 로그가 있었다면 이미 쓰여 있다.

## 검증 전략

- C1: `TimeSyncResponseTests`의 새 테스트 두 개(구독자 있음·없음). R5·R12·R13 로그가 있고 그 토픽의 R7 로그가 없음, 구독자가 응답을 받음.
- C2: `MqttBrokerTests`의 새 테스트(ClientId `power-manager` PUBLISH의 R7 로그)와 기존 `WillOnUnparsedLwtTopic_LogsClientIdAndPayload`(Will), 기존 R3~R6 분배 테스트(`MqttBrokerTests` 전체)가 통과.
- C3: 기준 spec diff를 읽어 R7 문장·머리말·이력 줄을 확인한다.
- C4: `dotnet build Biz.Bizadm.PowerManager.slnx`가 경고 0, `dotnet test Biz.Bizadm.PowerManager.slnx`가 실패 0.

## 원칙 점검

- 적용 규칙: 전역 규칙(언어·GitHub 접근·spec-driven), MCP rule `workflow/spec-driven.md`·`git/github-issue.md`·`workflow/repository-content.md`. 저장소에는 따로 둔 규칙 파일(AGENTS.md·CLAUDE.md)이 없어, 기존 코드 관례(한국어 XML 주석·주석)를 따른다
- 충돌: 없음. 저장소에 들어가는 문서·코드에는 저장소 기준 상대 경로만 쓰고 실장비 MAC 전체를 쓰지 않는다.
