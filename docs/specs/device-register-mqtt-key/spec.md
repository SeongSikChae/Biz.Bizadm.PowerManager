# 장치 등록 응답과 mqtt_key 발급·저장

기능: device-register(새 기능) / mqtt-broker(기준 spec docs/features/mqtt-broker.md)
이슈: GitHub #16

## 무엇을, 왜

장치는 공유기에 접속한 뒤 `POST /api/v1/devices/register/create`로 등록을 요청하고, 응답으로 MQTT 접속 키(`mqtt_key`)를 받는다. 지금 서버의 이 endpoint는 요청을 로그로 남기고 501을 반환하는 껍데기(`DevicesController.Register`)라 장치가 키를 받지 못한다.

B540 펌웨어(`docs/.tmp/b540-dump1.bin`) 분석으로 장치가 응답을 처리하는 방식을 확인했다.

- 장치는 TCP 설정 메시지와 등록 응답 JSON을 같은 키 해석 함수로 키 순서대로 처리한다.
- `verify`는 값이 16자 이상일 때만 기억한다.
- `mqtt_key`는 16자 이상일 때만 처리하며(저장 버퍼가 33바이트라 최대 32자로 추정), 앞에서 `verify`를 받지 못했거나 `verify`가 아래 계산값과 44자까지 같지 않으면 키를 버리고 등록 상태를 초기화한다. 따라서 응답에서 `verify`가 `mqtt_key`보다 앞에 와야 한다.
- 계산값: 치환표 `A = "XGAqW.Ibj3xdCRFzf60oMKNDYPaTQHLUhls4v1mei5S9w-r8n=kJtV/O+7ZEu2cgypB"`(67자), `s = server_addr + mac`(mac은 소문자 16진수 12자, server_addr는 장치에 설정된 MQTT 서버 주소), `sumS`·`sumK`는 `s`·`mqtt_key`의 바이트 합, `pos(c)`는 `A` 안에서 `c`의 위치일 때 `i = 0..43`마다 `verify[i] = A[(pos(s[i mod |s|])·sumK + pos(key[i mod |key|])·sumS) mod 67]`.
- 이 계산식은 2026-11-19 실장비 B540(mac `365f45501f96`)의 등록 본문으로 실제 다원 서버(`dwapi.dawonai.com:18443`)에 요청해 받은 `verify`·`mqtt_key`와 `server_addr` `dwmqtt.dawonai.com`으로 대조해 일치함을 확인했다. 실제 다원 응답의 키는 `verify`(44자)·`mqtt_key`(16자) 두 개뿐이었다.

이번 작업은 서버가 `mqtt_key`를 직접 만들고 `verify`를 계산해 응답하며, 발급한 키를 장치(MAC)별로 SQLite(EF Core)에 저장한다. 장치에 설정되는 `server_addr`는 설정 도구 기본값 `dwmqtt.dawonai.com`이며 장치 DNS로 우리 서버를 가리키게 하므로, 서버가 계산에 쓰는 `server_addr`를 설정으로 두고 기본값을 같게 한다.

장치는 등록 뒤 `GET /api/v1/device/connect/{a}-{b}-{c}`로 접속 정보를 조회하고 MQTT 접속을 시작하는 것으로 추정된다(`docs/.tmp/b540-rest-api.md` 3·4절). 지금 이 endpoint도 501 껍데기라 장치가 MQTT까지 가지 못할 수 있으므로, 과거 구현(`OldPowerManager` `ApiController.Connect`: 등록된 장치에 200 `{"delay":"0"}`)과 같은 최소 응답을 이번에 함께 구현한다.

MQTT 접속 인증 강제는 이번에 하지 않는다. 과거 구현(`OldPowerManager` `MqttServerServiceImpl`)은 ClientId `DAWONDNS-{mac}`, 사용자명 `DAWONDNS-{모델}-{mac}`, 비밀번호 `mqtt_key`로 인증했지만 현재 펌웨어에서 확인하지 않았으므로, 이번에는 CONNECT 자격 증명을 관찰하는 로그를 남기고 접속 거부는 확인 뒤 따로 한다.

범위 밖:

- MQTT 접속 거부(인증 강제)
- 등록 응답의 `status`·`reason` 필드(펌웨어상 `status`가 `REGISTERED`이면 실패 쪽 처리로 보여 보내지 않는다)
- 등록 장치 조회·삭제 등 관리 화면·API

## 완료 조건

- C1. 올바른 요청에 `POST /api/v1/devices/register/create`가 200과 `application/json` 본문 `{"verify":"<44자>","mqtt_key":"<키>"}`를 반환한다. 본문의 키는 이 두 개뿐이고 `verify`가 `mqtt_key`보다 앞에 있다.
- C2. 새로 발급하는 `mqtt_key`는 암호학적 난수로 만든 16자이며 문자는 `[A-Za-z0-9]`에서만 고른다.
- C3. `verify`는 위 계산식에 설정의 `server_addr`(기본값 `dwmqtt.dawonai.com`), 요청 mac(소문자), 응답하는 `mqtt_key`를 넣은 값과 같다. 이를 두 가지로 확인한다.
  - 자동 테스트: 다원 실제 응답과 일치함을 확인한 참조 계산(작업 문서 사전 분석 절의 계산식을 DevServer에서 Python으로 실행한 것)으로 가상 입력(실제가 아닌 mac·키, `server_addr` 둘 이상)에 대해 미리 만든 시험 값과 서버 구현 결과가 같다. 시험 값은 서버 구현으로 만들지 않는다.
  - 실측 대조(수동 확인, C9의 자동 테스트 대상이 아님): 서버 구현에 DevServer `/mnt/analysis/bizadm/work/b540/dawon-resp.json`의 실제 다원 응답(mac `365f45501f96`, `server_addr` `dwmqtt.dawonai.com`, 받은 `mqtt_key`)을 넣으면 받은 `verify`와 같다. 실제 키·verify 값은 저장소에 넣지 않으며, 대조한 명령과 일치 여부를 작업 문서에 기록한다.
  - `server_addr` 설정값은 치환표 `A`의 문자(`[A-Za-z0-9]`, `.`, `-`, `=`, `/`, `+`)로만 이뤄져야 하며, 비어 있거나 다른 문자가 있으면 서버가 시작할 때 설정 오류로 끝난다. 자동 테스트의 가상 입력도 이 범위에서 고른다.
- C4. 요청의 `device.mac`이 12자리 16진수가 아니면(없거나 빈 값 포함) 400 ProblemDetails를 반환하고 저장하지 않는다. 대소문자는 구분하지 않고 소문자로 정규화한다. 이 밖의 요청(본문 JSON이 올바르고 mac이 맞는 요청)은 올바른 요청이다. `account.user_id`·`device.model_id`·`device.company`는 없거나 빈 값이어도 거부하지 않는다.
- C5. 발급한 키를 mac별로 SQLite DB에 저장한다(mac, user_id, model_id, company, mqtt_key, 처음 등록 시각, 마지막 요청 시각). 처음 등록 때 없거나 빈 user_id·model_id·company는 빈 값으로 저장한다. 서버를 다시 시작해도 저장 값이 남는다.
- C6. 이미 저장된 mac으로 다시 요청하면 새 키를 만들지 않고 저장된 `mqtt_key`로 응답하며(`verify`는 현재 설정으로 다시 계산), 마지막 요청 시각을 갱신한다. user_id·model_id·company는 요청에 비어 있지 않은 값이 있을 때만 그 값으로 바꾸고, 없거나 빈 값이면 저장된 값을 그대로 둔다.
- C7. 서버 로그에 `mqtt_key`와 `verify` 값이 남지 않는다.
- C8. MQTT 클라이언트가 접속할 때마다 Information 로그 항목 하나에 ClientId, 사용자명, 비밀번호 유무, 비밀번호가 등록된 `mqtt_key`와 같은지를 남긴다. 판정은 일치·불일치·판정 불가 셋이다. ClientId가 `DAWONDNS-{12자리 16진수}` 형식이고 그 mac(대소문자 구분 없이 소문자로 바꿔 조회)이 등록돼 있으면 비밀번호가 키와 같을 때 일치, 다르거나 비밀번호가 없으면 불일치다. 형식이 아니거나 등록되지 않은 mac이면 판정 불가다. 비밀번호 값은 남기지 않는다. 접속은 지금처럼 모두 받는다.
- C9. C1~C8·C11을 확인하는 자동 테스트가 있고 통과한다(C3의 실측 대조는 제외).
- C10. 실장비 B540(`DWD-LS120_501f96`)을 배포한 서버로 등록시키면 서버가 등록 요청에 200으로 응답하고 장치가 MQTT에 접속한다. 그 접속의 C8 로그(ClientId, 사용자명, 판정 결과)를 작업 문서에 기록한다. 판정 결과는 이 조건의 통과 여부와 관계없으며, 접속 인증 강제(범위 밖)의 근거로 쓴다.
- C11. `GET /api/v1/device/connect/{deviceKey}`는 `deviceKey`가 `DAWONDNS-{모델}-{mac}` 형식(모델은 비어 있지 않으며 `-`를 포함할 수 있음, 예: `M130-W`. mac은 마지막 `-` 뒤의 12자리 16진수)이고 그 mac(대소문자 구분 없음)이 등록돼 있으면 200과 `application/json` 본문 `{"delay":"0"}`을 반환하고, 그렇지 않으면 404 ProblemDetails를 반환한다(과거 구현 `ApiController.Connect`의 장치 키 형식 `DAWONDNS-{모델}-{mac}` 기준).
