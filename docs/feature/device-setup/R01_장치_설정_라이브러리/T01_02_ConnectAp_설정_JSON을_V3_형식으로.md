---
출처: R01-R6, R01-R7, R01-R8
상태: 끝남
---

# T01_02 ConnectAp 설정 JSON을 V3 형식으로

## 읽을 것

- 요구사항: `R01-R5`, `R01-R6`, `R01-R7`, `R01-R8`(V1 키와 `mqtt_key` 마스킹은 `T01_03`이 뒤에 더했다)
- 수용 기준: `R01-A5`, `R01-A6`, `R01-A7`, `R01-A8`
- 결정: 없음
- 계약: `C01-E1`, `C01-I1`
- 옛 작업 기록: `docs/specs/connectap-v3/`(지움. `15dc357`에 있음)(spec·plan·tasks), 저장소 밖 작업 문서 `2026-11-19-connectap-v3.md`(기준점 `1517b24`, 검토 기록, 실장비 결과). 일감 #11, 커밋 `aa4a361`

## 고칠 파일

- `src/Biz.Bizadm.PowerManager.DeviceConfig/Protocol/DeviceSettings.cs`: `ToJson(mac, ssid, pass, masked)`로 바꾸고 `mqtt_key`·`UnsafeRelaxedJsonEscaping` 제거, 기본 직렬화
- `src/Biz.Bizadm.PowerManager.DeviceConfig/Transport/DeviceClient.cs`: `MqttKeyGenerator` 호출 제거, 새 `ToJson` 호출
- `src/Biz.Bizadm.PowerManager.DeviceConfig/Transport/DeviceConnection.cs`: 주석만
- `src/Biz.Bizadm.PowerManager.DeviceConfig/Protocol/MqttKeyGenerator.cs`, `tests/.../Protocol/MqttKeyGeneratorTests.cs`: 삭제
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/Protocol/DeviceSettingsTests.cs`, `tests/.../Transport/DeviceClientTests.cs`: 키 순서·이스케이프·마스킹 검사
- `docs/features/device-config.md`(지움. `15dc357`에 있음): 옛 기준 spec 새로 작성

## 할 일

### 단계 1: 설정 JSON 형식 변경
- `mqtt_key`를 빼고 기본 인코더로 직렬화한다(`R01-R6`, `R01-R7`). 쓰는 곳이 없어진 `MqttKeyGenerator`를 지운다.

확인: `dotnet build src/Biz.Bizadm.PowerManager.DeviceConfig` 성공.

### 단계 2: 테스트
- 키 이름·순서, `+ & < > '` 이스케이프, `pass`만 가림, 정상·시간 초과·연결 종료에서 원문·이스케이프된 `pass`가 로그·예외에 없음(`R01-R8`).

확인: `dotnet build Biz.Bizadm.PowerManager.slnx -warnaserror` 경고 0, `dotnet test --solution Biz.Bizadm.PowerManager.slnx` 실패 0(184건 통과).

### 단계 3: 배포·실장비
- MCP를 `FolderProfile`로 게시해 exe·pdb만 교체하고, B540 실장비 1에 `ConnectAp`를 보내 공유기·서버 접속을 관찰한다.

확인: 2026-11-19 10:11:20 `ConnectAp` 응답 OK, 약 4초 뒤 `dwapi.dawonai.com` DNS 조회, 약 7초 뒤 서버에 `POST /api/v1/devices/register/create`. 두 형식 차이(`mqtt_key`·이스케이프)를 원인으로 확정(어느 쪽인지는 분리하지 않음).

## 위험

- 장치 파서가 `\uXXXX`를 풀지 못하면 비밀번호에 `+ & < > '`·비ASCII가 있을 때 저장값이 달라진다. 참고 구현 V3가 같은 방식이라 받아들였다.
- 실행 중인 MCP exe는 덮어쓸 수 없어 배포 전에 꺼져 있는지 확인한다.

## 검증 방법

| 수용 기준 | 종류 | 대상 | 전제 | 순서 | 기대 결과 |
| --- | --- | --- | --- | --- | --- |
| `R01-A5` | 단위 테스트 | `DeviceClientTests#ConnectApAsync_OneConnectionInOrder`, `#ConnectApAsync_NotOk_Throws` | `MockDevice` | `dotnet test --solution Biz.Bizadm.PowerManager.slnx` | 송신 다섯 줄 순서 그대로, OK가 아니면 예외 |
| `R01-A6` | 단위 테스트 | `DeviceSettingsTests` | 없음 | 위와 같음 | 키 목록이 기대 순서와 같고 `mqtt_key` 없음, 값이 모두 문자열 |
| `R01-A7` | 단위 테스트 | `DeviceSettingsTests` | `pass` = `a+b&c<d>'e` | 위와 같음 | `"pass":"a\u002Bb\u0026c\u003Cd\u003E\u0027e"` |
| `R01-A8` | 단위 테스트 | `DeviceSettingsTests`, `DeviceClientTests` | 정상·시간 초과·연결 종료 모의 장치 | 위와 같음 | `pass`만 `***`, 원문·이스케이프된 `pass` 없음 |

## 원칙 점검

- 적용 규칙: 그때의 MCP `workflow/spec-driven.md`(spec·plan·tasks와 네 검토), MCP `git/github-issue.md`, 전역 `language-preference.md`, 메모리 실장비 대상 확인
- 충돌: 없음
