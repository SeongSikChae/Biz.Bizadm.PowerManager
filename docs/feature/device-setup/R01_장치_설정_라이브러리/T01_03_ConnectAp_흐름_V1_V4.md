---
출처: R01-R3, R01-R5, R01-R6, R01-R8, R01-R9, R01-R10, R02-R3, R02-R4, R03-R6, R03-R9
상태: 끝남
---

# T01_03 ConnectAp 흐름 V1~V4

## 읽을 것

- 요구사항: `R01-R3`, `R01-R5`, `R01-R6`, `R01-R8`, `R01-R9`, `R01-R10`, `R02-R3`, `R02-R4`, `R03-R6`, `R03-R9`
- 수용 기준: `R01-A3`, `R01-A5`, `R01-A6`, `R01-A8`, `R01-A9`, `R01-A10`, `R02-A3`, `R02-A4`, `R03-A6`, `R03-A9`
- 결정: 없음
- 계약: `C01-E1`, `C01-I1`, `C02-I1`, `C03-I1`, `C03-I5`
- 옛 작업 기록: `docs/specs/device-config-versions/`(지움. `15dc357`에 있음), 저장소 밖 작업 문서 `2026-11-19-device-config-versions.md`(기준점 `aa4a361`). 일감 #13, 커밋 `845c60a`

## 고칠 파일

- `src/Biz.Bizadm.PowerManager.DeviceConfig/Protocol/DeviceProtocol.cs`(새 파일): `DeviceProtocol`(V1~V4), `DeviceProtocols.TryParse`(대소문자 구분)
- `src/Biz.Bizadm.PowerManager.DeviceConfig/Protocol/DeviceSettings.cs`: `Protocol`(기본 V3)·`MqttKey`, V1 키 9개, `pass`·`mqtt_key` 마스킹
- `src/Biz.Bizadm.PowerManager.DeviceConfig/Protocol/DeviceMessages.cs`: `Delay`
- `src/Biz.Bizadm.PowerManager.DeviceConfig/Transport/DeviceConnection.cs`: `HandshakeAsync(maxSkippedFrames)`, `SendAsync`, `CloseGracefullyAsync`
- `src/Biz.Bizadm.PowerManager.DeviceConfig/Transport/DeviceClient.cs`: 흐름별 송수신, V1 `MqttKey` 검사
- `src/Biz.Bizadm.PowerManager.DeviceConfig/DeviceConfigRunner.cs`: V1 `MqttKey` 없으면 어댑터 선택 전에 실패
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/`: `McpConfiguration.cs`, `Security/ConfigSecretProtector.cs`, `Program.cs`, `DeviceConfigService.cs`(`settings.protocol`), `DeviceConfigTools.cs`(설명)
- `src/Biz.Bizadm.PowerManager.DeviceConfig.App/`: `AppConfiguration.cs`, `Security/ConfigSecretProtector.cs`, `Program.cs`
- 테스트: `DeviceConfig.Tests`(`MockDevice`의 `helloResponse`, `DeviceSettingsTests`, `DeviceMessagesTests`, `DeviceClientTests`, `DeviceConfigRunnerTests`), `DeviceConfig.Mcp.Tests`(`McpConfigurationTests`, `DeviceConfigServiceTests`, `HttpIntegrationTests`)
- `docs/mcp/device-config-mcp-사용.md`, `docs/features/device-config.md`(지움. `15dc357`에 있음), `docs/features/device-config-mcp.md`(지움. `15dc357`에 있음), `docs/features/device-config-app.md`(지움. `15dc357`에 있음)(새 파일)

## 할 일

### 단계 1: 라이브러리
- 흐름 열거형과 설정, `Delay`, 핸드셰이크 건너뛰기·정상 종료, 흐름별 `ConnectAp`, V1 `MqttKey` 검사(`R01-R3`, `R01-R5`, `R01-R6`, `R01-R8`~`R01-R10`).

확인: `dotnet build src/Biz.Bizadm.PowerManager.DeviceConfig -warnaserror` 성공.

### 단계 2: 라이브러리 테스트
- 흐름별 송신 줄 순서, V4 건너뛰기 0·1·2개 성공·3개 실패, V2 응답 없으면 시간 초과, V1 `MqttKey` 없음이면 송신·PC Wi-Fi 변경 없음.

확인: `dotnet test --project tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests` 실패 0.

### 단계 3: MCP
- `Settings.Protocol`·`MqttKey`, `MqttKey` 암호화, `settings.protocol` 덮어쓰기, V1 `MqttKey` 검사는 `ResolveSettings`에서(`R03-R6`, `R03-R9`).

확인: `dotnet test --project tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests` 실패 0.

### 단계 4: App
- `Settings.Protocol`·`MqttKey` 검사(V1 `MqttKey` 필수), 암호화, 복호화 값만 옵션에(`R02-R3`, `R02-R4`).

확인: 임시 설정으로 App exe를 실행해 `Protocol: v3`과 V1·`MqttKey` 없음이 종료 코드 2.

### 단계 5: 전체 검증·기준 spec·실장비
- 솔루션 빌드·전체 테스트, 옛 기준 spec 셋 갱신, MCP 게시·배포 뒤 B540 실장비 1에 V3 `ConnectAp`.

확인: 빌드 경고 0·테스트 실패 0. 2026-11-19 11:15:49 송신 뒤 약 6초 만에 서버 `POST /api/v1/devices/register/create` 수신.

## 위험

- V1·V2·V4 대상 장비가 없어 모의 장치로 송수신 순서만 확인했다.
- V2의 설정 응답 형식을 모르므로 내용은 검사하지 않는다.
- MCP는 기동할 때 V1 `MqttKey` 누락을 막지 않으므로 `ResolveSettings`(인자가 없는 경로 포함)가 유일한 검사다.
- YAML 모델의 `MqttKey`는 기동 뒤 암호문이므로 `Protect`의 복호화 반환값만 옵션에 넣는다.

## 검증 방법

| 수용 기준 | 종류 | 대상 | 전제 | 순서 | 기대 결과 |
| --- | --- | --- | --- | --- | --- |
| `R01-A3` | 단위 테스트 | `DeviceClientTests` | `MockDevice(helloResponse)`로 앞 프레임 0~3개 | `dotnet test --solution Biz.Bizadm.PowerManager.slnx` | V4는 0·1·2개 성공, 3개 실패 |
| `R01-A5` | 단위 테스트 | `DeviceClientTests` | 응답 있는·없는 `MockDevice` | 위와 같음 | V1 `connectap` 없음, V2 응답 뒤 `{"delay":"0"}`, V4 설정 → `connectap`, V3 그대로 |
| `R01-A6` | 단위 테스트 | `DeviceSettingsTests` | 없음 | 위와 같음 | V1 9개 키 순서, 기본 V3 |
| `R01-A8` | 단위 테스트 | `DeviceSettingsTests`, `DeviceClientTests` | 없음 | 위와 같음 | `pass`·`mqtt_key` 가림, 원문 없음 |
| `R01-A9` | 단위 테스트 | `DeviceClientTests` | 닫지 않는 `MockDevice` | 위와 같음 | V1·V4 정상 종료, V2 응답 없으면 `TimeoutException` |
| `R01-A10` | 단위 테스트 | `DeviceClientTests`, `DeviceConfigRunnerTests` | V1·`MqttKey` 없음 | 위와 같음 | 송신 없음, `ConnectOpenAsync` 호출 없음 |
| `R02-A3` | 실행 | App exe | 스크래치의 임시 설정 | `Protocol: v3`, V1·`MqttKey` 없음으로 실행 | 종료 코드 2 |
| `R02-A4` | 코드 확인 | App `Program.cs` | 없음 | `CreateOptions`가 `Protect` 반환값을 쓰는지 읽음 | YAML 모델의 `MqttKey`를 직접 쓰지 않음 |
| `R03-A6` | 단위 테스트 | `McpConfigurationTests` | 없음 | 위 명령 | 기본 V3, `v3`·`X` 오류, V1·`MqttKey` 없음도 기동 검증 통과 |
| `R03-A9` | 단위 테스트 | `DeviceConfigServiceTests`, `HttpIntegrationTests` | 없음 | 위 명령 | `protocol` 덮어쓰기·잘못된 값 오류, V1 `MqttKey` 없음 오류(두 경로), `mqttKey` 인자 거부, 스키마에 `protocol` |

## 원칙 점검

- 적용 규칙: 그때의 MCP `workflow/spec-driven.md`, MCP `git/github-issue.md`, MCP `dotnet/kms-config-secrets.md`, 전역 `language-preference.md`, 메모리 실장비 대상 확인
- 충돌: 없음
