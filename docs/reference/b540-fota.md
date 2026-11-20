# B540 FOTA (펌웨어 무선 업데이트)

- 작성: 2026-11-03 (KST), 갱신: 2026-11-20(FOTA URL→요청 경로 생성을 슬롯 B 디스어셈블로 확정, 2~5절)
- 근거: B540 플래시 덤프의 정적 문자열, 기존 부트·슬롯 분석([부트 분석](b540-rea-boot.md), [초기 분석](b540-rea-initial.md)), 2026-11-20 슬롯 B(user2) Xtensa 디스어셈블(`xtensa-lx106-elf-objdump`, 분석 폴더는 `docs/specs/b540-fota-path/`). 실제 장치에서의 업데이트 수행·서버측 서빙은 관찰하지 않았다.
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
4. **이미지 요청**(관찰): 연결 콜백(`0x4029e240`)이 port 문자열을 `atoi`로 포트에 넣고 요청 빌더(`0x4029e154`)를 부른다. 빌더는 포인터 테이블 `table[0]`(`0x3ffe882c` → `0x3ffe9a17`)의 형식 문자열 `GET %s%s HTTP/1.1\r\nHost: %s\r\n%s`에 네 인자를 넣는다: `%s1`=path(`0x3fff17b5`), `%s2`=**슬롯 접미사**(상수 `0x3ffe990c`), `%s3`=host(`0x3fff1750`), `%s4`=고정 헤더 블록(`0x3ffe990f`, `Connection: keep-alive`·`Cache-Control`·`User-Agent: Mozilla/5.0 …`·`Accept*`·끝 `\r\n\r\n`). 즉 실제 요청은

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
- 진행 시 슬롯 B가 보내는 실제 요청(대상 TCP `dwapi.dawonai.com:18446`):

  ```
  GET /DAWONDNS/B540_W/1.01.34_2 HTTP/1.1
  Host: dwapi.dawonai.com
  Connection: keep-alive
  …
  ```

- dawon-fake-device(#24)가 2026-11-20 실행에서 보낸 `GET http://dwapi.dawonai.com:18446/DAWONDNS/B540_W/1.01.34`(전체 URL을 GET 라인에)·`…/version`은 실제 장치 동작과 다르다. 그래서 404였다(추정: 서버에 `<path>_2` 경로가 있는지는 서버측이라 범위 밖). 가짜 장비는 경로만·슬롯 접미사 `_2`·Host 헤더 분리로 맞춰야 한다.

## 4. 슬롯별 이미지 이름(관찰)

요청 경로의 슬롯 구분은 파일 이름(`user1.bin`/`user2.bin`)이 아니라 **path 끝에 붙는 슬롯 접미사 `_N`**이다. 슬롯 A(user1)는 `_1`(파일 `0x5f65c`), 슬롯 B(user2)는 `_2`(파일 `0xdf6b8`)를 붙인다(관찰). 두 슬롯의 FOTA 형식 문자열 블록은 바이트 동일하고 접미사만 다르다. 따라서 이미지 경로는 `GET <path>_1`(슬롯 A) 또는 `GET <path>_2`(슬롯 B)다. 서버가 그 경로로 어떤 이미지를 서빙하는지는 서버측이라 미확인.

## 5. 미확인 항목

- 버전 게이트에서 URL 중 버전으로 비교하는 부분 문자열의 정확한 경계(마지막 세그먼트인지 경로 일부인지)
- MQTT `/5/0/N`의 N과 내부 리소스 필드 id의 매핑. 리소스 값을 URL로 쓰는 흐름은 확인했고(`Set_UpgradeResourceData` → `UpdateUrlParser`), LWM2M 객체 5 표준상 `/5/0/1`=Package URI와 일치해 `/5/0/1`을 URL로 본다(추정). 정확한 번호는 미확인.
- 이미지 무결성·서명 검증(EA 컨테이너 CRC 외 추가 검증 여부)
- HTTPS 다운로드 실제 지원 여부(SDK `upgrade_lib`는 기본 HTTP)
- 서버가 `<path>_N` 경로로 서빙하는 이미지(서버측, 범위 밖)
