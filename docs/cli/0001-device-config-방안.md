---
status: done
category: cli
feature: device-config
phase: 방안
---

# device-config 방안

GitHub: SeongSikChae/Biz.Bizadm.PowerManager#2

## 문제

B540(DAWONDNS `B540_W`, ESP8266)을 자체 서버(#1, `docs/server/`)에 붙이려면 장치에 Wi‑Fi, API 서버, MQTT 서버, 토픽, FOTA 값을 넣어야 한다. 장치는 SoftAP 상태에서 TCP로 설정을 받는다. 지금은 이 설정을 넣을 도구가 저장소에 없다.

근거:

- 정적 분석: `docs/.tmp/b540-tcp-config.md`, `b540-fota.md`, `b540-rest-api.md`
- 참고 구현: [SeongSikChae/PowerManagerConfig](https://github.com/SeongSikChae/PowerManagerConfig) (프로토콜만 참고, 코드·구조는 가져오지 않는다)

## 목표 (1차)

- 장치 SoftAP TCP 채널로 설정을 보내고 명령을 실행하는 콘솔 도구를 만든다. 설정값과 실행할 명령은 YAML 설정 파일로 받는다.
- 정적 분석과 참고 구현에서 확인한 프로토콜만 구현한다.

## 결정 사항

| 항목 | 채택 | 대안(미채택) |
|---|---|---|
| 진행 절차 | work-process 전체(방안→설계→개발→검토→구현) | 바로 구현 |
| 프로젝트 형태 | 콘솔 템플릿(Generic Host, `--config`/`--log` 필수). 설정값·명령은 YAML. 프로토콜 코드는 실행 프로젝트와 분리 | verb형 CLI 도구(설계 논의 중 변경) |
| 명령 범위 | TCP 설정 명령 4개 + 공장검사 명령 | 4개만 / 4개 + raw JSON |
| TCP 포트 | 기본 `5000`, 옵션으로 변경 가능 | 포트 필수 입력 |
| 참고 구현 범위 | 소켓 프로토콜(핸드셰이크·설정 JSON·`connectap`) | REST 키 발급·등록 흐름 이식 |
| `mqtt_key` | 서버(#1) 발급 전까지 CLI가 비어 있으면 자동 생성해 TCP로 전송. 서버가 발급하게 되면 서버로 이관 | 서버 발급 전용 / 사용자 입력만 |
| Wi‑Fi | 장치 YAML에 Wi‑Fi 목록(SSID·Pass)을 등록하고, `ConnectAp` 때 장치 `apscan` 결과와 대조해 사용자가 선택. 선택 지점은 나중에 로컬 MCP 등으로 교체 가능하게 분리 | 단일 SSID 고정 |
| 장치 접속 | PC Wi‑Fi로 장치 AP(SSID 접두사, 기본 `DWD-`)를 스캔해 선택·자동 접속하고 게이트웨이를 사용. 없으면 설정 IP. 끝나면 원래 네트워크로 복구. Windows·Linux·macOS 각각 구현 | 사용자가 미리 접속, 설정 IP 고정 |
| 문서 카테고리 | `docs/cli` | — |

### TCP 포트 근거

- 참고 구현: B540 v1.0.30/v1.0.32 실기 테스트에서 `5000` 사용(README: "현재 5000번 포트만 이용중인 것으로 파악").
- 덤프(관찰): u32 LE `5000`(`88 13 00 00`)이 슬롯 A `0x1b7e0`, 슬롯 B `0x9b800`에 각 1회 있다. 두 위치의 간격 `0x80020`은 슬롯 간격과 맞는다. 이 값을 `espconn` 포트 설정 코드가 참조하는지(xref)는 미확인이다.

## 프로토콜 요약

### 참고 구현에서 추가로 확인한 사항

| 단계 | 방향 | 내용 |
|---|---|---|
| 접속 | PC → 장치 | 장치 SoftAP 게이트웨이 IP, TCP `5000` |
| 시작 | PC → 장치 | `[DUT<-PC] START\n` |
| 인사 | PC → 장치 | `hello tcp SUCCESS_CONNECT\n` |
| MAC 응답 | 장치 → PC | `[DUT->PC] START_OK:<mac>#` (B550은 앞에 `[DUT->PC] BUTTON_...`이 올 수 있음) |
| 설정 | PC → 장치 | 설정 JSON 한 줄 + `\n` (`command` 키 없음) |
| 설정 응답 | 장치 → PC | 응답 문자열 수신 |
| 접속 지시 | PC → 장치 | `{"mac":"<mac>","command":"connectap"}\n` |
| 접속 응답 | 장치 → PC | `{"mac":"%s","respone":"OK"}` (정적 분석 문자열) |

- 메시지 경계는 `\n`이다. 수신은 단일 `Receive`로 처리했다.
- 공장검사 명령은 `[DUT<-PC] <명령>\n` 형식이고, 응답은 `[DUT->PC] <응답>#` 형식이다. 따라서 TCP 설정 채널과 같은 채널을 쓴다(정적 분석의 "같은 채널 여부 미확인"을 해소).
- 참고 구현 V2(B540 v1.0.28)는 `connectap` 대신 `{"delay":"0"}`을 보냈다. 이번 범위는 V3 흐름이다.

### 설정 JSON 키

참고 구현 V3(B540 v1.0.30/v1.0.32)에서 보낸 키: `mac`, `api_server_addr`, `api_server_port`, `server_addr`, `server_port`, `ssl_support`(`yes`/`no`), `ssid`, `pass`, `user_id`, `company`, `model`, `lati`, `long`, `topic`. 값은 모두 문자열이다.

정적 분석에서만 확인한 키: `mqtt_key`(참고 V1에서 사용), `delay`, `verify`, `LFota_Url`, `LFota_Addr`, `LFota_Port`, `LFota_Version`.

## 범위

| 명령(가칭) | 장치 송신 |
|---|---|
| `connectap` | 핸드셰이크 → 설정 JSON → `connectap` |
| `apscan` | 핸드셰이크 → `{"command":"apscan"}` |
| `reboot` | 핸드셰이크 → `{"command":"reboot"}` |
| `factoryreset` | 핸드셰이크 → `{"command":"factoryreset"}` |
| 공장검사 | `[DUT<-PC] START`/`SCAN_AP`/`ON_CMD`/`OFF_CMD`/`MEASUREMENT`/`OVERLOAD`/`FACTORY_RESET` |

대상 펌웨어: 덤프 버전 `1.01.30`(슬롯 A)·`1.01.34`(슬롯 B). 참고 구현 V3와 같은 흐름을 기본으로 한다.

## 비범위

- 참고 구현의 REST 흐름(`GetMqttAuth`, `MqttAuthAdd`, `MqttKeyChange`, 클라이언트 인증서)
- 자체 서버(#1) 코드 변경
- 장치 SoftAP Wi‑Fi 접속 자동화(PC가 장치 AP에 먼저 접속해 있다고 가정)
- B550 등 다른 모델 전용 흐름, V1/V2 흐름

## 미확인 항목 (구현·실기 시 확인)

- `apscan`/`reboot`/`factoryreset`을 핸드셰이크 뒤에 보내야 하는지, JSON 형식이 맞는지(참고 구현은 `connectap`만 사용)
- 설정 JSON에 `LFota_*`, `mqtt_key`, `verify`를 함께 보내도 장치가 받는지
- 설정 응답 문자열 형식
- 실장치 검증(현재 장치·캡처 없음)
