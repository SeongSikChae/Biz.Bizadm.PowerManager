---
status: done
category: mcp
feature: device-config-mcp
phase: 방안
---

# device-config-mcp 방안

GitHub: SeongSikChae/Biz.Bizadm.PowerManager#7 · 참고: [cli/0002-device-config-설계](../cli/0002-device-config-설계.md)

## 문제

B540 장치 설정은 콘솔 앱 `DeviceConfig.App`으로만 할 수 있다. 무선 어댑터·장치 AP·공유기 Wi‑Fi를 콘솔 번호로 고르고, 실행할 명령은 설정 파일 `Commands`에 고정한다. MCP 클라이언트(Claude 등)가 대화 중에 장치를 찾고 명령을 실행하려면 같은 기능을 MCP tool로 제공해야 한다.

`DeviceConfig` 라이브러리는 이미 `IHostWifi`(PC Wi‑Fi)와 `IDeviceConfigSelector`(선택)를 분리해 두었다(설계 §3 "이후 MCP 등으로 교체할 수 있다").

## 목표

- 로컬에서 실행하는 MCP 서버를 만들어 장치 조회·명령 실행을 tool로 제공한다.
- `DeviceConfig` 라이브러리(프로토콜·전송·HostWifi·Runner)를 그대로 재사용한다.

## 결정 사항

| 항목 | 채택 | 대안(미채택) |
|---|---|---|
| 진행 절차 | work-process 전체(방안→설계→개발→검토→구현) | 바로 구현 |
| 전송 | **stdio** (MCP 클라이언트가 로컬 프로세스로 실행) | Streamable HTTP, 둘 다 |
| tool 단위 | **원자적 실행 + 조회**: 조회 tool은 PC 연결을 바꾸지 않고, 실행 tool은 한 호출 안에서 장치 AP 접속 → 명령 → PC Wi‑Fi 복구 | 세분화 상태형(connect/execute/restore 개별 호출) |
| 비밀값 출처 | **YAML 설정 파일**(App과 같은 `WifiProfiles`+OsKms 암호화, `Settings`). tool은 SSID만 인자로 받는다 | tool 인자, 혼합 |
| 상태 변경 명령 | **전체 10개 명령 허용**, 실행 tool에 MCP `destructiveHint` 표시 | 초기화 명령 제외, 설정 허용 목록 |
| 문서 카테고리 | `docs/mcp` | `docs/cli` |

### 원자적 실행을 채택한 이유

PC가 장치 AP(`DWD-…`, 인터넷 없음)에 접속하면 같은 무선 어댑터로 통신하던 MCP 클라이언트가 LLM 서버와 끊길 수 있다. 접속 상태를 여러 tool 호출에 걸쳐 유지하면 다음 호출을 보내지 못하거나 복구가 누락된다. 한 호출 안에서 접속·실행·복구 요청까지 끝내면 복구 누락 위험이 없다.

단, Windows 복구(`RestoreAsync`)는 이전 Wi‑Fi로 재연결을 **요청만** 하고 연결 완료를 기다리지 않는다. 응답 직후 잠시 PC가 오프라인일 수 있으며, MCP는 이를 기다리지 않는다(검토 결정). 클라이언트는 응답을 받은 뒤 재연결될 때까지 자체 재시도에 맡긴다.

## tool 범위

| tool | 동작 | PC 연결 변경 |
|---|---|---|
| `list_wifi_adapters` | PC 무선 어댑터 목록과 현재 연결 SSID | 없음 |
| `scan_device_aps` | 어댑터로 주변을 스캔해 `ApPrefix`(기본 `DWD-`)로 시작하는 장치 AP 목록 | 없음 |
| `list_wifi_profiles` | 설정 파일에 등록된 공유기 SSID 목록(비밀번호 제외) | 없음 |
| `run_device_commands` | 어댑터·장치 AP·명령 목록(·`ConnectAp`면 공유기 SSID)을 받아 장치 AP 접속 → 명령 순서 실행(명령별 응답 반환) → PC Wi‑Fi 복구 | 호출 중에만, 끝나면 복구 |

명령은 App과 같다: `ConnectAp`, `ApScan`, `Reboot`, `FactoryReset`, `ScanAp`, `OnCmd`, `OffCmd`, `Measurement`, `OverLoad`, `TestFactoryReset`.

## 비범위

- HTTP(Streamable HTTP/SSE) 전송, 원격 접속·인증
- 상태형 tool(접속 유지), 여러 장치 동시 실행
- 여러 MCP 서버 프로세스 동시 실행(클라이언트 세션마다 프로세스가 뜨는 경우) 간 직렬화. 단일 인스턴스 사용을 전제한다
- 복구 후 이전 Wi‑Fi 재연결 완료 대기
- tool 인자로 Wi‑Fi 비밀번호·장치 Settings 받기, MCP에서 설정 파일 편집
- `DeviceConfig` 프로토콜·명령 동작 변경, 새 장치 명령 추가
- Linux·macOS PC Wi‑Fi 구현(App과 같이 미구현 OS는 `Device.Host`로 접속)
- `DeviceConfig.App` 기능 변경
