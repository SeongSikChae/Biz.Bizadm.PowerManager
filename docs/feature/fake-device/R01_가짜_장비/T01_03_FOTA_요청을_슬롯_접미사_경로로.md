---
출처: R01-R1, R01-R9, R01-R14, R01-R15, R01-R16
상태: 끝남
---

# T01_03 FOTA 요청을 슬롯 접미사 경로로

## 읽을 것

- 요구사항: `R01-R1`(실행 슬롯), `R01-R9`(본문 항목 삭제), `R01-R14`~`R01-R16`
- 수용 기준: `R01-A1`, `R01-A9`, `R01-A14`~`R01-A17`
- 결정: `D01_32`~`D01_38`(옛 `docs/specs/dawon-fake-device-fota-path/decisions.md`(지움. `15dc357`에 있음)에서 옮김)
- 계약: `C01-I2`, `C01-I3`, `C01-I4`
- 근거: `T01_02`의 `docs/reference/b540-fota.md`
- 옛 작업 기록: `docs/specs/dawon-fake-device-fota-path/`(지움. `15dc357`에 있음), 저장소 밖 작업 문서 `2026-11-20-dawon-fake-device-fota-path.md`(기준점 `34dd1de`, 실제 다원 실행 기록). 일감 #24, 커밋 `53a34ad`

## 고칠 파일

- `src/Biz.Bizadm.PowerManager.FakeDevice.App/AppConfiguration.cs`, `FakeDeviceOptions.cs`: `RunningSlot`(1·2, 기본 2)
- `src/Biz.Bizadm.PowerManager.FakeDevice.App/Fota/FotaDownloader.cs`, `FakeDeviceRunner.cs`, `Console/ConsoleCommand.cs`: 이미지 요청 하나(`{URL}{접미사}`), `fota`·`get`
- `src/Biz.Bizadm.PowerManager.FakeDevice.App/Recording/FakeDeviceRecorder.cs`: 응답 항목에서 본문 삭제
- 테스트: `AppConfigurationTests`, `FotaDownloaderTests`, `RunnerFotaTests`, `ConsoleCommandTests`
- `docs/reference/b540-fota.md`, `docs/features/dawon-fake-device.md`(지움. `15dc357`에 있음)

## 할 일

### 단계 1: 설정

### 단계 2: 받기 동작
- `/version`·접미사 없는 요청을 없애고 실행 슬롯의 반대편 접미사를 붙인 요청 하나만 보낸다. `get`은 가공하지 않는다.

### 단계 3: 테스트

### 단계 4: 문서

### 단계 5: 실제 실행
확인: 2026-11-20 21:56 실제 다원 실행(펌웨어 `1.01.30`, 실행 슬롯 2): 콘솔 `up notify/json` 뒤 `/5/0/1` URL 수신, 자동 받기 `GET …/1.01.34_1` → 200, 392340바이트, SHA-256이 사전 확인한 `_1` 기준값과 같음, `/version` 요청 없음, 종료 코드 0. 테스트 151건 통과.

## 위험

- URL에 프래그먼트가 있으면 접미사가 그 뒤에 붙어 요청에서 빠진다(다원 URL에는 없음).
- 실행 슬롯의 반대편 규칙은 추정이며 틀리면 설정으로 다른 슬롯을 고른다.
- 이전 기록 파일 형식(`version`·`url` purpose, `body`)과의 호환은 다루지 않는다.

## 검증 방법

| 수용 기준 | 종류 | 대상 | 전제 | 순서 | 기대 결과 |
| --- | --- | --- | --- | --- | --- |
| `R01-A1` | 단위 테스트 | `AppConfigurationTests` | 없음 | `dotnet test --solution Biz.Bizadm.PowerManager.slnx` | 기본 2, 0·3이면 설정 오류 |
| `R01-A14` | 단위 테스트, 실행 | `RunnerFotaTests`; 실제 다원 | 실행: 펌웨어 `1.01.30` | 테스트 명령; 수동 실행 | 요청 경로가 `["/fw/b540_1"]` 하나, `RunningSlot = 1`이면 `_2`; 실제 200과 기준 SHA-256 |
| `R01-A15` | 단위 테스트 | `RunnerFotaTests`, `ConsoleCommandTests` | 없음 | 테스트 명령 | `fota` 한 요청·두 번이면 두 번, `get`은 가공 없음 |
| `R01-A9`, `R01-A16` | 단위 테스트 | `RunnerFotaTests`, `FotaDownloaderTests` | 없음 | 테스트 명령 | 응답 항목에 접미사 포함 `url`·상태·바이트 수·SHA-256·파일 이름, `body` 없음 |
| `R01-A17` | 실행 | 가짜 장비 테스트 전체 | 없음 | 테스트 명령 | 151건 통과 |

## 원칙 점검

- 적용 규칙: 그때의 MCP `workflow/spec-driven.md`, MCP `git/github-issue.md`
- 충돌: 없음
