# B540 FOTA (펌웨어 무선 업데이트)

- 작성: 2026-11-03 (KST), 갱신: 2026-11-20(FOTA URL→요청 경로 생성을 슬롯 B 디스어셈블로 확정, 2~5절), 2026-11-20(슬롯 접미사 선택 분기·다원 서버 서빙 확인, 3~5절, `docs/specs/dawon-fake-device-fota-path/`)
- 근거: B540 플래시 덤프의 정적 문자열, 기존 부트·슬롯 분석([부트 분석](b540-rea-boot.md), [초기 분석](b540-rea-initial.md)), 2026-11-20 슬롯 B(user2) Xtensa 디스어셈블(`xtensa-lx106-elf-objdump`, 분석 폴더는 `docs/specs/b540-fota-path/`), 2026-11-20 다원 FOTA 서버에서 접미사별 경로를 직접 받아 덤프와 바이트 비교. 실제 장치에서의 업데이트 수행은 관찰하지 않았다.
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
| `LFota_Url` | TCP 설정 키(관찰) | 이미지 URL 또는 경로(이름 기준 추정). 실제로는 host 필드로 간다(3절, 관찰) |
| `LFota_Addr` | TCP 설정 키(관찰) | 서버 주소(이름 기준 추정). 실제로는 GET 경로 필드로 간다(3절, 관찰) |
| `LFota_Port` | TCP 설정 키(관찰) | 서버 포트 |
| `LFota_Version` | TCP 설정 키(관찰) | 목표 버전 |
| `gSysInfo.FotaUrl`, `GetFotaPort`, `GetFotaAddr` | 디버그 로그(관찰) | 위 값을 파싱한 런타임 값 |
| `gSysInfo.NvPram.MqttModelName`, `versionEndcharPoint` | 디버그 로그(관찰) | 모델명·버전 비교 |

`L` 접두는 TCP 로컬 설정 경로(Local)로 추정한다. MQTT 경로에서는 `Set_UpgradeResourceData`가 같은 정보를 받는 것으로 추정한다([MQTT](b540-mqtt-topic.md)의 `w`/`chg`). 리소스 ID는 미확인.

## 3. 업데이트 흐름

2026-11-20 슬롯 B(user2, `1.01.34`) 디스어셈블로 URL 파싱·요청 생성을 확인했다(아래 함수 주소는 참 링크 주소 = 파일 오프셋 + `0x40200000`. 분석 환경·회차는 `docs/specs/b540-fota-path/`).

1. **트리거**(관찰): MQTT 쓰기(`Set_UpgradeResourceData` `0x402c924c`) 또는 TCP 설정(`LFota_*`). 받은 리소스 값(문자열)을 그대로 URL로 `UpdateUrlParser`에 넘긴다.
2. **URL 파싱**(관찰, `UpdateUrlParser` `0x402c8f80`): URL 하나를 스킴·호스트·포트·경로로 분해해 FOTA 컨텍스트(전역 `0x3fff1618`)의 버퍼에 넣는다 — host `+0x138`(`0x3fff1750`), path `+0x19d`(`0x3fff17b5`), port 문자열 `+0x202`(`0x3fff181a`, 없으면 기본 `80`). 구분자 `//`·`:`·`/`를 쓴다. path에는 **경로만** 들어가고 스킴·호스트·포트는 뺀다. (`AtoZParser` `0x402c8d4c`는 거의 같은 변형.) TCP 설정 파서(`0x4029d1a5`, `LFota_*` 키)는 키별 값을 같은 컨텍스트 버퍼에 strncpy해 같은 구조로 수렴한다 — `LFota_Url`→host 필드(`0x3fff1750`), `LFota_Addr`→path 필드(`0x3fff17b5`), `LFota_Port`→port(`0x3fff181a`), `LFota_Version`→version(`0x3fff1820`)(관찰: 키→버퍼 strncpy와 버퍼 역할을 GET 빌더 인자 `a4`=path·`a6`=host로 확인). 키 이름과 실제 역할이 반대다(`LFota_Url` 값이 Host로, `LFota_Addr` 값이 경로로 간다) — 2절의 추정 의미(§2)와 어긋나므로 2절 의미 추정은 이 관찰로 정정한다.
3. **버전 게이트**(관찰/일부 추정, `checkVersionChar` `0x402c8c70`): **HTTP `/version` 응답을 받는 게 아니라**, URL 안의 버전 문자열을 자기 펌웨어 버전 상수 `1.01.34`(DRAM `0x3ffe923e`)와 문자 단위로 비교한다(`.`은 건너뛰고 숫자만). URL 버전이 자기보다 **높을 때만** 진행하고, 같거나 낮으면 `0`을 돌려주어 FOTA를 중단한다(요청 자체를 보내지 않는다). URL에서 비교할 부분 문자열의 정확한 경계는 미확인(추정). 형식 문자열 `GET %s/version HTTP/1.1`(DRAM `0x3ffe9a37`)은 포인터 테이블 `table[1]`에 있지만 슬롯 B 코드에서 **참조되지 않는다**(관찰) — 별도 `/version` HTTP 요청은 슬롯 B에 없다.
4. **이미지 요청**(관찰): 연결 콜백(`0x4029e240`)이 port 문자열을 `atoi`로 포트에 넣고 요청 빌더(`0x4029e154`)를 부른다. 빌더는 포인터 테이블 `table[0]`(`0x3ffe882c` → `0x3ffe9a17`)의 형식 문자열 `GET %s%s HTTP/1.1\r\nHost: %s\r\n%s`에 네 인자를 넣는다: `%s1`=path(`0x3fff17b5`), `%s2`=**슬롯 접미사**(4절, 판정 함수 결과로 두 상수 가운데 고름), `%s3`=host(`0x3fff1750`), `%s4`=고정 헤더 블록(`0x3ffe990f`, `Connection: keep-alive`·`Cache-Control`·`User-Agent: Mozilla/5.0 …`·`Accept*`·끝 `\r\n\r\n`). 즉 실제 요청은

   ```
   GET <path><슬롯접미사> HTTP/1.1
   Host: <host>
   Connection: keep-alive
   … (고정 헤더)
   ```

   GET 라인에는 **경로만**(+ 슬롯 접미사) 들어가고 **호스트는 `Host` 헤더로 분리**된다. 받은 URL 전체(`http://host:port/path`)를 GET 라인에 넣지 않는다.
5. **수신·기록**: `Content-Length`로 `totallen` 확인, `upgrade file download start/finished`, `upgrade_get_sum_disconcb`(검증).
6. **결과**: `device_upgrade_success` → `system_upgrade_start`·재부팅, 실패 시 `device_upgrade_failed`.

### 다원 URL에 대한 실제 요청(관찰 기반 결론)

입력 URL `http://dwapi.dawonai.com:18446/DAWONDNS/B540_W/1.01.34` → 파싱: host `dwapi.dawonai.com`, port `18446`, path `/DAWONDNS/B540_W/1.01.34`.

- 버전 게이트: URL 버전 `1.01.34`가 자기 버전 `1.01.34`와 같으면 중단한다(요청 미송신). 더 높을 때만 진행한다.
- 진행 시 보내는 실제 요청(대상 TCP `dwapi.dawonai.com:18446`). 접미사는 4절대로 실행 슬롯의 반대편이다(추정 규칙). user2(슬롯 B)에서 돌면:

  ```
  GET /DAWONDNS/B540_W/1.01.34_1 HTTP/1.1
  Host: dwapi.dawonai.com
  Connection: keep-alive
  …
  ```

  user1(슬롯 A)에서 돌면 `…/1.01.34_2`다.
- 다원 서버 응답(관찰, 2026-11-20): `…/1.01.34_1`·`…/1.01.34_2`는 모두 200(`Content-Length` 392340, Apache)이고, 접미사 없는 `…/1.01.34`는 404다(Host 헤더에 포트가 있든 없든). `_2`로 받은 이미지는 덤프 슬롯 B(파일 `0x81000`부터 같은 길이)와 바이트 단위로 같고, `_1`로 받은 이미지는 머리가 `EA 04 00 01`인 1.01.34 user1 빌드다(덤프 슬롯 A는 1.01.30이라 다름). 같은 규칙(`/<회사>/<모델>/<버전>_<슬롯>`)이 다른 모델(M130_W 2.01.58)에서도 200이다.
- dawon-fake-device(#24)가 2026-11-20 실행에서 받은 404의 원인은 경로에 슬롯 접미사가 없던 것이다(관찰: 접미사 없는 경로만 404). .NET `HttpClient`는 GET 줄에 경로만 넣고 호스트는 `Host` 헤더로 보내므로 요청 줄 형식은 장치와 같았다.

## 4. 슬롯별 이미지 이름(관찰)

요청 경로의 슬롯 구분은 파일 이름(`user1.bin`/`user2.bin`)이 아니라 **path 끝에 붙는 슬롯 접미사 `_N`**이다.

- 상수(관찰): 각 슬롯 이미지에 `_2`와 `_1`이 둘 다 있다. 슬롯 B는 `_2`=DRAM `0x3ffe990c`(파일 `0xdf6b8`), `_1`=DRAM `0x3ffe9a08`(파일 `0xdf7b4`). 슬롯 A는 `_2`=파일 `0x5f560`, `_1`=파일 `0x5f65c`.
- 분기(관찰, 슬롯 B): GET 빌더 `0x4029e154`가 `0x4029e1c8`에서 판정 함수 `0x40296a34`를 부르고, 결과가 0이면 리터럴 `0x4029e140`(→ `0x3ffe990c`, `_2`)을, 1이면 `0x4029e1e1`에서 다시 부른 뒤 리터럴 `0x4029e148`(→ `0x3ffe9a08`, `_1`)을 `%s2`로 넘긴다. 두 경우 모두 같은 형식 문자열·path·host·고정 헤더로 `0x4029a4cc`(sprintf류)를 부른다.
- 판정 함수의 의미(추정): `0x40296a34`는 플래시 크기 맵 결과로 갈라 부트 설정 워드(+`0x214`)의 비트를 보고 0/1을 돌려준다. SDK `system_upgrade_userbin_check`(0=user1 실행 중, 1=user2 실행 중)와 같은 형태다(함수 이름은 덤프에 없음). 그렇다면 장치는 **실행 중인 슬롯의 반대편 이미지**를 받는다: user1에서 돌면 `_2`, user2에서 돌면 `_1`. 반대편 슬롯에 새 이미지를 쓰는 SDK 2슬롯 FOTA와 맞는다.
- 슬롯 A 코드(미확인): 슬롯 A에도 같은 두 상수가 같은 간격으로 있어 같은 분기일 가능성이 높지만, 슬롯 A의 GET 빌더는 디스어셈블하지 않았다.
- 두 슬롯 이미지 차이(관찰): 다원 서버의 `_1`·`_2` 이미지는 길이가 같고, 다른 바이트는 대부분 코드 주소가 `0x80000`만큼 다른 것(`0x4025xxxx`↔`0x402dxxxx`)과 머리 4번째 바이트(`01`/`02`)다. 같은 펌웨어를 실행 위치(플래시 `0x01000`/`0x81000`)에 맞춰 따로 링크한 빌드다.

## 5. 미확인 항목

- 버전 게이트에서 URL 중 버전으로 비교하는 부분 문자열의 정확한 경계(마지막 세그먼트인지 경로 일부인지)
- MQTT `/5/0/N`의 N과 내부 리소스 필드 id의 매핑. 리소스 값을 URL로 쓰는 흐름은 확인했고(`Set_UpgradeResourceData` → `UpdateUrlParser`), LWM2M 객체 5 표준상 `/5/0/1`=Package URI와 일치해 `/5/0/1`을 URL로 본다(추정). 정확한 번호는 미확인.
- 이미지 무결성·서명 검증(EA 컨테이너 CRC 외 추가 검증 여부)
- HTTPS 다운로드 실제 지원 여부(SDK `upgrade_lib`는 기본 HTTP)
- 슬롯 접미사 판정 함수 `0x40296a34`의 0/1이 실행 슬롯(user1/user2)이라는 것(4절, 추정)과 슬롯 A의 GET 빌더 분기
