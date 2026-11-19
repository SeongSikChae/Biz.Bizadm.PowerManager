# DeviceConfig ConnectAp에 참고 구현 V1·V2·V4 흐름 추가 작업

형식: `- [x] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 라이브러리

- [x] T001 `DeviceProtocol` 열거형(V1~V4)과 `DeviceProtocols.TryParse`(이름과 정확히 같을 때만) 추가 (src/Biz.Bizadm.PowerManager.DeviceConfig/Protocol/DeviceProtocol.cs) — C1
- [x] T002 `Protocol`(기본 V3)·`MqttKey` 속성 추가, `ToJson`이 V1이면 구형 9개 키, 마스킹은 `pass`·`mqtt_key` (src/Biz.Bizadm.PowerManager.DeviceConfig/Protocol/DeviceSettings.cs) — C2, C3, C5, C7
- [x] T003 [P] `Delay` 메시지 추가 (src/Biz.Bizadm.PowerManager.DeviceConfig/Protocol/DeviceMessages.cs) — C4
- [x] T004 [P] `HandshakeAsync(int maxSkippedFrames, CancellationToken)` 오버로드, 공개 `SendAsync`, `CloseGracefullyAsync`(송신 종료 → 장치가 닫을 때까지 수신 비움, 로그만, 시간 초과는 실패 아님) 추가 (src/Biz.Bizadm.PowerManager.DeviceConfig/Transport/DeviceConnection.cs) — C2, C4, C6
- [x] T005 `ConnectApAsync` 흐름별 송수신: V1(설정 → 정상 종료), V2(설정 → 응답 1개 검사 없음 → `Delay` → 정상 종료), V3(지금 그대로), V4(`HandshakeAsync(2)` → 설정 → `connectap` → 정상 종료). V1 `MqttKey`가 비면 송신 전에 `InvalidOperationException` (src/Biz.Bizadm.PowerManager.DeviceConfig/Transport/DeviceClient.cs) — C2, C3, C4, C5, C6, C7
- [x] T006 `ConnectAp`가 있고 V1인데 `MqttKey`가 비면 어댑터 선택 전에 `InvalidOperationException` (src/Biz.Bizadm.PowerManager.DeviceConfig/DeviceConfigRunner.cs) — C3

체크포인트: `dotnet build src/Biz.Bizadm.PowerManager.DeviceConfig -warnaserror` 성공

## 단계 2: 라이브러리 테스트

- [x] T007 생성자 선택 인자 `helloResponse` 추가(기본 `START_OK` 한 프레임) (tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/TestSupport/MockDevice.cs) — C6, C10
- [x] T008 [P] V1 키·순서·문자열·`pass`·`mqtt_key` 마스킹, 기본 `Protocol`이 V3 (tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/Protocol/DeviceSettingsTests.cs) — C1, C2, C5, C7, C10
- [x] T009 [P] `Delay` 형식 (tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/Protocol/DeviceMessagesTests.cs) — C4, C10
- [x] T010 흐름별 송신 줄 순서(V1 `connectap` 없음, V2 응답 뒤 `{"delay":"0"}`·응답이 OK가 아니어도 성공·응답 없으면 `TimeoutException`, V4 설정 → `connectap`), V1·V4가 응답 없는/있는 모의 장치 모두에서 실패 없이 끝남, V4 핸드셰이크 앞 프레임 0·1·2개 성공·3개 실패, V1 `MqttKey` 없음 → 설정 송신 없음, 로그·예외에 `pass`·`mqtt_key`(원문·이스케이프) 없음 (tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/Transport/DeviceClientTests.cs) — C2, C3, C4, C5, C6, C7, C10
- [x] T011 [P] V1 `MqttKey` 없음 → `ConnectOpenAsync` 호출 없이 실패 (tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/DeviceConfigRunnerTests.cs) — C3, C10

체크포인트: `dotnet test --project tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests` 실패 0

## 단계 3: MCP

- [x] T012 `SettingsSection.Protocol`(문자열, 기본 `"V3"`)·`MqttKey` 추가, `Validate`에서 `Protocol` 값만 검사(V1 `MqttKey` 누락은 기동 시 막지 않음), `ToOptions(wifiProfiles, mqttKey)` (src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/McpConfiguration.cs) — C1, C3, C8
- [x] T013 `MqttKey` 암호화·복호화, 반환을 `ProtectedSecrets(WifiProfiles, MqttKey)`로, `HasSecrets`에 `MqttKey` 포함 (src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Security/ConfigSecretProtector.cs) — C3, C8
- [x] T014 바뀐 `Protect` 반환값으로 `ToOptions` 호출 (src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Program.cs) — C8
- [x] T015 `SettingsOverride.Protocol` 추가, `ResolveSettings`가 `Protocol`(인자 → 파일 → V3, 잘못된 값 오류)·`MqttKey`(파일 값만, V1인데 없으면 오류)를 정함. 인자가 없을 때도 파일 값을 쓰고 V1·`MqttKey` 없음이면 오류 (src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/DeviceConfigService.cs) — C3, C8
- [x] T016 [P] `run_device_commands` 설명에 흐름 선택 추가 (src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/DeviceConfigTools.cs) — C8
- [x] T017 `Protocol` 기본·`v3`·`X` 오류, V1인데 `MqttKey`가 없어도 기동 검증 통과, `ToOptions` 새 시그니처 (tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/McpConfigurationTests.cs) — C1, C3, C8, C10
- [x] T018 `protocol` 덮어쓰기·잘못된 값, V1 `MqttKey` 없음 → Wi-Fi 접속 없이 오류(설정 파일 V1·인자 없음 경로와 인자 `protocol: V1` 경로 둘 다), `mqttKey` 인자는 모르는 키로 거부 (tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/DeviceConfigServiceTests.cs) — C3, C8, C10
- [x] T019 `settings` 속성 기대 목록에 `protocol` 추가 (tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/HttpIntegrationTests.cs) — C8, C10
- [x] T020 [P] 설정 예에 `Settings.Protocol`·`Settings.MqttKey` 설명 (docs/mcp/device-config-mcp-사용.md) — C8

체크포인트: `dotnet test --project tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests` 실패 0

## 단계 4: App

- [x] T021 `SettingsSection.Protocol`·`MqttKey` 추가, `Validate`에서 값 검사·V1 `MqttKey` 필수 (src/Biz.Bizadm.PowerManager.DeviceConfig.App/AppConfiguration.cs) — C1, C3, C9
- [x] T022 `MqttKey` 암호화·복호화(MCP와 같은 방식) (src/Biz.Bizadm.PowerManager.DeviceConfig.App/Security/ConfigSecretProtector.cs) — C3, C9
- [x] T023 `ProtectSecrets`가 복호화 값(`ProtectedSecrets`)을 돌려주고 `CreateHostApplication`·`CreateOptions`가 이를 받아 `Protocol`·`MqttKey`(복호화 값) 매핑. YAML 모델의 `Settings.MqttKey`는 직접 쓰지 않음 (src/Biz.Bizadm.PowerManager.DeviceConfig.App/Program.cs) — C3, C9
- [x] T024 App exe를 스크래치의 임시 설정으로 실행해 `Protocol: v3`과 `Protocol: V1`(`MqttKey` 없음)이 종료 코드 2인지 확인 (src/Biz.Bizadm.PowerManager.DeviceConfig.App) — C1, C3, C9

체크포인트: App 빌드 성공, T024 두 경우 종료 코드 2

## 단계 5: 전체 검증과 기준 spec

- [x] T025 솔루션 빌드(`dotnet build Biz.Bizadm.PowerManager.slnx -warnaserror`)와 전체 테스트(`dotnet test --solution Biz.Bizadm.PowerManager.slnx`) (Biz.Bizadm.PowerManager.slnx) — C10
- [x] T026 [P] 요구 갱신(흐름 요구 추가, 핸드셰이크·ConnectAp·설정 JSON·마스킹·응답 대기(R9) 요구 바꿈, `파일:`에 `DeviceProtocol.cs` 추가)과 이력 (docs/features/device-config.md) — C1~C7
- [x] T027 [P] 요구 갱신(설정 `Protocol`·`MqttKey`, `settings.protocol`, `MqttKey` 암호화)과 이력 (docs/features/device-config-mcp.md) — C3, C8
- [x] T028 [P] App 기준 spec 새로 작성(`파일:`에 App 프로젝트 파일, 요구·이력) (docs/features/device-config-app.md) — C1, C3, C9

체크포인트: 빌드 경고 0·테스트 실패 0, 세 기준 spec의 `파일:` 경로가 모두 있음

## 단계 6: 배포·실장비

- [x] T029 `FolderProfile`로 MCP 게시, MCP가 꺼져 있는지 확인(켜져 있으면 사용자에게 중지 요청), 기존 exe·pdb 백업 뒤 exe·pdb만 교체 (C:\Bizadm.PowerManager.Config) — C11
- [x] T030 사용자가 MCP를 시작하면 `DWD-LS120_501f96`에 `ConnectAp`(설정 파일 값 그대로, V3)를 보내고 서버 로그의 `POST /api/v1/devices/register/create`를 확인, 결과를 작업 문서와 `device-config` 이력에 기록 (docs/features/device-config.md) — C11

체크포인트: 서버 로그에 `register/create` 요청이 있고 결과가 기록됨

## 의존 관계

- T001 → T002 → T005(열거형·설정을 씀). T004 → T005. T003·T004는 서로 독립. T006은 T002 뒤.
- 단계 2는 단계 1 뒤. T007 → T010(모의 장치 `helloResponse` 사용). T008·T009·T011은 서로 독립.
- 단계 3은 단계 1 뒤(`DeviceSettings.Protocol`·`MqttKey`, `DeviceProtocols.TryParse`를 씀). T012 → T013(`SettingsSection.MqttKey`를 씀), T012 → T014, T013 → T014, T012 → T015. 테스트 T017은 T012 뒤, T018은 T015 뒤, T019는 T015 뒤(`SettingsOverride.Protocol`이 스키마에 나옴). T016·T020은 독립.
- 단계 4는 단계 1 뒤이며 단계 3과 독립(같은 방식이라 단계 3을 먼저 하면 참고가 된다). T021 → T022(`SettingsSection.MqttKey`를 씀), T021 → T023, T022 → T023, T024는 T021~T023 뒤.
- 단계 5는 단계 1~4 뒤. 단계 6은 단계 5 체크포인트 뒤이며, T030은 사용자의 MCP 시작이 필요하다.
