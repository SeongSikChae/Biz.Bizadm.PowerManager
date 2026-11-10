---
status: done
category: cli
feature: device-config
phase: 방안
---

# device-config 방안

GitHub: SeongSikChae/Biz.Bizadm.PowerManager#6

## 문제

#2에서 만든 장치 설정 CLI는 B540 스마트 플러그(DAWONDNS `B540_W`, ESP8266)를 장치 SoftAP(TCP 5000)로 설정한다. PC Wi‑Fi 구현이 Windows·Linux·macOS를 런타임 분기로 모두 담고 있는데, Linux·macOS는 개발 PC에서 실행 검증을 할 수 없다. 이 위에 MCP 서버를 얹는 계획(#4)을 검토하는 동안 검증할 수 없는 OS별 대응이 계속 늘어 설계가 과도하게 복잡해졌다. 그래서 #3·#4·#5를 폐기하고 CLI부터 단순한 형태로 다시 만든다.

## 목표

- 같은 장치 프로토콜·명령을 지원하는 CLI를 **가장 단순한 형태**로 새로 작성한다.
- 검증할 수 없는 OS별 예외·방어 로직을 넣지 않는다.
- MCP는 이 CLI 완료 후 별도 계획으로 진행한다.

## 결정 사항

| 항목 | 채택 | 대안(미채택) |
|---|---|---|
| 기존 코드 | 참고만 하고 전부 새로 작성(프로토콜·전송·메시지·Windows WLAN 포함). 먼저 기존 #2 프로젝트(`src/Biz.Bizadm.PowerManager.DeviceConfig`, `src/Biz.Bizadm.PowerManager.DeviceConfig.App`, `tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests`)를 삭제한 뒤 새 프로젝트를 생성한다. 기존 코드는 git 이력에서 참고한다 | 새 프로젝트 완성 후 삭제 / 기존 프로젝트와 병존 |
| 기존 문서 | `docs/cli/0001~0004`(#2 방안·설계·개발·실장비)를 삭제하고 이 문서부터 `0001`로 다시 시작한다. 필요한 실장비 관찰은 아래 "실장비 관찰 요약"에 남긴다(원본은 git 이력) | 기존 문서 뒤에 `0005`부터 이어 쓰기 |
| PC Wi‑Fi 구현 범위 | Windows만 구현. Linux·macOS는 모든 메서드가 `NotImplementedException`을 던지는 스텁 | 세 OS 모두 구현 |
| OS별 구조 | `Biz.Bizadm.KMS.Credentials`와 같은 방식. facade 어셈블리가 공통 API와 `Type.GetType("<Factory>, <Runtime 어셈블리명>")` 리플렉션 로더를 두고, `Runtime.win`·`Runtime.linux`·`Runtime.osx` 프로젝트가 같은 AssemblyName으로 OS별 구현을 둔다. App은 RID 조건 `ProjectReference`(RuntimeIdentifier의 win/linux/osx, RID가 없으면 빌드 호스트 OS)로 해당 OS 구현 하나만 참조하고 `dotnet publish -r <rid>`로 배포한다. NuGet 패키지화는 하지 않는다 | 한 어셈블리 안 런타임 분기(#2 방식) |
| 무선 어댑터 | 장치 AP 접속에 쓸 무선 어댑터를 선택할 수 있다. App은 콘솔에서 번호로 고른다 | 첫 어댑터 고정 |
| 미구현 OS 동작 | PC Wi‑Fi 호출이 미구현(`NotImplementedException`)으로 실패하면 경고를 남기고 설정 IP(`Device.Host`)로 접속한다. Windows의 실제 실패는 그대로 실패 처리한다 | 실행 중단 |
| 명령 범위 | #2와 같다. 설정 명령 `ConnectAp`·`ApScan`·`Reboot`·`FactoryReset` + 공장검사 명령 7개(`START`·`SCAN_AP`·`ON_CMD`·`OFF_CMD`·`MEASUREMENT`·`OVERLOAD`·`FACTORY_RESET`) | 설정 명령 4개만 / `ConnectAp`만 |
| 입력 방식 | 콘솔 템플릿(`--config`/`--log`). 설정값과 실행할 명령은 YAML 설정 파일로 받고, `Commands` 목록을 순서대로 실행한다 | verb형 명령행 |
| 공유기 Wi‑Fi | YAML에 공유기 Wi‑Fi 목록(SSID·Pass)을 등록한다. `ConnectAp` 때 장치 `apscan` 결과와 대조해 보이는 것만 콘솔에 번호로 보여 주고 사용자가 고른다 | 단일 SSID 그대로 전송 / 단일 SSID + `apscan` 확인 |
| 보안값 | 공유기 Wi‑Fi Pass 등 설정 파일의 보안값은 규칙 `dotnet/kms-config-secrets.md`대로 OsKms(OS 자격 증명 KEK + 로컬 DEK)로 암호화한다. 기동 시 평문이면 암호화해 설정 파일에 다시 쓴다 | 평문 |
| `mqtt_key` | 설정 파일에 항목을 두지 않는다. 서버(#1)가 발급하기 전까지 CLI가 실행할 때마다 내부에서 임의로 생성해 설정 JSON에 넣고, 생성한 값은 설정 파일에 기록하지 않는다 | 선택 항목(있으면 그 값) / 생성 후 설정 파일에 기록 / 필수 입력 / 키 생략 |
| 장치 AP 선택 | PC Wi‑Fi로 SSID 접두사(기본 `DWD-`) AP를 스캔해 콘솔에 번호로 보여 주고 사용자가 고른다(하나만 있어도 선택) | 설정에 대상 SSID 지정 |
| PC Wi‑Fi 복구 | 작업이 끝나면 접속 전 상태(연결이 없었으면 연결 해제)로 되돌리고 임시 프로필을 삭제한다 | 장치 AP 접속 상태로 둠 |
| 응답 대기 | 모든 명령 공통 단일 값, 기본 10s. 설정으로 바꿀 수 있다 | 일반 5s + `apscan` 15s(#2) |
| 명령 순서 | 검사하지 않는다. `Commands`에 적힌 순서대로 실행하며, SoftAP를 떠나게 하는 명령 뒤의 명령은 실패할 수 있다 | 연결을 끝내는 명령은 목록 마지막에만 허용(#2) |
| 테스트 | 단위 테스트(파서·메시지·프레임) + 로컬 TCP 모의 장치 통합 테스트 + 기본 실행에서 제외하는 `Device` 카테고리 실장비 테스트 | 단위 + 모의 장치만 |
| `Reboot` 등 무응답 명령 | 실장비에서 다시 확인한 뒤 처리 방식을 확정한다(아래 "미확인 항목") | — |
| 문서 카테고리 | `docs/cli` | — |

## 프로토콜 요약

#2 정적 분석·참고 구현([SeongSikChae/PowerManagerConfig](https://github.com/SeongSikChae/PowerManagerConfig))·실장비에서 확인한 내용이다.

| 단계 | 방향 | 내용 |
|---|---|---|
| 접속 | PC → 장치 | 장치 SoftAP 게이트웨이 IP(실장비 `192.168.244.1`), TCP `5000` |
| 시작 | PC → 장치 | `[DUT<-PC] START\n` |
| 인사 | PC → 장치 | `hello tcp SUCCESS_CONNECT\n` |
| MAC 응답 | 장치 → PC | `[DUT->PC] START_OK:<mac 12hex>#` |
| 설정 | PC → 장치 | 설정 JSON 한 줄 + `\n` (`command` 키 없음) |
| 설정 응답 | 장치 → PC | `{"mac":"<mac>","respone":"OK"}` |
| 접속 지시 | PC → 장치 | `{"mac":"<mac>","command":"connectap"}\n` |
| 접속 응답 | 장치 → PC | `{"mac":"<mac>","respone":"OK"}` |

- 송신 메시지 경계는 `\n`이다.
- 설정 JSON 키(값은 모두 문자열): `mac`, `api_server_addr`, `api_server_port`, `server_addr`, `server_port`, `ssl_support`(`yes`/`no`), `ssid`, `pass`, `user_id`, `company`, `model`, `lati`, `long`, `topic`, `mqtt_key`.
- 공장검사 명령은 `[DUT<-PC] <명령>\n`, 응답은 `[DUT->PC] <응답>#`이다(`SCAN_AP`는 예외, 아래 관찰).

## 실장비 관찰 요약 (#2, 2026-11-10)

| 항목 | 관찰 |
|---|---|
| SoftAP | 개방형, SSID `DWD-…`, SSID 끝 6자리 = MAC 끝 3바이트 |
| 응답 키 | `response`가 아니라 펌웨어 오타 `respone` |
| `apscan` | `{"command":"apscan"}\n` → 약 2.1s 뒤 `{"SCAN_AP_LIST":{"<ssid>:<rssi>",...}}`. 중괄호 안이 키 없는 문자열 나열이라 정식 JSON이 아니다 |
| `SCAN_AP`(공장검사) | `#` 프레임이 아니라 `apscan`과 같은 형식의 응답 |
| `apscan` 후 같은 연결 | 설정 JSON·`connectap`을 이어 보낼 수 있다 |
| `ON_CMD`/`OFF_CMD` | `ON_CMD_OK#`/`OFF_CMD_OK#`. 부하 없으면 약 0.3s, 부하 있으면 1.5~1.8s |
| `MEASUREMENT` | `MEASUREMENT_W:<값>#`(0W는 `0.0`), 약 2.0s |
| `OVERLOAD` | 응답 4.5~4.8s(#5 관찰). #2 기본 응답 대기 5s에 가까웠다(새 기본값 10s) |
| `Reboot` | 응답도 TCP 종료도 없이 10s 타임아웃. 약 38s 뒤 재접속 정상(#3) |
| `factoryreset`(JSON) | `{"mac":"…","respone":"OK"}`(0.28s), 약 30s 뒤 재접속 정상 |
| `FACTORY_RESET`(공장검사) | 응답 없이 약 4.7s 뒤 연결 종료, 약 30s 뒤 재접속 정상 |
| 같은 채널 AP 누락 | 공유기가 장치 SoftAP와 같은 채널이면 장치 `apscan` 결과에서 빠졌다. 운용 시 공유기 채널을 바꿔 다시 시도한다 |

## 미확인 항목

- `Reboot`·`FACTORY_RESET`(공장검사) 등 응답·연결 종료 없이 장치가 사라지는 명령의 처리 방식: 실장비에서 실제 재부팅·초기화 여부(LED·저장 설정)와 응답을 다시 확인한 뒤 확정한다.
- 장치가 공유기·서버(#1)에 실제로 접속하는지(공유기 DHCP 목록·서버 필요).

## 비범위

- MCP 서버(새 CLI 완료 후 별도 계획)
- Linux·macOS PC Wi‑Fi 구현(스텁만 둔다)
- 참고 구현의 REST 흐름(`GetMqttAuth`, `MqttAuthAdd`, `MqttKeyChange`, 클라이언트 인증서)
- 자체 서버(#1) 코드 변경
- 서버 MQTT 인증 확인: 실행마다 임의로 만든 `mqtt_key`는 서버가 모르므로 장치가 MQTT 인증을 할 수 없다. 서버 발급·인증 확인은 #1 이후에 한다
- B550 등 다른 모델 전용 흐름, V1/V2 흐름
- NuGet 패키지화
