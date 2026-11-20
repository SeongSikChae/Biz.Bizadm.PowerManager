# tisyn 시간 동기 응답 계획

## 요약

기본 핸들러 `LoggingDeviceMessageHandler.TimeSyncAsync`가 받은 내용을 지금처럼 로그로 남긴 뒤, 중계 모드가 꺼져 있으면 그 장치의 서버 → 장치 토픽으로 `w` `/99/0/1` 응답을 브로커에 주입한다. 핸들러는 브로커를 만드는 `MqttBrokerHostedService`에 주입되므로, 브로커 참조는 새 싱글턴 `MqttServerAccessor`를 거쳐 받는다(호스티드 서비스가 시작할 때 넣고 멈출 때 비운다). 시각은 이미 등록된 `TimeProvider`에서 얻고, 페이로드는 기존 `MqttEnvelope` 레코드를 직렬화해 만든다. 기준 spec mqtt-broker R5·머리말·이력을 고치고, 실장비 결과를 근거 문서에 적는다.

## 바꿀 파일·구조

- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttServerAccessor.cs`(새): 실행 중인 `MqttServer` 참조를 담는 싱글턴. `Server` 속성(`MqttServer?`, volatile)만 둔다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs`: 생성자에 `MqttServerAccessor`를 더한다. `StartAsync`에서 `server.StartAsync()` 뒤에 `accessor.Server = server`, `DisposeAsync`에서 이벤트를 떼기 전에 `accessor.Server = null`.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/LoggingDeviceMessageHandler.cs`: 생성자에 `MqttServerAccessor`, `TimeProvider`, 선택 의존성 `DawonMqttRelay? relay = null`을 더한다. `TimeSyncAsync`는 기존 `Log` 뒤에 `relay is null`이면 응답을 보낸다(인터페이스·결정 절). 클래스 설명 주석을 고친다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Program.cs`: `builder.Services.AddSingleton<MqttServerAccessor>()`를 핸들러 등록 앞에 더한다.
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TimeSyncResponseTests.cs`(새): C1~C4 중 중계 꺼짐 경우의 통합 테스트와 발행 실패 단위 테스트.
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DawonRelayMqttTests.cs`: 중계 켜짐에서 응답하지 않음(C3) 테스트 하나를 더한다. 이 파일의 앱 시작 도우미 `StartAsync(configuration, handler)`는 핸들러를 늘 `RecordingHandler`로 바꾸므로, 이 테스트는 핸들러를 바꾸지 않는 시작 경로(도우미에 교체하지 않는 인자를 두거나 `TestApp.StartAsync`를 직접 부름)로 기본 `LoggingDeviceMessageHandler`를 쓴다.
- `docs/features/mqtt-broker.md`: R5·머리말·이력 반영(C5). `파일:` 줄에 `MqttServerAccessor.cs`와 `TimeSyncResponseTests.cs`를 더한다.
- `docs/reference/b540-mqtt-topic.md`: 실장비 확인 결과를 4절 미확인 항목·5절 M130-W 관찰에 적는다(C7).

## 결정

- 응답은 기본 핸들러에서 보낸다: spec 「기준 spec에 미치는 변경」의 R5가 "기본 핸들러가 `tisyn`에 응답"으로 정해져 있다. 핸들러를 바꾸는 테스트(`RecordingHandler`)는 응답을 보내지 않게 되어, 기존 브로커 테스트의 받는 메시지가 늘지 않는다. / 검토한 대안: `MqttBrokerHostedService.OnPublishAsync`에서 `TimeSync` 분배 뒤 직접 보내기 — R5 문구와 어긋나고, 핸들러를 바꿔도 응답이 나가 기존 테스트의 전제가 바뀐다.
- 브로커 참조는 `MqttServerAccessor`로 넘긴다: 호스티드 서비스가 핸들러를 주입받으므로, 핸들러가 호스티드 서비스나 브로커를 직접 주입받으면 순환이 생긴다. 접근자는 둘 다에서 독립된 싱글턴이라 순환이 없다. / 검토한 대안: `IDeviceCommandPublisher` 구현에 넣기 — 명령 발행은 그대로 NotImplemented로 두기로 했다(D002). `IServiceProvider`로 늦게 꺼내기 — 의존성이 드러나지 않는다.
- 중계 모드 판단은 `DawonMqttRelay`가 등록됐는지로 한다: `Program`은 `DawonRelay.IsEnabled`일 때만 `DawonMqttRelay`를 등록하고, `MqttBrokerHostedService`도 같은 선택 의존성(`DawonMqttRelay? relay = null`)으로 중계 여부를 정한다. 같은 기준을 써서 두 곳이 어긋나지 않는다. / 검토한 대안: `AppConfiguration.DawonRelay?.IsEnabled` 직접 확인 — 결과는 같지만 판단 기준이 두 개가 된다.
- 페이로드는 `JsonSerializer.Serialize(new MqttEnvelope(sid, new MqttMessage("w", [new MqttElement("/99/0/1", value)])))`로 만든다: 기존 레코드의 `JsonPropertyName`이 `sid`·`msg`·`o`·`e`·`n`·`sv`이고 선언 순서가 spec C2 순서와 같다. 기본 옵션은 들여쓰기 없이 한 줄로 쓴다.
- 값은 `timeProvider.GetUtcNow().ToUnixTimeSeconds()`를 `CultureInfo.InvariantCulture`로 쓴 문자열이다: `ToUnixTimeSeconds`는 1970년 이후 시각에서 소수점 아래를 버린다(C2).
- 주입 메시지: `InjectedMqttApplicationMessage`(`Topic = DeviceTopic.ToDevice(prefix, deviceKey)`, `Retain = false`, QoS 0, `SenderClientId = "power-manager"`). QoS는 장치가 구독한 QoS를 모르므로 가장 낮은 0으로 둔다. 응답이 없어져도 장치가 `tisyn`을 다시 보내 다음 응답을 받는다. / 검토한 대안: QoS 1 — B540 덤프에 PUBACK 처리 로그는 있지만 구독 QoS가 0이면 어차피 0으로 전달되고, 근거가 없다.
- 실패 처리: `accessor.Server`가 null(브로커 미시작·종료 중)이거나 `InjectApplicationMessage`가 예외를 내면 `LogError`를 남기고 예외를 다시 던지지 않는다(C4). 브로커 종료 중 취소(`OperationCanceledException`이고 `ct.IsCancellationRequested`)는 다시 던져, 기존 R4 처리(로그 없이 넘김)를 따른다.

## 인터페이스·데이터

- `MqttServerAccessor`: `public sealed class MqttServerAccessor { private volatile MqttServer? server; public MqttServer? Server { get => server; set => server = value; } }`
- 응답 페이로드 예(요청 `sid` `"0"`, 시각 2026-11-20T04:00:00Z): `{"sid":"0","msg":{"o":"w","e":[{"n":"/99/0/1","sv":"1795147200"}]}}`
- 응답 토픽 예: 요청 토픽 `dwd.v1/DAWONDNS-M130-W-2ef4322ee886/iot-server/timesync/json` → `dwd.v1/iot-server/DAWONDNS-M130-W-2ef4322ee886`
- 응답 로그(C4, Information): `Time sync response sent: device={DeviceKey}, topic={Topic}, sid={Sid}, value={Value}`
- 실패 로그(C4, Error): `Time sync response failed: device={DeviceKey}, topic={Topic}`(예외 포함, 서버가 없을 때는 예외 없이 같은 문구)

## 오류·위험

- 주입한 응답도 브로커의 `InterceptingPublishAsync`를 지나 `OnPublishAsync`로 들어온다. 서버 → 장치 토픽은 토큰이 3개라 `DeviceTopic.TryParse`가 실패하므로 핸들러로 다시 분배되지 않고 R7 Information 로그(`Unparsed topic passed through`)가 하나 더 남는다. 응답이 다시 `tisyn`을 부르는 고리는 생기지 않는다. 로그는 장치마다 동기 뒤 약 2시간에 한 번이라 그대로 둔다.
- `OnPublishAsync` 안에서(장치 PUBLISH 처리 중) `InjectApplicationMessage`를 기다린다. 주입은 같은 디스패치 경로로 위 R7 로그까지 처리한 뒤 돌아온다. 잠금을 잡은 채 부르는 콜백이 아니므로 교착은 예상하지 않지만, 통합 테스트(C1)로 실제 전달을 확인한다.
- 근거가 B540 분석이고 대상은 M130-W다. 형식 추정이 틀리면 C7이 "동기 안 됨"이 되며, 그 관찰을 기록하는 것으로 완료다(spec C7).
- 응답 값은 서버 시계에 의존한다. 서버 PC 시계가 틀리면 장치 시계도 틀린다. 서버 시계 동기는 이 작업 범위 밖이다.

## 검증 전략

- C1: `TimeSyncResponseTests` — 핸들러를 바꾸지 않은 앱에서 구독 클라이언트가 `dwd.v1/iot-server/{장치 키}/#`를 구독하고, 다른 클라이언트가 `tisyn`을 보내면 구독자가 서버 → 장치 토픽의 메시지 하나를 받는다. 받은 메시지의 `Retain`이 false다. 그 뒤 새 클라이언트가 같은 필터를 구독해도 일정 시간 안에 메시지를 받지 않는다(그 사이 같은 클라이언트의 QoS 1 발행 처리로 순서를 맞춤).
- C2: 같은 테스트에서 `TimeProvider`를 고정 시각으로 바꾸고, 받은 페이로드가 `{"sid":"7","msg":{"o":"w","e":[{"n":"/99/0/1","sv":"<고정 시각 epoch 초>"}]}}`와 같은지 문자열로 비교한다. `sid`가 없는 요청은 `"sid":"0"`. 고정 시각은 소수 초가 있는 값으로 골라 버림을 확인한다.
- C3: `DawonRelayMqttTests`에 중계 켜짐 테스트 — 핸들러를 바꾸지 않은 앱(기본 `LoggingDeviceMessageHandler`, 중계 켜짐이라 `DawonMqttRelay`가 주입됨)에서 가짜 다원 브로커로 접속한 장치가 자기 서버 → 장치 토픽을 구독하고 `tisyn`을 보낸 뒤, 같은 클라이언트의 QoS 1 발행이 처리될 때까지 서버에서 `/99/0/1` 응답을 받지 않고, 로그에 `Device message "tisyn"`은 있고 `Time sync response sent`는 없다.
- C4: C1 테스트에서 응답 로그에 장치 키·토픽·`sid`·값이 있음을 확인한다. 실패 경로는 단위 테스트 — `MqttServerAccessor.Server`가 null인 핸들러의 `TimeSyncAsync`가 예외 없이 끝나고 Error 수준의 `Time sync response failed` 로그를 남긴다(테스트용 `ILogger` 기록기).
- C5: 기준 spec을 읽어 R5·머리말·이력·`파일:` 줄을 확인한다(구현 후 검토에서도 diff로 봄).
- C6: `dotnet build`(경고 0)와 `dotnet test` 전체 실행 결과를 작업 문서에 적는다.
- C7: 배포(서버 로컬 배포 폴더, 중계 꺼짐 확인) 뒤 서버 로그에서 장비별 첫 응답 로그부터 60분의 `tisyn` 시각과 끊김 기록을 모아 spec C7대로 판정하고, 작업 문서와 근거 문서에 적는다.

## 원칙 점검

- 적용 규칙: Bizadm MCP `workflow/spec-driven.md`(절차·검토·기준 spec 반영), `git/github-issue.md`(#21), `workflow/repository-content.md`(저장소에 비밀 값·개인정보를 넣지 않음 — 테스트 장치 키는 가짜 MAC, 근거 문서에 실장비 MAC 전체를 적지 않음), 전역 `language-preference.md`(대화 한국어, 코드 주석은 저장소 관례대로 한국어), 저장소 기존 관례(MSTest, `TestApp`, 한국어 XML 주석, primary constructor)
- 충돌: 없음
