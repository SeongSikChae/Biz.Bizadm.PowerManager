---
상태: 유효
---

# D03_01 settings 인자로 필드 단위 덮어쓰기

## 상황

`ConnectAp` 설정 JSON 값(`Settings`)은 기동할 때 설정 파일에서만 읽었다. 기기마다 `UserId`·`Lati`·`Long`·`Topic` 등이 다르면 설정 파일을 고치고 MCP 서버를 다시 시작해야 했다. 관련: `R03-R9`, `C03-I5`, 짝 태스크 `T03_03`.

## 결정

- `run_device_commands`에 선택 인자 `settings`(객체)를 더한다. 서버에는 상태를 남기지 않고 호출마다 적용한다.
- 인자로 넘긴(null이 아닌) 필드만 설정 파일 `Settings` 값을 덮어쓴다.
- 설정 파일에 `Settings`가 없으면 필수 필드(`apiServerAddr`·`apiServerPort`·`serverAddr`·`serverPort`·`userId`)를 모두 인자로 넘길 때 `ConnectAp`를 허용하고, 선택 필드는 `SettingsSection` 기본값을 쓴다.
- `WifiProfiles`(비밀번호)는 인자로 받지 않고 설정 파일에서만 받는다.
- 검사는 Wi-Fi 접속 전에 하고, 실패하면 `success: false`와 `error`로 돌려준다. 빈 문자열처럼 설정 파일 검증에 없는 검사는 더하지 않고 null만 "넘기지 않음"으로 본다.
- 모르는 키는 `ConnectAp` 포함 여부와 관계없이 거부한다(검토 뒤 사용자 결정).

## 이유

- 모르는 키 거부: 장치 JSON이 snake_case라 `user_id`처럼 보내기 쉬운데, 무시하면 덮어쓰기가 빠진 채 상태 변경 명령이 성공한다(옛 개발 문서의 결정 절).
- 그 밖의 항목: 사용자가 말하지 않음(옛 문서에는 "사용자 합의"로만 적혀 있다).

## 버린 안

| 안 | 버린 이유 |
| --- | --- |
| 모르는 키를 무시 | 위 「이유」에 함께 적힘 |

## 경위

- 2026-11-11 `docs/mcp/0005-device-config-mcp-settings-override-개발.md`(지움. `15dc357`에 있음) 「결정 (사용자 합의)」에서 옮겼다(2026-11-23).
- 모르는 키는 처음에 `[JsonUnmappedMemberHandling(Disallow)]`로 SDK 바인딩 단계에서 거부했으나, SDK 2.4.0이 원인을 가린 일반 오류만 돌려줘 `[JsonExtensionData]`로 받아 `ArgumentException`으로 돌려주게 바꿨다(구현 방식이라 에이전트가 정함).
- 뒤에 `T01_03`이 `settings.protocol`을 더했고 `MqttKey`는 인자로 받지 않는 모르는 키가 되었다(`R03-R9`).

## 코드

- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/DeviceConfigService.cs`(`SettingsOverride`, `ResolveSettings`), `DeviceConfigTools.cs`(`run_device_commands`의 `settings`)
  - 지켜야 하는 조건: 검사 순서는 commands 파싱 → 기존 메시지 조건 → 필수 필드 누락 → 포트 범위 → `wifiSsid`이며 모두 Wi-Fi 접속 전
