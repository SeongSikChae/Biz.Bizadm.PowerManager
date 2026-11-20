# B540 FOTA 요청 경로 펌웨어 분석 계획

## 요약

이 Windows 호스트에는 Xtensa 디스어셈블러가 없다. 분석은 사용자가 지정한 원격 Linux(`plink bizadm@20.214.104.36`, Rocky Linux)에서 한다. 그 서버의 128GB 디스크(`siem-lvsiem`)를 `/mnt/siem`에 마운트하면 `/mnt/siem/b540-analysis`에 이전 B540 분석 환경이 그대로 있다.

- ESP8266 크로스 도구 `xtensa-lx106-elf-objdump`
- 슬롯 B irom을 ELF로 감싼 `appB-irom.elf`(VMA `0x40201000`)와 전체 디스어셈블 `appB-irom.dis`, IRAM `appB-iram.{elf,dis}`
- 덤프 `b540-dump1.bin`
- 보조 스크립트 `xref.py`(형식 문자열·리터럴을 `l32r`로 싣는 지점 찾기), `callers.py`(함수 시작·호출자), `dis1.sh`(ELF 래핑·디스어셈블 명령), `annot.py`

이 디스어셈블을 근거로 다원이 보낸 FOTA URL을 받아 HTTP 요청을 만드는 코드(`UpdateUrlParser`·`CheckVersion`·`Set_UpgradeResourceData` 로그 이름 부근)를 추적해, `GET %s/version`·`GET %s%s`의 각 `%s`에 들어가는 값을 밝힌다. 결과만 저장소의 `docs/reference/b540-fota.md`에 반영한다. 분석 산출물·덤프는 원격에만 둔다.

정적 분석만 한다. 장치를 실행하거나 다원에 요청을 보내지 않는다. 결과 경로를 가짜 장비로 시험하는 것은 이 작업 뒤 사용자 확인을 받고 따로 한다.

## 바꿀 파일·구조

- `docs/reference/b540-fota.md`: 분석 결과를 반영한다(작업 대상 파일, 유일). 3절 업데이트 흐름의 URL 파싱·요청 경로를 관찰로 바꾸고, 4절 슬롯 이미지 이름·5절 미확인 항목을 분석 결과로 줄이거나 갱신한다. 서버 호스트·포트는 다른 참조 문서 관례대로 그대로 적고, 실장비 mac·계정·키와 덤프 경로는 넣지 않는다.

원격 서버의 `/mnt/siem/b540-analysis` 산출물(새 디스어셈블 텍스트 등)과 덤프는 저장소에 넣지 않는다. 작업 문서·변경 문서는 작업 대상 파일이 아니다.

## 결정

- **분석은 원격 Linux에서 한다.** 이 Windows 호스트에 Xtensa 디스어셈블러가 없고 REA `raw-binary`는 Xtensa를 지원하지 않는다(`6502·x86·x86_64·arm·arm64·m68k`만). 원격 `/mnt/siem/b540-analysis`에 이전 분석 환경과 도구가 있어 그대로 쓴다. 사용자가 `plink`·디스크 마운트를 지정했다.
- **슬롯 B(user2, 1.01.34)를 분석한다.** 다원이 보낸 URL이 `.../B540_W/1.01.34`로 끝나 슬롯 B 버전과 같고, 기존 디스어셈블(`appB-irom.dis`)이 슬롯 B다. 두 슬롯의 FOTA 코드가 같은지는 형식 문자열·관련 함수 바이트를 슬롯 A(파일 `0x5f66b` 부근)와 비교해 확인하고, 다르면 그 사실을 적는다.
- **기존 `appB-irom.dis`를 재사용하고, 필요한 범위만 `xtensa-lx106-elf-objdump`로 다시 본다.** 전체 재디스어셈블을 반복하지 않는다. `l32r` 리터럴 추적은 `xref.py`나 `.dis`에서 주소 문자열을 직접 찾는 방식으로 한다.
- **깊은 인자 추적은 `objdump` 수동 판독을 먼저 하고, 막히면 Ghidra를 설치해 쓴다**(D002). 베이스+오프셋 간접 참조·`sprintf` 데이터플로가 손으로 안 풀리면 Ghidra 12.2 headless + 서드파티 Xtensa 모듈(`Xtensa:LE:32`)을 원격에 설치해 디컴파일로 본다. Ghidra는 지금 원격에 없다(설치 필요, Java 21은 있음).

## 인터페이스·데이터

주소 매핑은 슬롯 B 이미지의 세그먼트 헤더(바깥 EA `0x81000`, 안쪽 E9 `0xd61a0`)에서 확정했다(D001).

- **irom0(코드):** 파일 `0x81010`~`0xd61a0`. 기존 `appB-irom.dis`는 `0x40201000`에 감싸므로 `.dis` 주소 = 파일 + `0x40180000`. 펌웨어 참 링크 주소 = 파일 + `0x40200000`(= `.dis` 주소 + `0x80000`), `b540-mqtt-topic.md`의 `0x402cxxxx` 표기와 같은 기준.
- **IRAM(코드):** vaddr `0x40100000`, 파일 `0xd61b0`, 길이 `0x7bf0`(`appB-iram.dis`).
- **DRAM(데이터):** seg2 vaddr `0x3ffe8000`(파일 `0xddda8`), seg3 vaddr `0x3ffe8db0`(파일 `0xdeb5c`). `xref.py`의 `DRAM_SEGS`와 같다.
- **대상 문자열(DRAM seg3):** `GET %s%s HTTP/1.1`=`0x3ffe9a17`, `GET %s/version HTTP/1.1`=`0x3ffe9a37`, `Host: `=`0x3ffe9a2a`. 설정 키 `LFota_Url`=`0x3ffe943a`·`LFota_Addr`=`0x3ffe9444`·`LFota_Version`=`0x3ffe945a`.
- **확인한 코드 자리:** 설정 키 `LFota_Url/Addr/Version`를 `l32r`로 싣는 곳이 `.dis` `0x4021d1a5`~`0x4021d31f`(참값 `0x4029d1a5`~, TCP 설정 파서)다. 한편 `GET %s%s`·`GET %s/version`의 주소(`0x3ffe9a17`·`0x3ffe9a37`)는 두 `.bin`의 리터럴 풀에 **바이트로도 없다**(직접 `l32r` 참조가 없다). 요청 생성 코드는 이미지 안(irom/IRAM = `.dis` 범위)에 있되, 형식 문자열을 **베이스+오프셋으로 간접 참조**한다(예: 세그 기준 포인터를 `l32r`한 뒤 `addi`로 가산). precompiled SDK라도 링크되면 `.dis` 범위에 들어가므로 "이미지 밖"은 아니다.
- **추적 방법:**
  1. `xref.py <dis> s:<문자열>` 또는 리터럴 풀 값으로, 문자열·버퍼를 `l32r`로 싣는 코드 자리를 찾는다(objdump는 `l32r aX, <풀주소>`로 풀 주소를 보이므로 `xref.py`가 풀→값을 해석한다).
  2. 설정 파서 `0x4021d1a5` 부근부터 본다: `LFota_Url/Addr/Port/Version`를 어떤 버퍼(`gSysInfo.FotaUrl`·`GetFotaAddr`·`GetFotaPort`)에 넣는지. MQTT `/5/0/*`(`Set_UpgradeResourceData`)가 같은 버퍼를 채우는지 `callers.py`로 확인한다.
  3. 진입점 확인(먼저): `upgrade_start`·`upgrade_connect`(로그 문자열 파일 `0xd4cxx`) 주소가 리터럴 풀에 있는지(직접 참조) 확인한다. 있으면 그 xref로 요청 생성 함수에 들어간다. 없으면(간접 참조) 설정 파서(`0x4021d1a5` 부근)·`Set_UpgradeResourceData`가 채운 FOTA 버퍼를 쓰는 곳을 따라 들어간다. 필요하면 그 범위를 `xtensa-lx106-elf-objdump`로 다시 디스어셈블한다.
  4. 요청 생성 함수에서 `GET %s%s`·`GET %s/version`을 쓰는 `sprintf`류 호출의 인자 레지스터를 읽는다. `%s`가 상수 문자열이면 그 값을, 런타임 버퍼면 그 버퍼를 채우는 경로(설정 파서·`UpdateUrlParser`)를 역추적해 다원 URL이 어떤 경로 문자열이 되는지 정한다.
     - 베이스+오프셋 간접 참조나 `sprintf` 인자 데이터플로가 `objdump` 수동 판독으로 풀리지 않으면(베이스 레지스터가 다른 함수·테이블에서 설정되는 등), 그때 원격에 Ghidra 12.2 headless와 서드파티 Xtensa 프로세서 모듈(`Xtensa:LE:32`)을 설치해 디컴파일 의사코드로 데이터플로를 본다(자매 문서 `b540-mqtt-topic.md`가 쓴 경로, D002). 설치 전에 사용자에게 알린다.
  5. `/version` 비교는 `CheckVersion`·`checkVersionChar`·`AtoZParser`, MQTT 수신은 `Set_UpgradeResourceData`의 리소스 번호를 본다.
- **산출물:** 새로 본 디스어셈블 범위는 원격 `/mnt/siem/b540-analysis` 아래 둔다. 저장소의 `b540-fota.md`에는 결론과 근거만 적되, 주소는 펌웨어 참 링크 주소(파일 + `0x40200000`)와 DRAM vaddr로 적어 다른 참조 문서와 기준을 맞춘다. 기존 `.dis`가 `0x80000` 낮은 기준으로 감싼 점은 분석 중에만 고려한다.

## 오류·위험

- **정적 분석 한계:** `%s` 인자가 런타임에 채워지는 버퍼면 상수로 확정할 수 없다. 그때는 버퍼를 채우는 경로까지 적고, 끝내 못 정하면 "미확인"으로 남긴다(spec C1~C5).
- **슬롯 A/B 차이:** 슬롯 B만 디스어셈블돼 있다. FOTA 코드가 두 슬롯에서 같은지 확인하고, 실제 활성 슬롯은 덤프로 단정하지 않는다(기존 분석의 한계와 같다).
- **원격 환경 의존:** 분석은 마운트한 `/mnt/siem`에 의존한다. 마운트가 풀리면 다시 마운트한다. 원격에서 쓰기는 `b540-analysis` 아래 새 텍스트 출력뿐이고, 덤프·기존 산출물을 지우거나 덮지 않는다.
- **objdump 경계 오류:** Xtensa 가변 길이 명령이라 리터럴 풀·패딩에서 디코딩 경계가 틀어질 수 있다. 실제 `call0`/`l32r` 타깃부터 다시 보고, 경계 밖 표시를 실제 명령으로 단정하지 않는다(기존 분석과 같은 원칙).

## 검증 전략

| 완료 조건 | 확인 방법 |
|---|---|
| C1 | `UpdateUrlParser` 함수에서 받은 URL 문자열을 어떤 조각으로 나누는지 디스어셈블로 보인다. 못 짚은 조각은 미확인으로 남긴다. |
| C2 | `upgrade_start`·`upgrade_connect` 로그 문자열로 요청 생성 함수를 찾아 그 안의 `sprintf`류와 `GET %s/version` 참조(베이스+오프셋)를 짚고, `%s` 인자를 추적해 URL의 어느 부분인지 보인다. 가짜 장비의 `{URL}/version`과 같은지/다른지 판정한다. 끝내 못 정하면 미확인. |
| C3 | 같은 요청 생성 함수에서 `GET %s%s`의 두 인자를 추적한다. 두 번째 조각에 파일 이름이 붙는지, 붙으면 어떻게 정해지는지 보이거나 미확인으로 남긴다. 진입은 형식 문자열 `l32r` 검색이 아니라 upgrade 함수에서 시작한다(형식 문자열은 직접 리터럴이 아니라 간접 참조). |
| C4 | `CheckVersion`·`checkVersionChar`·`AtoZParser`에서 `/version` 응답 비교 규칙을 보이거나 미확인으로 남긴다. |
| C5 | `Set_UpgradeResourceData`가 받는 `/5/0/*` 리소스 번호와 `/5/0/1` 값을 URL로 쓰는지 보이거나 미확인으로 남긴다. |
| C6 | 위 결과로 다원 URL `http://dwapi.dawonai.com:18446/DAWONDNS/B540_W/1.01.34`에 대한 실제 요청 경로 예를 적는다. 미확인이 남으면 무엇이 비어 경로를 확정 못 하는지 적는다. |
| C7 | `b540-fota.md`의 반영 내용이 위 분석 결과와 맞고, 관찰·추정·미확인 표기와 근거(주소·오프셋)가 붙어 있는지 본다. 서버 호스트·포트만 그대로이고 실장비 값·덤프 경로가 없는지 본다. |

## 원칙 점검

- 적용 규칙: `workflow/spec-driven.md`, `workflow/repository-content.md`, `python/portable-python.md`(원격에서 `xref.py` 등 python 실행 전에 읽고 따른다. 주 추적은 `.dis` 문자열 검색으로 하고 python은 보조로만 쓴다), `git/github-access.md`(원격 서버 접근은 사용자가 지정한 `plink`), 전역 규칙(응답 언어)
- 충돌: 없음.
  - `repository-content`: 덤프·실장비 값·원격 산출물은 저장소에 넣지 않는다. 서버 호스트·포트는 다른 참조 문서(`b540-mqtt-topic.md`의 `dwmqtt.dawonai.com` 등) 관례대로 그대로 적는다.
  - `github-issue`: 이 작업은 기존 참조 문서만 바꾸고 제품 코드·기능을 바꾸지 않아 새 이슈를 열지 않는다. 계기는 #24다.
  - 원격 서버의 디스크 마운트·python 실행은 사용자가 지정한 환경에서의 분석이며, 이 저장소나 로컬 시스템 설정을 바꾸지 않는다.
  - Ghidra+Xtensa 설치(D002, 필요 시에만): 사용자 서버에서의 분석 도구 설치이며 사용자 소유 장치의 역공학용이다. 설치 전에 사용자에게 알린다. 이 저장소·로컬 시스템 설정은 바꾸지 않는다.
