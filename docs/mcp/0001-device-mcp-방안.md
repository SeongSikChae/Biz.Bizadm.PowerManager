---
status: done
category: mcp
feature: device-mcp
phase: 방안
---

# device-mcp 방안

GitHub: SeongSikChae/Biz.Bizadm.PowerManager#4

## 문제

- 장치 설정은 지금 `Biz.Bizadm.PowerManager.DeviceConfig.App`으로만 할 수 있다(#2, `docs/cli/`). App은 YAML의 `Commands`를 순서대로 실행하고, 장치 AP와 Wi‑Fi는 콘솔에서 고른다. 그래서 LLM 에이전트가 장치를 조회하거나 설정할 수 없다.
- #2 방안에서 "Wi‑Fi 선택 지점은 나중에 로컬 MCP 등으로 교체 가능하게 분리"하기로 했고, `IWifiProfileSelector`·`IDeviceApSelector`로 이미 분리해 두었다.
- 장치 AP에 접속하는 동안 그 무선 어댑터는 인터넷이 끊긴다. 그런데 PC Wi‑Fi 구현은 어댑터를 지정할 수 없다. 무선 어댑터가 2개인 PC에서는 App도 인터넷용 어댑터를 장치 AP로 바꿀 수 있다(아래 "무선 어댑터 현황").

## 목표 (1차)

- 기존 라이브러리(`Biz.Bizadm.PowerManager.DeviceConfig`)를 그대로 쓰는 STDIO MCP 서버를 추가해, 장치 명령을 MCP tool로 제공한다.
- 장치 AP 접속에 쓸 무선 어댑터를 선택할 수 있게 한다. App과 MCP가 함께 쓴다.

## 사용 전제

장치 AP에 접속하면 해당 무선 어댑터의 기존 연결이 끊긴다. MCP 서버는 아래 환경에서만 쓸 수 있다. 이 전제는 MCP 서버 설명(README·tool 설명)에 적는다.

| 환경 | 조건 |
|---|---|
| 로컬 LLM | LLM과 MCP 클라이언트가 같은 PC에 있고, 외부 네트워크가 필요 없다 |
| Wi‑Fi + 유선 | 인터넷은 유선으로, 장치 AP 접속은 Wi‑Fi로 한다 |
| Wi‑Fi + Wi‑Fi | 무선 어댑터가 2개다. 하나는 인터넷용, 다른 하나를 장치 AP용으로 선택한다 |

원격 LLM을 무선 어댑터 1개로만 쓰는 PC는 지원하지 않는다. tool 실행 중에 인터넷이 끊기고, 복구가 실패하면 클라이언트도 끊긴다.

## 무선 어댑터 현황

`IHostWifi` 구현별로 어댑터를 고르는 방식이다. 모두 어댑터를 지정하는 입력이 없다.

| OS | 스캔 | 현재 연결 | 장치 AP 접속 | 복구 |
|---|---|---|---|---|
| Windows (`WindowsHostWifi`) | 첫 인터페이스(`FirstInterface`) | 연결된 첫 인터페이스 | 첫 인터페이스 | 저장한 이전 인터페이스 |
| Linux (`LinuxHostWifi`) | 모든 장치(`ifname` 없음) | 연결된 첫 Wi‑Fi 장치 | 첫 Wi‑Fi 장치 | 이전 연결 프로필 `connection up` |
| macOS (`MacHostWifi`) | 첫 Wi‑Fi 포트 | 첫 Wi‑Fi 포트 | 첫 Wi‑Fi 포트 | 이전 SSID |

무선 어댑터가 2개일 때 생기는 문제(App도 동일):

- "첫 어댑터"가 인터넷용이면, 그 어댑터가 장치 AP로 넘어가 작업하는 동안 인터넷이 끊긴다.
- 현재 연결을 찾는 어댑터와 장치 AP에 접속하는 어댑터가 다를 수 있다(Windows·Linux). 이때 `previous`는 다른 어댑터를 가리키므로, 장치 AP에 접속한 어댑터는 원래 연결로 돌아가지 않는다.
- Linux 스캔은 모든 장치의 결과를 합친다. 그래서 장치 AP가 보인 어댑터와 실제로 접속하는 어댑터가 다를 수 있다.

## 결정 사항

| 항목 | 채택 | 대안(미채택) | 상태 |
|---|---|---|---|
| 진행 절차 | work-process 전체(방안→설계→개발→검토→구현) | 바로 구현 | 합의 |
| 연결 방식 | STDIO MCP 서버 | HTTP(Streamable) MCP | 합의 |
| 사용 전제 | 로컬 LLM, 또는 Wi‑Fi+유선 / Wi‑Fi+Wi‑Fi 이중화 환경 | 제한 없음 | 합의 |
| 무선 어댑터 | 장치 AP용 무선 어댑터를 선택할 수 있게 한다. 무선 어댑터 목록에서 고르는 선택 지점을 Wi‑Fi·장치 AP 선택처럼 분리한다(App은 콘솔, MCP는 아래 "선택 지점"). 스캔·현재 연결·접속·복구를 모두 고른 어댑터로 한다. App과 MCP가 함께 쓴다 | 설정값 지정 / 인터넷 연결이 없는 어댑터 자동 선택 / 설정 필수 | 합의 |
| App 어댑터 선택 | 기존 콘솔 선택과 같이 후보가 1개여도 번호를 입력받는다 | 1개면 자동 선택 | 합의 |
| 일감 분리 | 무선 어댑터 선택을 이 일감(#4) 안의 첫 작업 단위로 먼저 구현한다 | App 버그로 별도 일감 분리 | 합의 |
| MCP 프로젝트 | `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp` 콘솔 실행 파일. 공식 C# SDK(`ModelContextProtocol`). `--config`/`--log`는 App과 같다 | App에 `--mcp` 모드 추가 | 합의 |
| MCP 설정 파일 | MCP 전용 YAML 형식. `Device`·`Settings`·`WifiProfiles`·KMS 섹션은 App과 같은 형식을 공유하고, `Commands`는 두지 않는다. 검증도 MCP에 맞게 따로 한다 | App YAML을 그대로 쓰고 `Commands` 무시 | 합의 |
| 설정·비밀값 | App과 같은 섹션 형식과 KMS 암호화를 쓴다. Wi‑Fi 비밀번호와 `mqtt_key`는 YAML에서만 읽고, tool 인자로는 SSID 이름만 받는다 | tool 인자로 비밀값 전달 | 합의 |
| 실행 단위 | tool 호출 1회 = 장치 AP 접속 → TCP → 핸드셰이크 → 명령 1개 → 종료 → PC Wi‑Fi 복구 | 여러 호출에 걸친 세션 유지 / 호출 1회에 명령 여러 개 | 합의 |
| 선택 지점 | 무선 어댑터·장치 AP·Wi‑Fi는 tool 인자로 받을 수 있다. 인자가 없고 후보가 여러 개면 MCP elicitation으로 사용자에게 고르게 한다. 후보가 1개면 그대로 쓴다 | 후보 목록 오류 반환 / elicitation + 오류 대체 / 항상 첫 후보 | 합의 |
| 공통 흐름 | `DeviceConfigService`의 흐름(장치 AP 탐색~명령 실행)과 `DeviceSettings` 생성을 라이브러리로 옮겨 App·MCP가 함께 쓴다. App 동작은 유지한다 | MCP에 흐름 복제 | 합의 |
| 로그 | MCP는 stdout을 프로토콜에 쓰므로, 로그는 stderr와 파일로만 보낸다(STDIO 전송의 필수 조건) | — | 합의 |
| 문서 카테고리 | `docs/mcp` | `docs/cli` | 합의 |

## 범위

| tool(가칭) | 동작 | 성격 |
|---|---|---|
| `list_wifi_adapters` | PC 무선 어댑터 목록(이름·ID·현재 연결 SSID) | 읽기 |
| `list_device_aps` | PC Wi‑Fi로 장치 AP(SSID 접두사) 스캔 | 읽기 |
| `get_device_info` | 핸드셰이크 → MAC | 읽기 |
| `scan_device_wifi` | `ApScan` | 읽기 |
| `configure_device` | `ConnectAp`(설정 JSON → `connectap`) | 변경 |
| `reboot_device` | `Reboot` | 변경 |
| `factory_reset_device` | `FactoryReset` | 변경 |
| `factory_test` | `ScanAp`/`OnCmd`/`OffCmd`/`Measurement`/`OverLoad`/`TestFactoryReset` | 명령별 |

변경 tool에는 MCP `destructiveHint`를 표시한다.

## 비범위

- HTTP(Streamable) MCP 전송, 원격 MCP 접속
- 무선 어댑터 1개 + 원격 LLM 환경 지원
- tool로 YAML 설정값(Wi‑Fi 목록·서버 주소 등)을 추가·변경하는 기능
- 장치 프로토콜 변경(#2 범위 유지), Reboot 응답 처리(#3)

## 미확인 항목

- Wi‑Fi + 유선 환경에서 장치 AP DHCP가 기본 게이트웨이를 주면 기본 경로가 Wi‑Fi로 바뀌는지(Windows 인터페이스 메트릭, Linux NetworkManager 경로 우선순위). 바뀐다면 유선 인터넷도 끊긴다.
- 무선 어댑터를 표시·지정할 식별자. Windows Native Wi‑Fi API는 인터페이스 GUID만 쓰므로, 사용자에게 보여 줄 이름(예: `Wi-Fi 2`)과 GUID를 대응시키는 방법
- 사용할 MCP 클라이언트가 elicitation을 지원하는지. 지원하지 않으면 후보가 여러 개일 때 선택할 수 없으므로, 설계에서 동작(오류 처리 등)을 정한다.
- MCP 클라이언트의 tool 호출 타임아웃이 장치 AP 접속·스캔·복구 시간보다 긴지
- Wi‑Fi 제어에 관리자 권한이 필요한 OS에서 MCP 클라이언트 실행 권한
