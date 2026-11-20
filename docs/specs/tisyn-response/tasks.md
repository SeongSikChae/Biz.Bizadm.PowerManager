# tisyn 시간 동기 응답 작업

형식: `- [x] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 브로커 참조 전달

- [x] T001 `MqttServerAccessor`(volatile `MqttServer? Server` 속성 하나) 작성, 한국어 XML 주석 (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttServerAccessor.cs) — C1
- [x] T002 생성자에 `MqttServerAccessor` 추가, `StartAsync`에서 `server.StartAsync()` 뒤 `accessor.Server = server`, `DisposeAsync`에서 이벤트를 떼기 전에 `accessor.Server = null` (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs) — C1, C4
- [x] T003 `builder.Services.AddSingleton<MqttServerAccessor>()`를 `IDeviceMessageHandler` 등록 앞에 추가 (src/Biz.Bizadm.PowerManager.Server.Spa/Program.cs) — C1

체크포인트: `dotnet build`가 경고 없이 끝나고, 기존 `MqttBrokerTests`·`DawonRelayMqttTests`가 통과한다.

## 단계 2: tisyn 응답

- [x] T004 생성자에 `MqttServerAccessor`, `TimeProvider`, `DawonMqttRelay? relay = null` 추가. `TimeSyncAsync`는 기존 `Log` 뒤 `relay is null`이면 응답을 보낸다: 토픽 `DeviceTopic.ToDevice(topic.Prefix, topic.DeviceKey)`, `sid` = `envelope.Sid ?? "0"`, 값 = `timeProvider.GetUtcNow().ToUnixTimeSeconds()`(InvariantCulture 문자열), 페이로드 = `JsonSerializer.Serialize(new MqttEnvelope(sid, new MqttMessage(MqttOperation.Write, [new MqttElement("/99/0/1", value)])))`, `InjectedMqttApplicationMessage`(Retain false, QoS 0, `SenderClientId = "power-manager"`). 성공 시 Information `Time sync response sent: device={DeviceKey}, topic={Topic}, sid={Sid}, value={Value}`. `accessor.Server`가 null이거나 주입이 예외를 내면 Error `Time sync response failed: device={DeviceKey}, topic={Topic}`을 남기고 다시 던지지 않음(종료 중 취소 `OperationCanceledException` + `ct.IsCancellationRequested`만 다시 던짐). 클래스 설명 주석 수정 (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/LoggingDeviceMessageHandler.cs) — C1, C2, C3, C4

체크포인트: `dotnet build`가 경고 없이 끝난다.

## 단계 3: 자동 테스트

- [x] T005 [P] `TimeSyncResponseTests` 작성. 핸들러를 바꾸지 않고 `TimeProvider`만 고정 시각(소수 초가 있는 값) 구현으로 바꾼 앱(`TestApp.StartAsync`의 `configureBuilder`)에서 (1) 구독 클라이언트가 `dwd.v1/iot-server/{장치 키}/#`를 구독하고 다른 클라이언트가 `sid` `"7"`인 `tisyn`을 `.../iot-server/timesync/json`으로 보내면 구독자가 토픽 `dwd.v1/iot-server/{장치 키}`, `Retain` false, 페이로드 `{"sid":"7","msg":{"o":"w","e":[{"n":"/99/0/1","sv":"<고정 시각 epoch 초>"}]}}`인 메시지 하나를 받는다, (2) `sid`가 없는 요청은 `"sid":"0"`, (3) 응답 뒤 새로 같은 필터를 구독한 클라이언트는 retained 메시지를 받지 않는다(그 클라이언트의 QoS 1 발행 처리로 순서를 맞춘 뒤 받은 것이 없음), (4) 로그에 `Time sync response sent`와 장치 키·토픽·`sid`·값이 있다. 실패 경로 단위 테스트: `Server`가 null인 `MqttServerAccessor`로 만든 `LoggingDeviceMessageHandler.TimeSyncAsync`가 예외 없이 끝나고 Error 수준 `Time sync response failed` 기록이 남는다(테스트용 `ILogger` 기록기). 테스트 장치 키의 MAC은 가짜 값 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TimeSyncResponseTests.cs) — C1, C2, C4
- [x] T006 [P] 중계 켜짐 테스트 추가. 핸들러를 바꾸지 않는 시작 경로(도우미 `StartAsync`에 교체하지 않는 인자를 두거나 `TestApp.StartAsync` 직접 호출)로 기본 `LoggingDeviceMessageHandler`를 쓰는 앱에서, 가짜 다원 브로커로 접속한 장치가 자기 서버 → 장치 토픽(`/#`)을 구독하고 `tisyn`을 보낸 뒤 같은 클라이언트의 QoS 1 발행이 처리될 때까지 `/99/0/1` 응답을 받지 않고, 로그에 `Device message "tisyn"`은 있고 `Time sync response sent`는 없다 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DawonRelayMqttTests.cs) — C3

체크포인트: 새 테스트와 기존 브로커·중계 테스트가 통과한다.

## 단계 4: 기준 spec 반영

- [x] T007 R5를 spec 「기준 spec mqtt-broker에 미치는 변경」대로 고친다(받은 내용 Information 로그 유지, 중계 꺼짐이면 `tisyn`마다 서버 → 장치 토픽에 retain 없이 `w` `/99/0/1`(요청 `sid`, 없으면 `"0"`, UTC Unix epoch 초 10진수 문자열)을 보내고 응답 로그를 남김, 중계 켜짐이면 응답하지 않음, 명령 발행 `MqttDeviceCommandPublisher`의 `w`·`chg`·`reb`는 `NotImplementedException`). 이 작업의 C번호를 가리키지 않고 직접 적는다. 머리말의 "지금 핸들러는 로그만 남기고" 문장을 맞추고, `파일:` 줄에 `MqttServerAccessor.cs`·`TimeSyncResponseTests.cs`를 더하고, 이력 표에 `| 2026-11-20 | tisyn-response | R5 바꿈(tisyn 응답, 중계 켜짐이면 응답 안 함) | docs/specs/tisyn-response/ |`를 더한다 (docs/features/mqtt-broker.md) — C5

체크포인트: 기준 spec을 읽어 R5·머리말·`파일:` 줄·이력이 spec C5와 맞는다.

## 단계 5: 전체 검증

- [x] T008 `dotnet build`(경고 0)와 `dotnet test` 전체를 실행하고 명령·결과를 작업 문서에 적는다 (저장소 전체) — C6

체크포인트: 빌드 경고 0, 테스트 실패 0.

## 단계 6: 실장비 확인

- [ ] T009 서버 로컬 배포 폴더의 `config.yaml`에서 `DawonRelay.Enabled`가 꺼져 있음을 확인하고, `dotnet publish`(FolderProfile) 결과를 서버 로컬 배포 폴더에 반영한다. 실행 중인 서버를 멈추고 다시 시작하는 일은 사용자에게 알리고 승인 뒤에 한다(설정·DB·비밀 키·인증서는 덮어쓰지 않음) (서버 로컬 배포 폴더) — C7
- [ ] T010 다시 시작한 뒤 M130-W 장비별 첫 `Time sync response sent` 로그부터 60분 동안 서버 로그에서 그 장비의 `tisyn` 시각과 끊김 기록(R9 접속 로그·R7 `"/lwt"` 통과 로그·R4 `Device offline (LWT)` 로그, MAC 12자리로 맞춤)을 모아 spec C7대로 판정(간격 하나라도 20분 초과 = 동기 완료, 모두 20분 안 = 동기 안 됨, 끊김이 있으면 다음 응답부터 다시 잼)하고 시각·간격·판정을 작업 문서에 적는다 (작업 문서) — C7
- [ ] T011 T010 결과를 근거 문서 4절 미확인 항목(시간 동기 응답 값 키·`sid`, 동기 뒤 재송신 여부)과 5절 M130-W 관찰에 관찰/추정 표기로 적는다. 실장비 MAC 전체는 적지 않는다(장치 1·2·3 표기) (docs/reference/b540-mqtt-topic.md) — C7

체크포인트: 작업 문서와 근거 문서에 판정 결과가 있다.

## 의존 관계

- 단계 1 → 단계 2: 핸들러가 `MqttServerAccessor`를 주입받고, 호스티드 서비스가 그 값을 채워야 응답할 수 있다.
- 단계 2 → 단계 3: 테스트는 응답 구현을 확인한다. T005와 T006은 서로 다른 파일이라 동시에 할 수 있다.
- 단계 3 → 단계 4: 기준 spec은 구현·테스트로 확인한 동작을 적는다(tasks 마지막 구현 단계, spec-driven 3절 2번).
- 단계 4 → 단계 5: C6 전체 검증은 기준 spec 반영까지 끝난 상태에서 한다.
- 단계 5 → 단계 6: 자동 테스트가 통과한 빌드를 배포한다. T010은 T009 배포 뒤 최소 60분이 걸리고, T011은 T010 결과를 쓴다.
- 구현 후 검토(spec-driven 5절)는 단계 6까지 끝난 뒤 기준점부터의 diff로 한다(T011의 근거 문서 수정도 들어감).
