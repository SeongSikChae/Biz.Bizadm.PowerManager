# 다원 중계 모드 제거 계획

## 요약

#20(커밋 3bac44e)이 더한 중계 모드를 걷어 낸다. `Relay/` 폴더와 중계 전용 테스트는 지우고, 기존 파일에 끼워 넣은 중계 분기·매개변수·설정·주석은 중계 모드가 꺼져 있을 때 타던 경로만 남기도록 지운다. #21(tisyn 응답)이 중계 모드를 보고 응답을 건너뛰던 조건도 지워 늘 응답하게 한다. 기준 spec 네 개와 가짜 장비 주석을 맞춘다. 남은 `DawonRelay` 설정 섹션은 받아 주지 않는다(D001).

## 바꿀 파일·구조

지움:
- `src/Biz.Bizadm.PowerManager.Server.Spa/Relay/BufferRequestBodyAttribute.cs`
- `src/Biz.Bizadm.PowerManager.Server.Spa/Relay/DawonMqttRelay.cs`
- `src/Biz.Bizadm.PowerManager.Server.Spa/Relay/DawonRelayConfiguration.cs`
- `src/Biz.Bizadm.PowerManager.Server.Spa/Relay/DawonRelayRecorder.cs`
- `src/Biz.Bizadm.PowerManager.Server.Spa/Relay/DawonRestRelay.cs`
- `src/Biz.Bizadm.PowerManager.Server.Spa/Relay/MqttConnectPacketReader.cs`
- `src/Biz.Bizadm.PowerManager.Server.Spa/Relay/TopLevelJson.cs`
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DawonRelayMqttTests.cs`(#21의 중계 모드 tisyn 무응답 테스트 포함)
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DawonRelayRestTests.cs`
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DawonRelayRecorderTests.cs`
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/FakeDawon.cs`
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TopLevelJsonTests.cs`

고침:
- `src/Biz.Bizadm.PowerManager.Server.Spa/AppConfiguration.cs`: `DawonRelay` 속성과 `Validate()`의 검증 줄, `using ...Relay`를 지운다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Program.cs`: `c.DawonRelay?.IsEnabled` 분기와 `AddDawonRelay` 메서드, 그것만 쓰던 `using`(`...Relay`, `System.Net.Security`, `System.Security.Authentication`)을 지운다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DevicesController.cs`: `[BufferRequestBody]`, register·connect의 `RestRelay` 분기, `RestRelay` 속성과 그 요약 주석, `using ...Relay`를 지운다. `RegisterJsonOptions` 요약 주석은 그대로 둔다(지금 `RestRelay` 요약이 그 사이에 끼어 있음).
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceRegistrationService.cs`: `StoreRelayedKeyAsync`·`StoreRelayedKeyOnceAsync`를 지운다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs`: 생성자 매개변수 `DawonMqttRelay? relay`, 시작·종료의 중계 이벤트 연결·해제와 `DisposeAsync`, 접속 검증 끝의 `relay.OnValidatingConnectionAsync` 호출, `OnPublishAsync`의 `DawonMqttRelay.IsInjected` 검사, `using ...Relay`를 지운다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/LoggingDeviceMessageHandler.cs`: 생성자 매개변수 `DawonMqttRelay? relay`와 `TimeSyncAsync`의 `if (relay is null)` 조건을 지워 늘 응답하게 하고, 클래스 요약과 `SenderClientId` 주석에서 중계 문장을 지운다. `using ...Relay`를 지운다.
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/AppConfigurationTests.cs`: 중계 설정 테스트 4개(`Validate_WithoutDawonRelay_LeavesNull`, `Validate_EmptyDawonRelay_FillsDefaults`, `Validate_EnabledDawonRelay_KeepsValues`, `Validate_EnabledDawonRelayInvalid_Throws`)를 지운다. 테스트는 더하지 않는다(C3 확인 방법은 아래 결정).
- `src/Biz.Bizadm.PowerManager.FakeDevice.App/Mqtt/DeviceMqttClient.cs`: `ToMqtt31ReturnCode` 주석의 "(중계와 같은 표)"를 지운다.
- `src/Biz.Bizadm.PowerManager.FakeDevice.App/Recording/FakeDeviceRecorder.cs`: 클래스 요약의 "형식은 중계 모드(dawon-relay R9·R10)와 같다(D018)"를 "형식은 기준 spec dawon-fake-device R9를 따른다"로 바꾼다.
- `src/Biz.Bizadm.PowerManager.FakeDevice.App/Recording/TopLevelJson.cs`: 요약의 "Server.Spa 중계의 같은 이름 클래스 가운데 가림에 필요한 부분만 둔다(D018)"를 가림 용도만 설명하는 문장으로 바꾼다.
- `docs/features/dawon-relay.md`: `파일:` 줄을 `없음(중계 모드 제거로 모든 파일을 지웠거나 다른 기능에만 속함)`으로, 머리말을 제거된 기능임을 밝히는 문장(제거 이유와 대신 쓰는 dawon-fake-device)으로 바꾸고, 요구 목록을 비운다. 이력에 이번 작업 한 줄("R1~R13 뺌(중계 모드 제거)")을 더한다.
- `docs/features/device-register.md`: 머리말의 중계 모드 문장과 R1·R2·R6·R8의 중계 단서를 지운다. 이력에 한 줄을 더한다.
- `docs/features/mqtt-broker.md`: 머리말의 중계 모드 문장, R1·R9의 중계 단서를 지우고, R5를 "`tisyn`을 받을 때마다 … 보낸다"로 바꾼다(중계 조건과 "켜져 있으면 응답하지 않는다" 문장 삭제). 이력에 한 줄을 더한다.
- `docs/features/dawon-fake-device.md`: 머리말 "dawon-relay와 같은 형식의 JSONL"과 R9 "형식은 dawon-relay R9·R10과 같은 형식을 쓴다"를 고쳐, R9에 채널 값과 채널별 kind 값을 `FakeDeviceRecorder` 그대로 적는다(`rest`: `request`·`response`·`error`·`verify`, `mqtt`: `connect`·`connack`·`subscribe`·`suback`·`publish`·`disconnect`·`error`, `fota`: `candidate`·`request`·`response`·`error`). 이력에 "R9 바꿈(형식 값을 직접 적음, 동작은 그대로)"을 더한다.

바꾸지 않음(근거):
- `src/Biz.Bizadm.PowerManager.Server.Spa/Biz.Bizadm.PowerManager.Server.Spa.csproj`의 `MQTTnet` 패키지 참조: #20에서 더했지만 지금 `LoggingDeviceMessageHandler`가 `MQTTnet` 이름공간(`MqttApplicationMessage` 등)을 직접 쓴다.
- `MqttServerAccessor`, `TimeSyncResponseTests`: #21의 tisyn 응답에 쓰인다. `TimeSyncResponseTests`는 `LoggingDeviceMessageHandler`를 선택 인자 없이 3개 인자로 만들므로 매개변수를 지워도 그대로 컴파일된다.
- `docs/specs/dawon-relay/`, `docs/specs/tisyn-response/`, `docs/specs/dawon-fake-device/`: 끝난 작업 기록이다.

## 결정

- 지운 뒤 남는 코드 경로는 지금의 중계 꺼짐 경로를 그대로 쓰고 새 코드는 쓰지 않는다: 다른 방식으로 다시 짤 이유가 없고, 동작을 그대로 두는 것이 이 작업의 범위다 / 검토한 대안: 정리하는 김에 주변 코드 개선 — 범위 밖이라 고르지 않음.
- 남은 `DawonRelay` 설정 섹션은 받아 주지 않고 기본 역직렬화 오류(설정 오류, 종료 코드 2)로 둔다 (D001).
- C3는 자동 테스트를 더하지 않고 빌드한 서버 실행 파일로 확인한다: C1이 `src`·`tests` C# 파일에 `DawonRelay` 검색 결과가 없기를 요구하므로, 키 이름을 담은 테스트는 C1과 어긋난다. 모르는 키를 오류로 보는 것은 지금 쓰는 기본 YamlDotNet `Deserializer` 동작이고 이번 작업이 바꾸지 않는다 / 검토한 대안: 키 이름이 든 테스트를 더하고 C1에 예외를 둠 — 확정한 spec C1을 바꿔야 해서 고르지 않음. 키 이름을 문자열 조각으로 이어 붙여 검색을 피함 — 검색을 통과시키려고 코드를 꼬는 것이라 고르지 않음.
- `MQTTnet` 패키지 참조는 남긴다: 위 근거. / 검토한 대안: #20이 더한 것이니 지움 — `MQTTnet.Server`가 간접으로 가져오지만 직접 쓰는 이름공간의 참조를 숨기게 되어 고르지 않음.

## 오류·위험

- 운영 설정에 `DawonRelay:` 줄이 남아 있으면 서버가 시작하지 않는다(D001, 의도된 동작). 커밋 보고에 알린다.
- 생성자 매개변수를 지우므로, 그 매개변수를 넘기던 코드가 남아 있으면 빌드가 깨진다. 지울 테스트 밖에서 넘기는 곳은 없다(`TimeSyncResponseTests`는 넘기지 않음). 빌드로 확인한다.
- `OnPublishAsync`의 `IsInjected` 검사는 `dawon-relay/` 접두 ClientId만 걸렀다. 서버 자신의 tisyn 응답(보낸 쪽 `power-manager`)은 원래 이 검사에 걸리지 않았으므로 지워도 처리 경로가 바뀌지 않는다.
- `DevicesController`에서 `[BufferRequestBody]`를 지우면 요청 본문 버퍼링이 사라진다. 중계 분기 밖에서는 본문을 다시 읽지 않으므로 영향이 없다. 장치 등록 테스트로 확인한다.

## 검증 전략

- C1: `src`·`tests`의 C# 파일에서 `DawonRelay`·`DawonMqttRelay`·`DawonRestRelay`·`DawonRelayRecorder`·`StoreRelayedKeyAsync`·`BufferRequestBody`·`IsInjected`를 검색해 결과가 없음을 확인한다. Server.Spa src·tests의 C# 파일에서 `중계`·`dawon-relay`를 검색한다. `Relay/` 폴더가 없음을 확인한다.
- C2: 지울 테스트 파일 다섯 개가 없고, `AppConfigurationTests.cs`에 중계 설정 테스트 4개가 없음을 확인한다.
- C3: 빌드한 서버 실행 파일(`src/Biz.Bizadm.PowerManager.Server.Spa/bin/Debug/net10.0/`)을 스크래치의 임시 설정으로 두 번 실행한다. `DawonRelay` 섹션이 있는 설정은 종료 코드 2와 `Configuration error` 메시지(키 이름 포함)를 확인한다. 같은 설정에서 그 섹션만 뺀 것은 역직렬화를 지나 다음 단계(OsKms 검증 등)로 넘어가 `DawonRelay` 관련 메시지가 나오지 않음을 확인한다. 섹션 없는 정상 설정의 시작은 기존 `AppConfigurationTests`와 `TestApp`을 쓰는 테스트로 확인한다. `Program.RunAsync`의 예외 처리가 바뀌지 않았음을 diff로 확인한다.
- C4: `DeviceEndpointTests`·`MqttBrokerTests`·`TimeSyncResponseTests`가 통과함을 확인한다.
- C5: `dotnet build Biz.Bizadm.PowerManager.slnx`와 `dotnet test --solution Biz.Bizadm.PowerManager.slnx`(테스트 프로젝트 6개)가 성공함을 확인한다.
- C6: 기준 spec 네 개를 C6 항목별로 읽어 대조한다. dawon-fake-device R9의 값은 `FakeDeviceRecorder.cs`의 `WriteAsync` 호출과 대조한다.
- C7: `src/Biz.Bizadm.PowerManager.FakeDevice.App`에서 `중계`·`dawon-relay`를 검색한다.

## 원칙 점검

- 적용 규칙: `workflow/spec-driven.md`(절차, 기준 spec 반영·R번호 규칙), `git/github-issue.md`(#20 사용·갱신, 커밋에 번호 참조), `workflow/repository-content.md`(저장소 상대 경로만, 개인정보 없음), 사용자 전역 규칙(한국어 응답, `gh` 미사용).
- 충돌: 없음. 기준 spec dawon-relay는 규칙대로 뺀 요구를 목록에서 지우고 이력에 남긴다(파일을 지우지 않음). 끝난 변경 폴더 `docs/specs/dawon-relay/`는 고치지 않는다.
