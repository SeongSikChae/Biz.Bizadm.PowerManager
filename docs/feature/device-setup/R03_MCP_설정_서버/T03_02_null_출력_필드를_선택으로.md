---
출처: R03-R10
상태: 끝남
---

# T03_02 null 출력 필드를 선택으로

## 읽을 것

- 요구사항: `R03-R10`
- 수용 기준: `R03-A10`
- 결정: 없음
- 계약: `C03-I2`, `C03-I5`
- 옛 작업 기록: `docs/mcp/0004-device-config-mcp-nullable-output-개발.md`(지움. `15dc357`에 있음). 일감 #8, 커밋 `d44925f`(2026-11-11)

## 고칠 파일

- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/DeviceConfigTools.cs`: 출력 record의 `CurrentSsid`·`Error` 생성자 매개변수에 기본값 `= null`
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/`: Wi-Fi 미연결 어댑터·성공 결과의 스키마 검사 테스트

## 할 일

### 단계 1: 재현
- Wi-Fi에 연결되지 않은 어댑터가 있으면 `list_wifi_adapters`가 "output schema: … must have required property 'currentSsid'"로 실패하는 것을 새 테스트로 재현한다.

확인: 수정 전 새 테스트가 같은 이유로 실패.

### 단계 2: 선택 필드로
- `currentSsid`·`error`에 기본값을 줘 SDK(ModelContextProtocol 2.4.0)가 만드는 outputSchema의 `required`에서 뺀다. SDK 전역 직렬화 옵션은 바꾸지 않는다.

확인: 새 테스트 통과, 기존 테스트 회귀 없음, 재빌드한 서버로 미연결 어댑터 환경에서 `list_wifi_adapters`가 오류 없이 반환.

## 위험

- 기본값을 주면 schema 속성에 `"default": null`이 붙는다(SDK 2.4.0 실측, 무해). 성공 결과의 `error`는 수정 전후 모두 생략된다.

## 검증 방법

| 수용 기준 | 종류 | 대상 | 전제 | 순서 | 기대 결과 |
| --- | --- | --- | --- | --- | --- |
| `R03-A10` | 단위 테스트, 직접 호출 | `DeviceConfig.Mcp.Tests`의 outputSchema 테스트; 실제 MCP 서버 `list_wifi_adapters` | 직접 호출: Wi-Fi 미연결 어댑터가 있는 PC, MCP 클라이언트 재시작 | `dotnet build Biz.Bizadm.PowerManager.slnx -warnaserror`, `dotnet test --solution Biz.Bizadm.PowerManager.slnx`; tool 호출 | `required`에 두 필드 없음, null 필드가 생략된 결과가 schema를 통과 |

## 원칙 점검

- 적용 규칙: 그때의 저장소 개발 문서 절차(버그 수정이라 방안·설계 생략), 전역 `language-preference.md`
- 충돌: 없음
