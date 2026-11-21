# MQTT 구독·전달 기록 로그 계획

## 요약

`MqttBrokerHostedService`가 MQTTnet 5.2 서버 이벤트 넷에 처리기를 달아 로그만 남긴다.

- 구독·구독 해제: `InterceptingSubscriptionAsync`·`InterceptingUnsubscriptionAsync`
- 큐에 넣음: `ApplicationMessageEnqueuedOrDroppedAsync`
- 구독자 없음: `ApplicationMessageNotConsumedAsync`

처리기는 이벤트 인자의 처리 여부 속성(`ProcessSubscription` 등)을 건드리지 않아 구독·전달 동작은 그대로다.

서버 → 장치 토픽 판정은 `DeviceTopic`에 정적 메서드 `IsToDevice`로 둔다. 토픽 규칙을 `TryParse`와 한곳에 모으기 위해서다.

자동 테스트는 새 테스트 클래스에서 실제 브로커(테스트 앱)와 MQTTnet 클라이언트로 확인한다. 끝으로 기준 spec을 반영하고, 배포해 실장비로 확인한다.

## 바꿀 파일·구조

- `src/Biz.Bizadm.PowerManager.Core/Mqtt/DeviceTopic.cs`: `public static bool IsToDevice(string topic)`를 더한다. 빈 토큰을 빼고 토큰이 3개 이상, 첫 토큰이 `.v1`로 끝나고 접두가 비지 않음, 둘째 토큰이 `iot-server`이면 `true`. 클래스 설명 주석에 서버 → 장치 토픽의 접미 형태를 더한다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs`: 처리기 네 개를 더한다.
  - `StartAsync`에서 `server.StartAsync()` 전에 기존 처리기와 함께 등록하고, `DisposeAsync`에서 기존 처리기와 함께 뗀다.
  - 클래스 설명 주석에 구독·전달 기록을 더한다.
- `tests/Biz.Bizadm.PowerManager.Core.Tests/DeviceTopicTests.cs`: `IsToDevice` 경우별 테스트를 더한다.
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttSubscribeLogTests.cs`(새 파일): C1~C5 브로커 테스트.
- `docs/features/mqtt-broker.md`: 기준 spec 반영.
  - R10~R13을 더한다(spec C1~C4를 R번호로 옮기고, C5의 "다른 토픽에는 로그 없음"은 R12·R13 문장에 넣는다).
  - 머리말에 구독·전달 로그 한 문장을 더한다.
  - `파일:` 줄에 `MqttSubscribeLogTests.cs`를 더한다(`DeviceTopic.cs`·`DeviceTopicTests.cs`·`MqttBrokerHostedService.cs`는 이미 있음).
  - 이력에 한 줄을 더한다.

## 결정

- 구독 로그는 `InterceptingSubscriptionAsync`를 쓴다. 이 이벤트는 SUBSCRIBE 안 토픽 필터마다 한 번씩 오고 ClientId·토픽 필터·요청 QoS를 준다. / 검토한 대안: `ClientSubscribedTopicAsync`는 처리 뒤에 와서 결과를 반영하지만, 구독이 거부되는 경로가 없는 지금은 차이가 없다. 거부·실패 때도 요청을 남기는 편이 진단에 낫다.
- 구독 해제는 `InterceptingUnsubscriptionAsync`를 쓴다(같은 이유. `ClientUnsubscribedTopicAsync`는 대안).
- 큐에 넣음은 `ApplicationMessageEnqueuedOrDroppedAsync`에서 `IsDropped`가 `false`이고 `IsToDevice(ApplicationMessage.Topic)`일 때만 남긴다. `IsDropped`가 `true`인 경우는 로그를 남기지 않는다(D002. 기본 정책 `DropOldestQueuedMessage`에서는 생기지 않음). / 검토한 대안: `InterceptingClientEnqueueAsync`는 넣기 전 가로채기라 넣었다는 사실이 아니고, `AcceptEnqueue`를 건드릴 위험이 있다.
- 구독자 없음은 `ApplicationMessageNotConsumedAsync`에서 `IsToDevice(ApplicationMessage.Topic)`일 때만 Warning으로 남긴다. 보낸 ClientId는 `SenderId`다.
- 서버 → 장치 판정은 `DeviceTopic.IsToDevice`(Core)에 둔다. / 검토한 대안: 브로커 서비스 안의 private 메서드는 토픽 규칙이 두 곳으로 갈린다.
- 로그 문구(Serilog 메시지 템플릿, 구조화 속성)는 아래와 같다. 페이로드는 넣지 않는다. 서버 → 장치 페이로드는 이미 R7 통과 로그에 있다.
  - 구독(Information): `MQTT client subscribe: clientId={ClientId}, topicFilter={TopicFilter}, qos={Qos}`. `Qos`는 정수 0·1·2.
  - 구독 해제(Information): `MQTT client unsubscribe: clientId={ClientId}, topicFilter={TopicFilter}`
  - 큐에 넣음(Information): `Downlink message enqueued: sender={SenderClientId}, receiver={ReceiverClientId}, topic={Topic}`
  - 구독자 없음(Warning): `Downlink message not consumed: sender={SenderClientId}, topic={Topic}`

## 인터페이스·데이터

- `DeviceTopic.IsToDevice(string topic): bool`. 예:
  - `true`: `dwd.v1/iot-server/K`, `dwd.v1/iot-server/K/write/json`, `/dwd.v1//iot-server/K`(빈 토큰 무시)
  - `false`: `.v1/iot-server/K`(빈 접두), `dwd.v1/iot-server`(토큰 2개), `dwd.v1/K/iot-server/json`(장치 → 서버), `/lwt`, `dwd/iot-server/K`(`.v1` 아님)
- 로그 형식: 서버 Serilog 파일 로그. 문자열 속성은 따옴표가 붙어 출력된다(기존 로그와 같음, 예 `clientId="DAWONDNS-…"`).

## 오류·위험

- 처리기 예외: MQTTnet 5.2는 네 이벤트를 예외를 잡지 않는 `InvokeAsync`로 부른다(디컴파일 확인).
  - 전달 이벤트의 예외는 분배 루프를 멈춘다. 남은 구독자 전달과 구독자 없음 이벤트가 막힌다.
  - 구독 이벤트의 예외는 SUBSCRIBE·UNSUBSCRIBE 처리로 번진다.
  - 그래서 네 처리기 모두 몸체를 `try { … } catch (Exception)`으로 감싸 예외를 밖으로 내보내지 않는다. catch에서는 따로 감싼 Error 로그를 한 번 시도하고, 그것도 실패하면 무시한다.
- 성능:
  - 큐에 넣음 이벤트는 메시지 × 구독자마다 온다. 판정이 문자열 분리 한 번이고 장치 수가 적어 영향이 작다.
  - 장치 → 서버 메시지는 구독자가 없어(서버 핸들러는 구독자가 아님) 메시지마다 구독자 없음 이벤트가 온다. `IsToDevice` 판정(문자열 분리 한 번)으로 걸러 로그는 남기지 않는다.
  - 그래서 이 처리기는 모든 상향 메시지 경로에 있고, 위의 예외 차단이 특히 필요하다.
- 주입 메시지의 보낸 ClientId: `InjectedMqttApplicationMessage.SenderClientId`(`power-manager`)가 `SenderClientId`·`SenderId`로 나오는지는 MQTTnet 문서에 없다. 테스트로 확인하고, 다르면 구현 결함으로 보고 처리한다(spec C3은 `power-manager`를 요구).
- 구독자 없음 이벤트가 주입 메시지에도 오는지 미확인이다. 테스트로 확인한다.
- C8 배포:
  - 배포 폴더 `config.yaml`에 `DawonRelay` 섹션이 남아 있어 새 빌드가 시작하지 않는다(dawon-relay-removal C3). 섹션 삭제는 사용자 승인 뒤에 한다.
  - 서버는 사용자가 직접 띄운 것이라, 멈추고 다시 시작하는 방법도 사용자와 정한다.

## 검증 전략

- C1: `MqttSubscribeLogTests`
  - 한 SUBSCRIBE에 필터 두 개를 QoS가 높은 순서(QoS 1, QoS 0)로 보낸다(D005). MQTTnet 5.2 서버는 필터를 QoS 내림차순으로 처리해 SUBACK 결과 코드를 그 순서로 담고, 클라이언트는 요청 순번대로 짝짓는다(기존 동작).
  - 필터마다 결과 코드가 `GrantedQoS1`·`GrantedQoS0`이다.
  - 로그에 필터마다 `MQTT client subscribe` 줄이 ClientId·필터·QoS와 함께 Information으로 남는다. 로그 순서는 단언하지 않는다.
  - 그 필터로 메시지를 받는다.
- C2: 같은 클래스. UNSUBSCRIBE한 필터마다 `MQTT client unsubscribe` 줄이 남고, 해제 뒤 그 토픽 메시지를 받지 않는다.
- C3: 같은 클래스.
  - 구독자가 `dwd.v1/iot-server/{키}/#`를 구독한 뒤 장치가 `tisyn`을 보내면 `Downlink message enqueued: sender="power-manager", receiver="<구독자 ClientId>", topic="dwd.v1/iot-server/{키}"`가 Information으로 남는다.
  - 다른 클라이언트가 `.../write/json`으로 보내면 sender가 그 ClientId인 줄이 남는다.
  - 구독자 둘이면 receiver별로 두 줄이 남는다.
- C4: 같은 클래스. 구독자 없이 클라이언트가 서버 → 장치 토픽으로 보내면, 그리고 장치가 `tisyn`을 보내면(주입 응답) `Downlink message not consumed`가 Warning으로 sender와 함께 남는다.
- C5:
  - 같은 클래스. 장치 → 서버 토픽(`dwd.v1/{키}/iot-server/json`)을 다른 클라이언트가 구독한 상태에서 장치가 보내도 enqueued 줄이 없다.
  - `/lwt`·빈 접두 `.v1/iot-server/x`로 보내도 enqueued·not consumed 줄이 없다.
  - 기존 `MqttBrokerTests`·`TimeSyncResponseTests`가 그대로 통과한다.
  - C1·C5 공통: 처리기가 예외를 밖으로 내보내지 않음은 코드 검토로 확인한다. 로거를 실패시키는 자동 테스트는 앱 전체 로깅을 깨뜨려 두지 않는다.
  - `DeviceTopicTests`에 `IsToDevice` 경우별 테스트를 둔다.
- C6: 기준 spec을 읽어 R10~R13·머리말·`파일:` 줄·이력이 spec과 맞는지 대조한다.
- C7: `dotnet build Biz.Bizadm.PowerManager.slnx`(경고 0), `dotnet test --solution Biz.Bizadm.PowerManager.slnx`(실패 0).
- C8: 다음 순서로 확인한다.
  1. 사용자 승인 뒤 배포 `config.yaml`의 `DawonRelay` 섹션을 지운다.
  2. `dotnet publish`(Server.Spa FolderProfile) 결과를 서버 로컬 배포 폴더에 복사한다. `config.yaml`·DB·`secrets.dek`·인증서·`log`는 덮어쓰지 않는다.
  3. 사용자와 정한 방법으로 서버를 다시 시작한다.
  4. 장비가 접속한 뒤 서버 로그에서 장비별 `MQTT client subscribe`와 첫 `tisyn` 응답의 `Downlink message enqueued`/`not consumed`를 모아 작업 문서에 적고 판정한다(MAC은 저장소에 적지 않음).

## 원칙 점검

- 적용 규칙:
  - `workflow/spec-driven.md`(변경 문서·검토·기준 spec 반영)
  - `git/github-issue.md`(#23 참조, 범위 변경 시 이슈 갱신 — D001로 갱신함)
  - `workflow/repository-content.md`(저장소에 로컬 경로·MAC 전체를 적지 않음. 배포 폴더는 "서버 로컬 배포 폴더"로 적음)
  - 전역 `language-preference.md`(대화는 한국어, 코드·로그 문구는 영어)
  - 메모 `device-target-confirmation`(장치 상태를 바꾸는 명령은 보내지 않음. C8은 로그 관찰만)
  - 저장소 기존 관례: 한국어 XML 주석, 로그 메시지 영어, MSTest
- 충돌: 없음
