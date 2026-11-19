# B540 장치 등록 REST API

- 작성: 2026-11-03 (KST)
- 근거: B540 플래시 덤프의 정적 문자열·설정 섹터 바이트. 실제 HTTP 요청·응답은 관찰하지 않았다.
- 표기: **관찰**, **추정**, **미확인**. 개인정보·키 값은 마스킹했다.
- 관련 문서: [MQTT 토픽](b540-mqtt-topic.md), [FOTA](b540-fota.md), [TCP 설정 수신](b540-tcp-config.md)

## 1. 서버

| 항목 | 값 | 근거 |
|---|---|---|
| 호스트 | `dwapi.dawonai.com` | 관찰: 설정 섹터 `0x7c2cd`/`0x7d2cd` |
| 포트 | `18443` (`0x480b`, LE) | 관찰: 설정 섹터 `0x7c2f0`. 오프셋과 필드 대응은 추정 |
| TLS 사용 | 설정 키 `ssl_support` | 관찰. 기본값·전환 동작은 미확인 |
| 변경 경로 | TCP 설정 키 `api_server_addr`, `api_server_port` | 관찰. [TCP 설정 수신](b540-tcp-config.md) |

요청은 HTTP 원문을 `sprintf`형 형식 문자열로 직접 만든다(관찰).

## 2. 장치 등록 — `POST /api/v1/devices/register/create`

요청 형식(관찰):

```http
POST /api/v1/devices/register/create HTTP/1.1
Host:%s
Content-Length:%s
Content-Type:application/json
X-HIT-Version:1.0

%s
```

본문 형식 두 가지(관찰):

```json
{"account":{"user_id":"%s"},"device":{"mac":"%s","model_id":"%s","company":"%s","lati":"%s","long":"%s","verify":"true"}}
```

```json
{"account":{"user_id":"%s"},"device":{"mac":"%s","model_id":"%s","company":"%s","lati":"%s","long":"%s","status":"%s","verify":"true"}}
```

| 필드 | 추정 출처 | 덤프 값 |
|---|---|---|
| `user_id` | TCP 설정 `user_id` | 이메일+`/kakao` 형식(마스킹: `<user>@<domain>/kakao`) |
| `mac` | Wi‑Fi MAC `%02x%02x%02x%02x%02x%02x` | 미기재 |
| `model_id` | TCP 설정 `model` | `B540_W` |
| `company` | TCP 설정 `company` | `DAWONDNS` |
| `lati`, `long` | TCP 설정 `lati`, `long` | 위경도 문자열(마스킹) |
| `status` | 미확인 | 두 번째 형식에서만 사용 |

응답 처리(추정): 장치 데이터 영역에 `status`, `REGISTERED`, `reason` 키 문자열이 있어 응답 JSON의 `status`가 `REGISTERED`인지 확인하고 실패 사유를 `reason`에서 읽는 것으로 본다. 두 본문 형식의 선택 조건은 미확인이다.

## 3. 접속 정보 조회 — `GET /api/v1/device/connect/{a}-{b}-{c}`

```http
GET /api/v1/device/connect/%s-%s-%s HTTP/1.1
Host:%s

```

- 3요소는 MQTT 토픽의 `%s-%s-%s`와 같은 구성으로 추정한다(회사·모델·MAC). 순서는 미확인.
- 관련 함수 이름(관찰): `SendDataGetMqttStart`, `SendDataGetKey`, `user_key_check_ip`, `user_esp_platform_dns_found`.
- 추정: 응답으로 MQTT 접속 키(`mqtt_key`) 또는 접속 시작 정보를 받은 뒤 [MQTT](b540-mqtt-topic.md) 접속을 시작한다. 응답 형식은 미확인.
- 앱 데이터에 65자 치환 문자표 `XGAqW.Ibj3...` (`0xdf0df`)가 있다. Base64와 같은 길이의 사용자 정의 인코딩 표로 추정하며, 키 인코딩 사용 여부는 미확인이다.

## 4. 호출 순서(추정)

1. TCP 설정 수신으로 Wi‑Fi·서버·계정 정보 저장
2. Wi‑Fi 접속 후 `dwapi` DNS 조회
3. `register/create` 등록 → 응답 `status` 확인
4. `device/connect` 조회 → MQTT 키·접속 시작
5. MQTT `rg` 등록 메시지 발행

## 5. 미확인 항목

- 응답 본문 스키마, HTTP 상태 코드 처리, 재시도 정책
- `device/connect` 경로 3요소 순서와 응답 값
- TLS 사용 시 인증서 검증 여부
- 실제 서버 응답 캡처 대조
