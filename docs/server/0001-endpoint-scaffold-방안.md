---
status: done
category: server
feature: endpoint-scaffold
phase: 방안
---

# endpoint-scaffold 방안

## 문제

B540(DAWONDNS `B540_W`, ESP8266) 스마트 플러그는 제조사 클라우드(`dwapi.dawonai.com:18443`, `dwmqtt.dawonai.com:8883`)에 의존한다. 장치의 서버 주소는 TCP 설정 수신(`api_server_addr`, `api_server_port`, `topic`, `LFota_*`)으로 바꿀 수 있으므로, 같은 계약을 제공하는 자체 서버를 만들면 장치를 직접 관리할 수 있다.

근거 문서: `docs/.tmp/b540-rest-api.md`, `b540-mqtt-topic.md`, `b540-fota.md`, `b540-tcp-config.md`. `docs/.tmp/`는 `.gitignore` 대상이라 저장소에 커밋되지 않는다(로컬 분석 자료).

## 목표 (1차)

- ASP.NET Core 서버와 React 클라이언트 솔루션 골격을 만든다.
- 장치가 호출하는 endpoint를 **껍데기**로 노출한다. 요청을 받아 로그를 남기고 고정·빈 응답을 돌려줄 뿐 내부 처리는 하지 않는다.
- React는 프로젝트 생성 수준으로 둔다.

## 결정 사항

| 항목 | 채택 | 대안(미채택) |
|---|---|---|
| 진행 절차 | work-process 전체(방안→설계→개발→검토→구현) | 바로 구현 |
| 서버 템플릿 | `server-spa-template`: `Server.Core` + `Server.Spa`(React 호스팅) + `Client` | `server-api-template` + 독립 Client |
| MQTT 역할 | 서버 **내장 브로커**(MQTTnet 서버, TLS 8883(`Tls.UseTls` 시) + 비TLS 1803) + 토픽 수신 핸들러·발행 서비스 껍데기 | 외부 브로커(Mosquitto 등)에 클라이언트로 접속 |
| HTTP 포트·Redirect | HTTP 80·18446, HTTPS 443·18443. TLS 사용 시 HTTP 요청은 대응 HTTPS 포트로 307 Redirect하되, **`/fota` 세그먼트 경로(`/fota`, `/fota/...`)만 Redirect에서 제외**하고 HTTP로 계속 받는다. FOTA SDK(`upgrade_lib`)는 기본 HTTP이고 HTTPS 다운로드 지원이 미확인(`b540-fota.md` §5)이라, 전부 Redirect하면 TLS 운영 중 FOTA가 불가능해질 수 있기 때문이다. 장치 등록 REST는 HTTPS URL 설정을 전제로 한다 | 장치 경로 전부 HTTP 허용, 예외 없는 전체 Redirect |
| 문서 카테고리 | `docs/server` | — |

## endpoint 범위

| 구분 | endpoint | 근거 |
|---|---|---|
| 장치 REST | `POST /api/v1/devices/register/create` | `b540-rest-api.md` §2 |
| 장치 REST | `GET /api/v1/device/connect/{a}-{b}-{c}` | `b540-rest-api.md` §3 |
| MQTT | 내장 브로커(8883 TLS — `Tls.UseTls` 시, MQTT 3.1 `MQIsdp` 수용), LWT `/lwt` `offline` | `b540-mqtt-topic.md` §1 |
| MQTT | 내장 브로커 비TLS 리스너 1803(같은 토픽·핸들러 공유) | 사용자 요구 |
| MQTT 수신 | 장치 → 서버 `rg`, `drg`(방향 추정, 근거 문서는 "등록 해제"만 기재), `tisyn`, `notify` | `b540-mqtt-topic.md` §2–3 |
| MQTT 발행 | 서버 → 장치 `w`, `chg`, `reb` | `b540-mqtt-topic.md` §3 |
| FOTA HTTP | `GET`·`HEAD {path}/version`, `GET`·`HEAD {path}{image}`(HEAD는 SDK의 HEAD→GET 재시도 대비) | `b540-fota.md` §3 |

## 비범위

- 관리 API(React용 장치 목록·제어)
- endpoint 내부 로직(등록 처리, 키 발급, 시간 동기 응답 생성, 릴레이 제어 등)
- 데이터 영속화(DB), 인증·권한
- React 화면 기능
- 장치 측 TCP 설정 수신 프로토콜(장치가 서버인 채널)
- 펌웨어 이미지 관리 기능

