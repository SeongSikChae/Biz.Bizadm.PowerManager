---
status: done
category: cli
feature: device-config
phase: 실장비
---

# device-config 실장비 테스트 결과

선행: [0002-device-config-설계](0002-device-config-설계.md) 9.1·12절, [0003-device-config-개발](0003-device-config-개발.md) U10 · GitHub: SeongSikChae/Biz.Bizadm.PowerManager#2

## 0. 개요

- 일시: 2026-11-10 (Windows 11, PC Wi‑Fi로 장치 SoftAP 접속)
- 실행: 상태를 바꾸는 단계는 빌드한 `DeviceConfig.App` exe로, 읽기 단계는 `Device` 카테고리 테스트로 실행했다. 일부 원인 분석은 프로그램을 거치지 않고 TcpClient로 원시 바이트를 받았다.
- 대상 장치 이름: 공개 문서라 MAC·SSID·비밀번호·`mqtt_key`·주변 Wi‑Fi 이름은 가린다.

| 표기 | 의미 |
|---|---|
| 장치 T | 시험 대상 B540(`DWD-LS120_xxxxxx`, 사용자 지정). ①~⑧ 전 단계 대상 |
| 장치 P1~P5 | 같은 장소에서 운용 중인 B540 5대. 측정·읽기와 사용자 요청 시험에만 사용 |
| 공유기 A | 테스트 공유기 2.4GHz SSID |
| 공유기 B | 같은 공유기의 IoT용 2.4GHz SSID |

## 1. 단계별 결과

| 단계 | 결과 | 비고 |
|---|---|---|
| ① PC Wi‑Fi 장치 AP 탐색·접속·복구 | 통과 | SoftAP 개방형, 게이트웨이 `192.168.244.1`. 실행 후 매번 이전 상태(disconnected)로 복구, 임시 프로필 `B540-Config-Temp` 삭제 확인 |
| ② 핸드셰이크 | 통과 | `[DUT->PC] START_OK:<mac 12hex>#`. SSID 끝 6자리 = MAC 끝 3바이트 |
| ③ `Measurement`·`ScanAp` | 통과 | `SCAN_AP`는 `#` 프레임이 아니라 JSON 응답 → 회귀 수정(2485aa3). 장치 AP 접속 판단 결함 → 회귀 수정(6e5c7ef) |
| ④ `OnCmd`·`OffCmd` | 통과 | 부하 분리 상태에서 `ON_CMD_OK#`·`OFF_CMD_OK#`(약 0.3s). 부하가 있는 장치에서 측정값 변화로 릴레이 동작 확인(2.3절) |
| ⑤ `ApScan` | 통과 | 응답 형식·소요 시간 2.4절 |
| ⑥ `ConnectAp` | 통과(장치 응답 기준) | 설정 JSON·`connectap` 모두 `OK`. 공유기 실제 접속은 PC에서 확인 못 함(2.5절) |
| ⑦ `Reboot` | **설계와 불일치** | 응답도 연결 종료 신호도 없어 10s 타임아웃 → 실패 처리. 38s 뒤 재접속 정상(2.6절). 사용자 결정으로 코드는 바꾸지 않고 기록만 남김 |
| ⑧ `FactoryReset` | 통과 | `{"mac":"…","respone":"OK"}`(0.28s). 30s 뒤 같은 SSID로 재접속 정상 |
| ⑧ `TestFactoryReset` | 통과 | 응답 없이 4.7s 뒤 연결 종료 → 설계대로 경고 후 성공 처리. 30s 뒤 재접속 정상 |

## 2. 관찰 상세

### 2.1 측정값 형식

- 0W는 `MEASUREMENT_W:0.0#`, 그 밖은 소수 둘째 자리(`MEASUREMENT_W:153.95#`).
- 응답 간격: 요청 후 약 2.0s.

### 2.2 운용 장치 측정 (사용자 요청, 읽기 전용)

장치마다 연결 1회에 `Measurement`를 약 2s 간격으로 15회 보냈다.

| 장치 | 평균 | 중앙값 | 최소 | 최대 | 표준편차 |
|---|---|---|---|---|---|
| P1 | 1.72W | 1.70W | 1.26W | 2.34W | 0.30W |
| P2 | 0.90W | 0.89W | 0.0W | 1.38W | 0.34W |

- P2의 0.0W 1회는 저부하에서 순간적으로 0으로 잡힌 것으로 보인다(원인 미확인).
- 1회 측정: P3 153.95W, P4·P5 0.0W.

### 2.3 부하가 있는 장치의 릴레이 동작 (사용자 요청)

P1에서 원래 상태(ON)로 끝나도록 Off → On 순서로 실행했다.

| 순서 | 응답 |
|---|---|
| 측정 2회 | 1.74W, 1.91W |
| `OFF_CMD` | `OFF_CMD_OK#` (1.8s) |
| 측정 3회 | 0.0W ×3 |
| `ON_CMD` | `ON_CMD_OK#` (1.5s) |
| 측정 3회 | 3.67W, 2.14W, 1.65W |

- 부하가 있으면 `ON_CMD`/`OFF_CMD` 응답이 1.5~1.8s로 길어진다(부하 분리 장치 T는 약 0.3s). 기본 `TimeoutSeconds` 5s 안이다.

### 2.4 `apscan`

- 송신 `{"command":"apscan"}\n`, 응답은 약 2.1s 뒤 한 덩어리(185~198바이트).
- 형식: `{"SCAN_AP_LIST":{"<ssid>:<rssi>","<ssid>:<rssi>",...}}`. 겉모양은 JSON이지만 중괄호 안이 키 없는 문자열 나열이라 **정식 JSON이 아니다**. 현재 `ApScanParser`가 처리한다.
- 항목 수: 장치 T 6~9개, 운용 중인 P1은 4개.
- 장치 T의 결과에는 다른 B540 SoftAP가 나오지 않았다(PC 스캔에는 보임).
- 접속·핸드셰이크 포함 7.9s. `ApScanTimeoutSeconds` 기본 15는 충분하다.

### 2.5 `ConnectAp`

- 순서 `apscan` → Wi‑Fi 선택 → 설정 JSON → `connectap`을 **한 연결에서** 이어 보낼 수 있다.
- 설정 JSON 응답과 `connectap` 응답 모두 `{"mac":"<mac>","respone":"OK"}`. 장치 펌웨어의 키 이름이 `response`가 아니라 **`respone`(오타)**다.
- `mqtt_key`·`server_addr`·`server_port`를 포함한 설정 JSON에 `OK`로 응답했다. 다만 서버(#1)가 없어 자리표시자 주소(`*.example.local`)를 보냈으므로, 장치가 이 값을 실제로 쓰는지는 확인하지 못했다.
- 비밀번호는 환경 변수에서 스크립트 내부로만 주입했고, 실행 후 표준 출력·오류 출력·로그 파일·설정 파일에 평문이 없음을 확인했다. 설정 파일에는 `DEKENC:`로 저장, 로그에는 `pass`·`mqtt_key`가 `***`로 남았다.
- 공유기 접속 확인: PC에서는 확인할 수 없었다. `connectap` 80s 뒤에도 장치 T SoftAP로 재접속·측정이 됐다(SoftAP 유지). 사용자 관찰로는 장치가 공유기 A에 잠시 접속했다.

### 2.6 `Reboot`

- `{"command":"reboot"}\n` 송신 후 10s 동안 수신 데이터도 TCP 종료(FIN/RST)도 없었다. SoftAP가 사라지면서 PC 소켓이 종료를 통지받지 못한 것으로 보인다.
- 현재 구현은 `TimeoutException`으로 실패 처리한다(설계 4절 "응답 없이 연결이 끊기면 성공"과 다름). 명령 38s 뒤 재접속·측정은 정상이었다. 실제 재부팅 여부는 PC에서 확인하지 못했다.
- 후속: `Reboot` 송신 뒤 응답 타임아웃도 "명령 전송 완료"로 볼지 설계에서 정한다. 이번 작업에서는 사용자 결정으로 바꾸지 않았다. Reboot·초기화 계열의 실제 동작 확인(장비 LED 등 직접 관찰)과 함께 SeongSikChae/Biz.Bizadm.PowerManager#3으로 분리했다.

## 3. 공유기 채널 문제 (운용 주의)

공유기 A가 장치 T의 SoftAP와 **같은 채널(ch1)**에 있을 때 장치 T의 `apscan` 결과에서 공유기 A가 빠졌다. 그래서 `ConnectAp`가 `No registered Wi-Fi found in apscan result`로 멈췄다(장치에는 아무것도 보내지 않음, 설계대로).

| 조건 | 장치 T (SoftAP ch1) | P1 (SoftAP ch5) |
|---|---|---|
| 공유기 A ch1 | 6회(프로그램 3, 원시 캡처 3) + 재현 2회 모두 **없음** | 재현 2회 모두 있음(-51, -47 dBm) |
| 공유기 A ch4/5(사용자가 변경) | 있음(-44 dBm, 가장 강함) | — |

- 원시 캡처로 장치가 보낸 바이트 자체에 없음을 확인했다. 수신 버퍼·프레임 분리·파서 원인이 아니다. 같은 응답에 -93 dBm AP는 있어 "신호 상위 N개" 제한으로도 설명되지 않는다.
- P1이 ch1의 공유기 A를 찾으므로 ch1 자체 문제는 아니다. 장치 SoftAP와 같은 채널의 AP가 빠지는 것으로 본다.
- 배제하지 못한 것: 장치 T 개체 차이, 장치 T에 저장돼 있던 설정의 영향.
- 운용 주의: 등록 Wi‑Fi가 장치 스캔에 나오지 않으면 공유기 채널을 바꿔 다시 시도한다.

## 4. 설계 12절 미확인 항목 결과

| 항목 | 결과 |
|---|---|
| `server_addr`·`server_port` 사용 여부 | 미확인. 장치는 키를 포함한 설정 JSON에 `OK`로 응답했으나 실제 사용은 서버(#1) 필요 |
| `mqtt_key` TCP 수용 | 설정 JSON에 포함해 `OK` 응답. 서버 쪽 사용은 미확인 |
| `apscan` 응답 형식·소요 시간 | 확정(2.4절). 형식은 설계 추정과 같음. 약 2.1s, 기본 대기 15s 유지 |
| `apscan` 뒤 같은 연결에서 설정·`connectap` | 확정. 가능 |
| Windows 11 Native Wifi 스캔 권한 | 이번 PC에서는 접근 거부 없이 스캔됨 |
| Linux·macOS 구현 | 미확인(실행하지 않음) |

그 밖에 이번에 새로 확인한 것: 장치 응답 키 오타 `respone`, `Reboot` 응답 없음(2.6절), 같은 채널 AP 누락(3절), `FACTORY_RESET`(공장검사)은 응답 없이 연결 종료, `factoryreset`(JSON)은 `OK` 응답.

## 5. 미검증 범위

- 장치가 공유기에 실제로 접속했는지(공유기 DHCP 목록 확인 필요), 서버 접속 동작
- `Reboot`·`FactoryReset`·`TestFactoryReset` 뒤 실제 재부팅·초기화 여부(LED·저장 설정 확인 필요) → #3에서 장비 직접 확인
- `OverLoad` 명령(실행하지 않음)
- Linux·macOS 실행
