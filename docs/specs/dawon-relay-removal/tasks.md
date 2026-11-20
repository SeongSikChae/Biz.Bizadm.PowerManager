# 다원 중계 모드 제거 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: Server.Spa 중계 코드 제거

- [x] T001 중계 분기 제거: `DawonRelay` 속성·검증 줄·`using ...Relay` 삭제 (src/Biz.Bizadm.PowerManager.Server.Spa/AppConfiguration.cs) — C1, C3, C4
- [x] T002 `c.DawonRelay?.IsEnabled` 분기와 `AddDawonRelay` 메서드, `using ...Relay`·`System.Net.Security`·`System.Security.Authentication` 삭제 (src/Biz.Bizadm.PowerManager.Server.Spa/Program.cs) — C1, C4
- [x] T003 `[BufferRequestBody]`, register·connect의 `RestRelay` 분기, `RestRelay` 속성과 그 요약 주석, `using ...Relay` 삭제. `RegisterJsonOptions` 요약은 남김 (src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DevicesController.cs) — C1, C4
- [x] T004 `StoreRelayedKeyAsync`·`StoreRelayedKeyOnceAsync` 삭제 (src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceRegistrationService.cs) — C1, C4
- [x] T005 생성자 매개변수 `relay`, 시작·종료의 중계 이벤트 연결·해제와 `relay.DisposeAsync()`, `relay.OnValidatingConnectionAsync` 호출, `IsInjected` 검사, `using ...Relay` 삭제 (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs) — C1, C4
- [x] T006 생성자 매개변수 `relay`와 `TimeSyncAsync`의 `if (relay is null)` 조건 삭제(늘 응답), 클래스 요약·`SenderClientId` 주석의 중계 문장 삭제, `using ...Relay` 삭제 (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/LoggingDeviceMessageHandler.cs) — C1, C4
- [x] T007 `Relay/` 폴더의 7개 파일 삭제 (src/Biz.Bizadm.PowerManager.Server.Spa/Relay/) — C1

체크포인트: `dotnet build src/Biz.Bizadm.PowerManager.Server.Spa` 성공(테스트 프로젝트는 단계 2 뒤에 빌드).

## 단계 2: 중계 테스트 제거

- [x] T008 [P] 중계 전용 테스트 파일 5개 삭제 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DawonRelayMqttTests.cs, DawonRelayRestTests.cs, DawonRelayRecorderTests.cs, FakeDawon.cs, TopLevelJsonTests.cs) — C2
- [x] T009 [P] 중계 설정 테스트 4개 삭제 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/AppConfigurationTests.cs) — C2

체크포인트: `dotnet build Biz.Bizadm.PowerManager.slnx` 성공, C1 검색(`src`·`tests` C# 파일의 중계 식별자, Server.Spa src·tests의 `중계`·`dawon-relay`) 결과 없음.

## 단계 3: 가짜 장비 주석

- [x] T010 [P] `ToMqtt31ReturnCode` 주석의 "(중계와 같은 표)" 삭제 (src/Biz.Bizadm.PowerManager.FakeDevice.App/Mqtt/DeviceMqttClient.cs) — C7
- [x] T011 [P] 클래스 요약을 "형식은 기준 spec dawon-fake-device R9를 따른다"로 바꿈 (src/Biz.Bizadm.PowerManager.FakeDevice.App/Recording/FakeDeviceRecorder.cs) — C7
- [x] T012 [P] 요약에서 Server.Spa 중계 클래스 언급을 지우고 가림 용도만 설명 (src/Biz.Bizadm.PowerManager.FakeDevice.App/Recording/TopLevelJson.cs) — C7

체크포인트: `src/Biz.Bizadm.PowerManager.FakeDevice.App`에서 `중계`·`dawon-relay` 검색 결과 없음.

## 단계 4: 기준 spec 반영

- [x] T013 [P] `파일:` 줄을 `없음(…)`으로, 머리말을 제거된 기능 설명으로 바꾸고 요구 목록을 비움, 이력에 "R1~R13 뺌(중계 모드 제거)" 추가 (docs/features/dawon-relay.md) — C6
- [x] T014 [P] 머리말과 R1·R2·R6·R8의 중계 단서 삭제, 이력 추가 (docs/features/device-register.md) — C6
- [x] T015 [P] 머리말과 R1·R9의 중계 단서 삭제, R5를 `tisyn`마다 응답하는 문장으로 바꿈, 이력 추가 (docs/features/mqtt-broker.md) — C6
- [x] T016 [P] 머리말·R9의 dawon-relay 참조를 지우고 R9에 채널·kind 값을 `FakeDeviceRecorder` 그대로 적음, 이력 "R9 바꿈(형식 값을 직접 적음, 동작은 그대로)" 추가 (docs/features/dawon-fake-device.md) — C6

체크포인트: 기준 spec 네 개를 spec.md C6 항목별로 대조.

## 단계 5: 검증

- [x] T017 솔루션 빌드와 테스트 프로젝트 6개 전체 테스트 실행(`dotnet build Biz.Bizadm.PowerManager.slnx`, `dotnet test --solution Biz.Bizadm.PowerManager.slnx`) — C4, C5
- [x] T018 빌드한 서버 실행 파일을 스크래치 임시 설정 두 개(`DawonRelay` 섹션 있음·없음)로 실행해 종료 코드와 메시지 확인 — C3
- [x] T019 C1·C2·C7 검색과 파일 존재 확인, C6 대조 — C1, C2, C6, C7

체크포인트: plan.md 검증 전략의 모든 항목 결과를 C번호와 함께 기록.

## 의존 관계

- 단계 1의 T001~T006은 같은 프로젝트에서 `Relay` 형식을 쓰는 곳을 지우는 작업이라, T007(형식 정의 삭제)은 그 뒤에 한다. T001~T006은 서로 다른 파일이지만 모두 끝나야 빌드되므로 [P]를 붙이지 않고 차례로 한다.
- 단계 2는 단계 1 뒤에 한다(지울 테스트가 `Relay` 형식을 쓰므로 단계 1 체크포인트에서 테스트 프로젝트는 빌드하지 않음).
- 단계 3·4는 코드 빌드와 상관없어 단계 1·2와 별개로 할 수 있다. 단계 4는 규칙대로 마지막 구현 단계로 둔다.
- 단계 5는 모든 구현 단계 뒤에 한다.
