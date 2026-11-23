---
출처: R03-R9
상태: 끝남
---

# T03_03 settings 인자로 설정 덮어쓰기

## 읽을 것

- 요구사항: `R03-R9`(그때의 형태. `settings.protocol`과 `MqttKey` 검사는 `T01_03`이 더했다)
- 수용 기준: `R03-A9`
- 결정: `D03_01`
- 계약: `C03-I5`
- 옛 작업 기록: `docs/mcp/0005-device-config-mcp-settings-override-개발.md`(지움. `15dc357`에 있음). 일감 #9, 커밋 `2914f35`(2026-11-11)

## 고칠 파일

- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/DeviceConfigService.cs`: `SettingsOverride`, 필드 병합(`ResolveSettings`), 검사 순서
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/DeviceConfigTools.cs`: `run_device_commands`의 선택 인자 `settings`, 속성 설명
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/DeviceConfigServiceTests.cs`, `HttpIntegrationTests.cs`(그때는 stdio 통합 테스트): 병합·누락·범위·모르는 키
- `docs/mcp/0002-device-config-mcp-설계.md`(지움. `15dc357`에 있음): tool 표와 5.3절 반영

## 할 일

### 단계 1: settings 인자와 필드 병합 (U1)
- null이 아닌 필드만 설정 파일 값을 덮어쓰고, `Settings`가 없으면 필수 필드 다섯 개를 인자로 받아 허용한다. 검사 순서는 commands 파싱 → 기존 메시지 조건 → 필수 필드 누락 → 포트 범위 → `wifiSsid`이고 모두 Wi-Fi 접속 전이다. 모르는 키는 `[JsonExtensionData]`로 받아 `ArgumentException`("Unknown settings keys: …")으로 `success=false`에 담는다.

확인: `dotnet build Biz.Bizadm.PowerManager.slnx -warnaserror`, `dotnet test --solution Biz.Bizadm.PowerManager.slnx` 통과.

### 단계 2: 설계 문서 반영 (U2)
- 0002 설계의 tool 표와 5.3절에 인자를 적는다.

확인: 문서 링크·표가 깨지지 않음.

## 위험

- 처음 쓴 `[JsonUnmappedMemberHandling(Disallow)]`는 SDK 2.4.0이 원인을 가린 일반 오류로 돌려줘 `[JsonExtensionData]` 방식으로 바꿨다.
- 타입 불일치(예: `serverPort: true`)는 SDK 바인딩에서 원인 없는 isError로 나간다(범위 밖).
- 실제 장치 `ConnectAp`는 상태 변경 명령이라 이 작업에서 실행하지 않았다.

## 검증 방법

| 수용 기준 | 종류 | 대상 | 전제 | 순서 | 기대 결과 |
| --- | --- | --- | --- | --- | --- |
| `R03-A9` | 단위 테스트 | `DeviceConfigServiceTests`(settings 병합·필수 필드 누락·포트 범위·모르는 키) | `FakeHostWifi`, `MockDevice` | `dotnet test --solution Biz.Bizadm.PowerManager.slnx` | 인자 필드가 덮어써짐, 누락·범위·`user_id` 같은 모르는 키는 Wi-Fi 접속 없이 `success: false`와 메시지 |

## 원칙 점검

- 적용 규칙: 그때의 저장소 개발 문서 절차(작은 확장이라 방안·설계 생략), 전역 `language-preference.md`, 메모리 실장비 대상 확인
- 충돌: 없음
