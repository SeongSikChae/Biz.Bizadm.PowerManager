# B540 FOTA (펌웨어 무선 업데이트)

- 작성: 2026-11-03 (KST)
- 근거: B540 플래시 덤프의 정적 문자열, 기존 부트·슬롯 분석([부트 분석](b540-rea-boot.md), [초기 분석](b540-rea-initial.md)). 실제 업데이트 수행은 관찰하지 않았다.
- 표기: **관찰**, **추정**, **미확인**
- 관련 문서: [MQTT 토픽](b540-mqtt-topic.md), [REST API](b540-rest-api.md), [TCP 설정 수신](b540-tcp-config.md)

## 1. 플래시 레이아웃

| 영역 | 파일 오프셋 | 내용 |
|---|---|---|
| 2nd 부트로더 | `0x00000` | E9 이미지, `2nd boot version : 1.6` |
| 앱 슬롯 A (user1) | `0x01000` | EA 컨테이너 + 내부 E9, MQTT 등록 버전 `1.01.30` |
| 앱 슬롯 B (user2) | `0x81000` | EA 컨테이너 + 내부 E9, MQTT 등록 버전 `1.01.34` |
| 제품 설정 | `0x7c000`, `0x7d000` | 동일 내용 2벌, 설정 버전 `1.01.34` |
| 부트 선택 레코드 | `0xfc000`~`0xfe000` 부근 | fd/fe 저장본, 기존 분석상 user2 최초 시도 |

추정: 실행 중이 아닌 슬롯에 새 이미지를 받고 부트 레코드를 전환하는 ESP8266 NONOS SDK 2슬롯 FOTA다. 설정 버전 `1.01.34`가 슬롯 B 버전과 같아 슬롯 B가 마지막 업데이트 결과일 가능성이 높다. 실제 활성 슬롯은 미확인.

## 2. 업데이트 설정 값

| 키/필드 | 근거 | 추정 의미 |
|---|---|---|
| `LFota_Url` | TCP 설정 키(관찰) | 이미지 URL 또는 경로 |
| `LFota_Addr` | TCP 설정 키(관찰) | 서버 주소 |
| `LFota_Port` | TCP 설정 키(관찰) | 서버 포트 |
| `LFota_Version` | TCP 설정 키(관찰) | 목표 버전 |
| `gSysInfo.FotaUrl`, `GetFotaPort`, `GetFotaAddr` | 디버그 로그(관찰) | 위 값을 파싱한 런타임 값 |
| `gSysInfo.NvPram.MqttModelName`, `versionEndcharPoint` | 디버그 로그(관찰) | 모델명·버전 비교 |

`L` 접두는 TCP 로컬 설정 경로(Local)로 추정한다. MQTT 경로에서는 `Set_UpgradeResourceData`가 같은 정보를 받는 것으로 추정한다([MQTT](b540-mqtt-topic.md)의 `w`/`chg`). 리소스 ID는 미확인.

## 3. 업데이트 흐름(추정)

1. **트리거**: MQTT 쓰기(`Set_UpgradeResourceData`) 또는 TCP 설정(`LFota_*`)
2. **URL 파싱**: `UpdateUrlParser`, `CheckHttps`/`CheckHttp`(스킴 `https`/`http` 판별), 기본 포트 후보 `80`(`0xe085b`)
3. **버전 확인**: `CheckVersion`, `checkVersionChar`, `AtoZParser`. 요청 `GET %s/version HTTP/1.1`
4. **다운로드 시작**: `Fota_StartServerConnection`, `OTA Start`. 이미 진행 중이면 `upgrade is already started`
5. **이미지 요청**: `GET %s%s HTTP/1.1\r\nHost: %s\r\n%s` + 고정 헤더(`Connection: keep-alive`, `User-Agent: Mozilla/5.0 ... Chrome/30.0.1599.101`). SDK `upgrade_lib.c`는 `HEAD` 미지원 서버에 `GET`으로 재시도한다(`server do not support HEAD method now send GET message`).
6. **수신·기록**: `Content-Length`로 `totallen` 확인, `upgrade file download start/finished`, `upgrade_get_sum_disconcb`(검증)
7. **결과**: `device_upgrade_success` → `system_upgrade_start`·재부팅, 실패 시 `device_upgrade_failed`

추정: 버전 응답과 장치 버전을 비교해 다를 때만 이미지를 받는다. 비교 규칙은 미확인.

## 4. 슬롯별 이미지 이름(미확인)

ESP8266 SDK 관례는 요청 경로에 실행 중 슬롯 반대편 이미지(`user1.bin`/`user2.bin`)를 고르는 것이지만, 덤프에서 해당 파일명 문자열은 찾지 못했다. 서버가 슬롯별 이미지를 어떻게 구분하는지는 미확인이다.

## 5. 미확인 항목

- `/version` 응답 형식과 비교 규칙
- 이미지 무결성·서명 검증(EA 컨테이너 CRC 외 추가 검증 여부)
- HTTPS 다운로드 실제 지원 여부(SDK `upgrade_lib`는 기본 HTTP)
- MQTT FOTA 리소스 경로와 결과 보고 메시지
