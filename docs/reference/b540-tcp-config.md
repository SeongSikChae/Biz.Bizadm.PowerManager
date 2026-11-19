# B540 TCP 설정 수신 프로토콜

- 작성: 2026-11-03 (KST)
- 근거: B540 플래시 덤프의 정적 문자열·설정 섹터 바이트. 실제 앱 ↔ 장치 통신은 관찰하지 않았다.
- 표기: **관찰**, **추정**, **미확인**. 개인정보·키 값은 마스킹했다.
- 관련 문서: [MQTT 토픽](b540-mqtt-topic.md), [REST API](b540-rest-api.md), [FOTA](b540-fota.md)

## 1. 연결 구성

| 항목 | 값 | 근거 |
|---|---|---|
| 장치 AP SSID | `DWD-LS120_%02x%02x%02x` (덤프: `DWD-LS120_xxxxxx`) | 관찰: 형식 `0xdefc0`, SDK 파라미터 `0xfd0b4`/`0xfe0b4`. 끝 3바이트는 MAC 하위로 추정 |
| AP 설정 | `user_set_softap_config`, `Set ApConfig`, `dhcp server start` | 관찰 |
| TCP 서버 | `user_tcplocalServer_init`, `TCPSERVER RX FRAME` | 관찰. ESP `espconn` TCP 서버 |
| 수신 포트 | — | **미확인**(코드의 `espconn` 포트 설정을 아직 찾지 않음) |
| 장치 IP | `%d.%d.%d.%d` 형식 | 추정: SoftAP 기본 `192.168.4.1`. 미확인 |
| 응답 전송 | `tcp sent succeed !!!` | 관찰 |

추정 사용 흐름: 스마트폰 앱이 장치 AP에 접속 → TCP로 JSON 설정 전송 → 장치가 저장·응답 → 장치가 공유기에 접속해 [REST API](b540-rest-api.md) 등록과 [MQTT](b540-mqtt-topic.md) 접속을 진행한다.

## 2. 요청 형식

JSON 객체 하나에 `command`와 설정 키를 함께 보내는 것으로 추정한다. 키 이름은 관찰, 조합·필수 여부는 미확인이다.

### `command` 값(관찰)

| 값 | 추정 동작 | 응답(관찰) |
|---|---|---|
| `connectap` | 설정 저장 후 공유기 접속 | `{"mac":"%s","respone":"OK"}` |
| `apscan` | 주변 AP 스캔 | `{"SCAN_AP_LIST":{"<ssid>:<rssi>","<ssid>:<rssi>",...}}` |
| `reboot` | 재부팅 | `{"mac":"%s","respone":"reboot"}` |
| `factoryreset` | 공장 초기화 | 미확인 |

`respone`는 원문 철자 그대로다. 스캔 목록은 `%s"%s:%d"`, `%s,"%s:%d"`, `%s}}` 형식을 이어 붙인다(관찰). 객체 안에 키 없이 문자열을 나열하므로 엄격한 JSON이 아니다.

### 설정 키(관찰)

| 키 | 추정 의미 | 덤프 설정 섹터 값 |
|---|---|---|
| `ssid`, `pass` | 공유기 Wi‑Fi | 마스킹 |
| `api_server_addr`, `api_server_port` | REST API 서버 | `dwapi.dawonai.com`, `18443` |
| `ssl_support` | TLS 사용 여부 | 미확인 |
| `mqtt_key` | MQTT 인증 키 | 16자(마스킹) |
| `topic` | MQTT 토픽 접두 | `dwd` |
| `user_id` | 계정 ID | `<user>@<domain>/kakao` |
| `company`, `model` | 회사·모델 | `DAWONDNS`, `B540_W` |
| `lati`, `long` | 위도·경도 | 마스킹 |
| `delay` | 지연 시간(용도 미확인) | 미확인 |
| `verify` | 검증 플래그 | 미확인 |
| `LFota_Url`, `LFota_Addr`, `LFota_Port`, `LFota_Version` | 로컬 FOTA 지시 | [FOTA](b540-fota.md) |

예시(추정, 값은 가상):

```json
{"command":"connectap","ssid":"MyAP","pass":"********","user_id":"user@example.com/kakao",
 "company":"DAWONDNS","model":"B540_W","lati":"37.0","long":"127.0",
 "api_server_addr":"dwapi.dawonai.com","api_server_port":"18443","ssl_support":"yes",
 "mqtt_key":"****************","topic":"dwd"}
```

`ssl_support`의 `yes`는 근처 문자열(`0xdf19c`)에서 본 후보이며 확정이 아니다. 값이 문자열인지 숫자인지도 미확인이다.

## 3. 설정 저장

제품 설정은 `0x7c000`, `0x7d000`의 4 KiB 섹터 2벌에 같은 내용으로 저장된다(관찰). 필드 배치 추정:

| 오프셋 | 내용(덤프) |
|---|---|
| `+0x000` | 문자열(마스킹, `ssid` 후보) |
| `+0x021` | 문자열(마스킹, `pass` 후보) |
| `+0x062` | 16자 문자열(마스킹, `mqtt_key` 후보) |
| `+0x083` | `dwmqtt.dawonai.com` |
| `+0x0a4` | `user_id` |
| `+0x0c8` | `0x22b3` = 8883 (MQTT 포트) |
| `+0x0ce`, `+0x0dd` | 위도, 경도 |
| `+0x0ec` | `dwd` (토픽 접두) |
| `+0x0fb` | `DAWONDNS` |
| `+0x10a` | `B540_W` |
| `+0x2cd` | `dwapi.dawonai.com` |
| `+0x2f0` | `0x480b` = 18443 (API 포트) |
| `+0x2f5` | `1.01.34` (펌웨어 버전) |

정수 필드 `+0x130`=60, `+0x230`=300, `+0x234`=3500, `+0x238`=20의 의미는 미확인이다. 두 섹터 중 어느 쪽을 먼저 읽는지도 미확인이다.

## 4. 공장 검사 명령(같은 채널 여부 미확인)

`[DUT<-PC]` 요청과 `[DUT->PC]` 응답 로그가 있다. TCP 설정 채널과 같은 통로인지, UART인지는 미확인이다.

| 요청 | 응답 |
|---|---|
| `START` | `START_OK:` |
| `SCAN_AP` | `SCAN_AP_LIST:` (`%s%s:%d`, `%s;%s:%d`) |
| `ON_CMD` / `OFF_CMD` | `ON_CMD_OK` / `OFF_CMD_OK` |
| `MEASUREMENT` | `MEASUREMENT_W:` (`%s%d.%d#`) |
| `OVERLOAD` | `OVERLOAD_A:` |
| `FACTORY_RESET` (슬롯 B만) | 미확인 |

비요청 이벤트: `BUTTON_ON_EVT`, `BUTTON_OFF_EVT`. 응답 끝 `#` 구분자는 `%s%s#` 형식에서 추정했다.

## 5. 미확인 항목

- TCP 수신 포트, 프레임 경계(길이 접두·개행·단일 패킷 여부)
- 키 필수 여부, 값 타입, 검증 실패 응답
- 설정 섹터 이중화 선택 규칙
- 실제 앱 통신 캡처 대조
