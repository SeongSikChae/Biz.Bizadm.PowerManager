# MQTT 구독·전달 기록 로그 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 서버 → 장치 토픽 판정

- [x] T001 `DeviceTopic`에 `public static bool IsToDevice(string topic)`를 더한다. 빈 토큰을 빼고 토큰이 3개 이상, 첫 토큰이 `.v1`로 끝나고 그 앞 접두가 비지 않음, 둘째 토큰이 `iot-server`이면 `true`. 한국어 XML 주석과 클래스 설명 주석(서버 → 장치 토픽의 접미 형태)을 고친다 (src/Biz.Bizadm.PowerManager.Core/Mqtt/DeviceTopic.cs) — C3, C5
- [x] T002 `IsToDevice` 경우별 테스트를 더한다. `true`: `dwd.v1/iot-server/K`, `dwd.v1/iot-server/K/write/json`, `/dwd.v1//iot-server/K`. `false`: `.v1/iot-server/K`, `dwd.v1/iot-server`, `dwd.v1/K/iot-server/json`, `/lwt`, `dwd/iot-server/K` (tests/Biz.Bizadm.PowerManager.Core.Tests/DeviceTopicTests.cs) — C3, C5

체크포인트: `dotnet test tests/Biz.Bizadm.PowerManager.Core.Tests --filter DeviceTopicTests`가 통과한다.

## 단계 2: 브로커 처리기

- [x] T003 `MqttBrokerHostedService`에 처리기 네 개를 더한다. 등록은 `StartAsync`에서 `server.StartAsync()` 전, 해제는 `DisposeAsync`에서 기존 처리기와 함께 한다.
  - 구독(`InterceptingSubscriptionAsync`): Information `MQTT client subscribe: clientId={ClientId}, topicFilter={TopicFilter}, qos={Qos}`. `Qos`는 `(int)TopicFilter.QualityOfServiceLevel`.
  - 구독 해제(`InterceptingUnsubscriptionAsync`): Information `MQTT client unsubscribe: clientId={ClientId}, topicFilter={TopicFilter}`(`Topic` 속성).
  - 큐에 넣음(`ApplicationMessageEnqueuedOrDroppedAsync`): `IsDropped`가 `false`이고 `DeviceTopic.IsToDevice(ApplicationMessage.Topic)`이면 Information `Downlink message enqueued: sender={SenderClientId}, receiver={ReceiverClientId}, topic={Topic}`.
  - 구독자 없음(`ApplicationMessageNotConsumedAsync`): `IsToDevice`이면 Warning `Downlink message not consumed: sender={SenderClientId}, topic={Topic}`(`SenderId` 속성).
  - 네 처리기 모두 이벤트 인자의 처리 여부 속성(`ProcessSubscription`·`ProcessUnsubscription`·`CloseConnection` 등)을 바꾸지 않는다.
  - await 없이 동기로 끝내고 `Task.CompletedTask`를 돌려준다(구독 해제 이벤트는 잠금 안에서 불림).
  - 몸체를 `try { … } catch (Exception)`으로 감싸 예외를 밖으로 내보내지 않는다. catch 안에서는 따로 감싼 Error 로그를 한 번 시도하고, 실패하면 무시한다.
  - 클래스 설명 주석에 구독·전달 기록을 더한다.

  (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs) — C1, C2, C3, C4, C5

체크포인트: `dotnet build Biz.Bizadm.PowerManager.slnx`가 경고 없이 끝나고, 기존 `MqttBrokerTests`·`TimeSyncResponseTests`가 통과한다.

## 단계 3: 브로커 테스트

- [x] T004 `MqttSubscribeLogTests`를 작성한다. `TestApp`으로 앱을 띄우고 MQTTnet 클라이언트(MQTT 3.1)로 확인하며, 로그는 로그 폴더 파일을 읽어 확인한다. 테스트 장치 키의 MAC은 가짜 값이다.
  - C1: 한 SUBSCRIBE에 필터 두 개를 QoS 1, QoS 0 순서로 보낸다(D005). 필터마다 결과 코드가 `GrantedQoS1`·`GrantedQoS0`인지 본다. 필터마다 `[Information]`의 `MQTT client subscribe: clientId="…", topicFilter="…", qos=1`/`qos=0` 줄을 본다(순서는 단언하지 않음). 그 필터 토픽으로 보낸 메시지를 받는지 본다.
  - C2: UNSUBSCRIBE한 필터마다 `MQTT client unsubscribe` 줄을 보고, 해제 뒤 그 토픽 메시지를 받지 않는지 본다. 해제 뒤 보낸 메시지가 없다는 것은 구독이 남은 다른 필터의 QoS 1 표시 메시지로 순서를 맞춰 확인한다.
  - C3 주입: 구독자가 `dwd.v1/iot-server/{키}/#`를 구독한 뒤 장치가 `tisyn`을 보내면 `[Information]` `Downlink message enqueued: sender="power-manager", receiver="<구독자 ClientId>", topic="dwd.v1/iot-server/{키}"` 줄을 본다.
  - C3 클라이언트: 다른 클라이언트가 `.../write/json`으로 보내면 sender가 그 ClientId인 줄을 본다.
  - C3 구독자 둘: 구독자가 둘이면 receiver별로 두 줄인지 본다.
  - C4: 구독자 없이 클라이언트가 서버 → 장치 토픽으로 보내면, 그리고 장치가 `tisyn`을 보내면(주입 응답) 각각 `[Warning]` `Downlink message not consumed: sender="<ClientId>"/"power-manager", topic="…"` 줄을 본다.
  - C5: 장치 → 서버 토픽(`dwd.v1/{키}/iot-server/json`)을 다른 클라이언트가 구독한 상태에서 장치가 보내도 그 토픽의 `Downlink message enqueued` 줄이 없는지 본다. `/lwt`와 빈 접두 `.v1/iot-server/x`로 보내도 그 토픽의 enqueued·not consumed 줄이 없는지 본다. 로그가 다 쓰였음은 같은 테스트에서 뒤에 보낸 서버 → 장치 메시지의 로그 줄이 나타나는 것으로 맞춘다.

  (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttSubscribeLogTests.cs) — C1, C2, C3, C4, C5

체크포인트: `dotnet test tests/Biz.Bizadm.PowerManager.Server.Spa.Tests --filter "MqttSubscribeLogTests|MqttBrokerTests|TimeSyncResponseTests"`가 통과한다.

## 단계 4: 기준 spec 반영

- [x] T005 기준 spec을 고친다.
  - R10~R13을 더한다. spec C1→R10, C2→R11, C3→R12, C4→R13으로 옮기고, 이 작업의 C번호를 가리키지 않고 직접 적는다. R12에는 서버 → 장치 토픽 정의와 "그 밖의 토픽에는 남기지 않음"을, R13에는 "서버 → 장치 토픽에만"을 넣는다.
  - 머리말에 구독·전달 로그 한 문장을 더한다.
  - `파일:` 줄에 `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttSubscribeLogTests.cs`를 더한다.
  - 이력에 `| 2026-11-21 | mqtt-subscribe-log | R10~R13 추가(구독·구독 해제, 서버 → 장치 메시지 큐 넣음·구독자 없음 로그) | docs/specs/mqtt-subscribe-log/ |`를 더한다.

  (docs/features/mqtt-broker.md) — C6

체크포인트: 기준 spec을 읽어 R10~R13·머리말·`파일:` 줄·이력이 spec C1~C6과 맞는다.

## 단계 5: 전체 검증

- [x] T006 `dotnet build Biz.Bizadm.PowerManager.slnx`(경고 0)와 `dotnet test --solution Biz.Bizadm.PowerManager.slnx`를 실행하고, 명령과 결과를 작업 문서에 적는다. 처리기 네 개가 예외를 밖으로 내보내지 않고, 처리 여부 속성을 바꾸지 않고, await가 없는지 코드로 확인해 함께 적는다 (저장소 전체) — C1, C5, C7

체크포인트: 빌드 경고 0, 테스트 실패 0, 코드 확인 결과가 작업 문서에 있다.

## 단계 6: 실장비 확인

- [x] T007 배포 준비와 반영을 한다.
  1. 서버 로컬 배포 폴더의 `config.yaml`에서 `DawonRelay` 섹션을 지운다(사용자 승인 뒤).
  2. `dotnet publish src/Biz.Bizadm.PowerManager.Server.Spa -p:PublishProfile=FolderProfile`을 실행한다.
  3. 실행 중인 서버를 멈추는 방법을 사용자와 정해 멈춘다.
  4. publish 결과를 배포 폴더에 복사한다. `config.yaml`·DB·`secrets.dek`·인증서·`log`는 덮어쓰지 않는다.
  5. 서버를 다시 시작한다(사용자와 정한 방법).

  (서버 로컬 배포 폴더) — C8
- [x] T008 장비(M130-W·B540)가 접속한 뒤 서버 로그를 모아 작업 문서에 적고 판정한다.
  - 장비별 `MQTT client subscribe` 줄(토픽 필터·QoS)
  - 첫 `tisyn` 응답의 `Downlink message enqueued` 또는 `not consumed` 줄
  - 판정: 구독 토픽 필터가 그 장비의 서버 → 장치 토픽에 맞는가, 응답이 그 장비 ClientId 큐에 넣어졌는가
  - 장비가 스스로 접속하지 않으면 전원 재투입을 사용자에게 부탁한다. 장치 상태를 바꾸는 명령은 보내지 않는다.
  - MAC 전체는 저장소에 적지 않는다.

  (작업 문서) — C8

체크포인트: 작업 문서에 장비별 구독·전달 로그와 판정이 있다.

## 의존 관계

- 단계 1 → 단계 2: 처리기가 `DeviceTopic.IsToDevice`를 쓴다. T002는 T001의 메서드를 테스트하므로 T001 뒤에 한다.
- 단계 2 → 단계 3: 테스트는 처리기 로그를 확인한다.
- 단계 3 → 단계 4: 기준 spec은 구현·테스트로 확인한 동작을 적는다(tasks 마지막 구현 단계).
- 단계 4 → 단계 5: 전체 검증은 기준 spec 반영까지 끝난 상태에서 한다.
- 단계 5 → 단계 6: 자동 테스트가 통과한 빌드를 배포한다. T008은 T007 뒤 장비 접속을 기다린다.
- 구현 후 검토(spec-driven 5절)는 4절 검증(단계 5·6) 뒤 기준점 e79a4b8부터의 diff로 한다.
