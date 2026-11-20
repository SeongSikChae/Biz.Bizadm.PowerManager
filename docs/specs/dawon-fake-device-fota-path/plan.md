# 다원 가짜 장비 FOTA 요청 경로 맞춤 계획

## 요약

가짜 장비의 FOTA 받기를 "버전 요청 + 원래 URL 요청" 두 요청에서 "URL에 슬롯 접미사를 붙인 이미지 요청" 하나로 바꾼다. 접미사는 새 설정 `Device.RunningSlot`(기본 2)의 반대편(`_1`/`_2`)이다. 자동 받기와 콘솔 `fota` 명령이 이 요청을 쓰고, 콘솔 `get`은 그대로 둔다. 버전 요청이 없어지므로 FOTA 응답 기록의 본문 필드와 그 코드를 없앤다. 테스트를 새 동작에 맞추고, 기준 분석 기록 `docs/reference/b540-fota.md`와 기준 spec `docs/features/dawon-fake-device.md`를 고친다. 마지막으로 실제 다원 서버로 다시 실행해 이미지를 받는다(C7).

## 바꿀 파일·구조

- `src/Biz.Bizadm.PowerManager.FakeDevice.App/AppConfiguration.cs`: `DeviceSection.RunningSlot`(int, 기본 2) 추가, `Validate`에서 1·2가 아니면 `ArgumentException`
- `src/Biz.Bizadm.PowerManager.FakeDevice.App/FakeDeviceOptions.cs`: `RunningSlot`(int, 기본 2)과 계산 속성 `FotaSuffix`(2 → `_1`, 1 → `_2`) 추가, 설정에서 옮겨 담기
- `src/Biz.Bizadm.PowerManager.FakeDevice.App/Fota/FotaDownloader.cs`: `PurposeVersion`·`PurposeUrl`·`VersionUrl`·`DownloadPairAsync`·본문 보관(`keepBody`) 제거. `PurposeImage = "image"`, `ImageUrl(url, suffix)`, `DownloadImageAsync(url, token)` 추가
- `src/Biz.Bizadm.PowerManager.FakeDevice.App/FakeDeviceRunner.cs`: 자동 받기와 `fota` 명령이 `DownloadImageAsync`를 부름
- `src/Biz.Bizadm.PowerManager.FakeDevice.App/Console/ConsoleCommand.cs`: `fota` 도움말·주석을 새 동작으로
- `src/Biz.Bizadm.PowerManager.FakeDevice.App/Recording/FakeDeviceRecorder.cs`: `FotaResponseRecord.Body`와 그 기록 코드 제거, `FotaRequestAsync`의 purpose 설명 갱신
- `src/Biz.Bizadm.PowerManager.FakeDevice.App/config.example.yaml`: `Device.RunningSlot: 2`와 설명 추가
- `tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/FotaDownloaderTests.cs`: `VersionUrl` 시험을 `ImageUrl` 시험으로 바꿈
- `tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/RunnerFotaTests.cs`: 요청 경로·purpose·응답 기록 기대값을 새 동작으로, 버전 404 시험을 이미지 404 시험으로, 실행 슬롯 1 시험 추가
- `tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/ConsoleCommandTests.cs`: 도움말(`UsageText`)의 `fota` 줄이 새 동작을 보이고 `/version`이 없는지 보는 시험 추가
- `tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/AppConfigurationTests.cs`: `RunningSlot` 기본값·옮겨 담기·잘못된 값(0, 3) 시험 추가
- `docs/reference/b540-fota.md`: spec C8대로 고침
- `docs/features/dawon-fake-device.md`: spec C9대로 개요·범위 밖·R1·R9·R13·R14·R15·R16·이력을 고침

## 결정

- 접미사는 받은 URL 문자열 끝에 그대로 붙인다(`url + suffix`): 장치는 URL을 호스트·포트·경로로 나눈 뒤 경로 필드(호스트 뒤 전체) 끝에 접미사를 붙인다. 문자열 끝에 붙이면 같은 결과다. 끝의 `/`도 손대지 않는다(장치도 손대지 않음) / 검토한 대안: `Uri`로 경로만 뽑아 붙이기 — 쿼리가 있을 때 장치와 결과가 달라지고 코드만 늘어 고르지 않음
- 접미사는 옵션의 계산 속성 `FotaSuffix` 한 곳에서 정한다: 다운로더·러너·테스트가 같은 규칙을 쓴다 / 검토한 대안: 다운로더 안에서 계산 — 시험에서 옵션만으로 기대값을 만들기 어려워 고르지 않음
- 요청 목적(purpose) 이름은 `image`, 저장 파일 이름 끝은 `-image.bin`: 기존 `version`·`url`이 없어지고, 받는 것이 이미지임을 드러낸다. `get`은 그대로 `get` / 검토한 대안: `url`을 그대로 씀 — 접미사가 붙은 요청과 예전 요청이 기록에서 구분되지 않아 고르지 않음
- `FotaResponseRecord.Body`를 지운다: 본문을 담던 요청(버전 요청)이 없어져 늘 비게 된다(spec C5) / 검토한 대안: 필드를 남김 — 쓰지 않는 코드가 남아 고르지 않음
- `RunningSlot` 검증은 기존 설정 검증(`AppConfiguration.Validate`의 `ArgumentException` → 종료 코드 2) 경로를 쓴다: 다른 설정 오류와 처리가 같다(spec C2)

D001~D007은 spec 단계 결정으로 위 설계의 전제다(요청 하나, 반대편 접미사, 버전 비교 없음, `get` 유지, C7 판정 방법).

## 인터페이스·데이터

설정(YAML):

```yaml
Device:
  RunningSlot: 2   # 흉내 낼 실행 슬롯(1|2). FOTA는 반대편 이미지를 받는다(2 → _1, 1 → _2)
```

콘솔 도움말:

```
  fota <url>              GET {url}{slot suffix} (RunningSlot 2 -> _1, 1 -> _2)
  get <url>               GET {url}
```

FOTA 기록(채널 `fota`):

| 항목 | 바뀌는 것 |
|---|---|
| `request` | `purpose`가 `image` 또는 `get`. `url`은 실제로 보낸 URL(`image`면 접미사 포함) |
| `response` | `url`은 실제로 보낸 URL. `body`·`bodyEncoding` 필드는 더 나오지 않음. 나머지 그대로 |
| `error` | `url`은 실제로 보낸 URL. 그대로 |
| 저장 파일 | `fota/<UTC 시각>-<순번>-image.bin` 또는 `-get.bin` |

`candidate` 항목은 받은 원래 URL을 그대로 담는다(바뀌지 않음). 자동 받기의 "같은 URL은 한 번만"은 원래 URL 기준이다(바뀌지 않음).

## 오류·위험

- URL에 프래그먼트(`#…`)가 있으면 접미사가 프래그먼트 뒤에 붙고, HTTP 요청에서는 프래그먼트가 빠지므로 접미사도 빠진다. 다원이 보낸 URL에는 프래그먼트가 없어 실제로 일어나지 않는다. 다루지 않는다.
- `RunningSlot` 규칙(반대편 슬롯)은 추정이다(spec 무엇을 왜). 틀렸으면 설정으로 다른 슬롯을 고를 수 있다.
- 기존 기록 파일(`version`·`url` purpose, `body` 필드)을 읽는 도구는 없다. 호환을 다루지 않는다.
- 실제 다원 실행(C7)은 다원이 FOTA 메시지를 보내야 이미지 받기까지 간다. 지난 실행처럼 펌웨어 버전 1.01.30과 `up notify/json` 응답으로 유도한다. 보내지 않으면 그 사실을 적는다(spec C7).

## 검증 전략

- C1: `RunnerFotaTests` 자동 받기 시험. 쓰기 메시지를 받으면 요청 경로가 `["/fw/b540_1"]` 하나뿐이고 `/version`이 없는지, 고정 헤더가 그대로인지 본다. 중첩 배열 URL(끝 `/`)은 `"/fw/b540/_1"`, 같은 URL 두 번은 요청 1개, URL 후보 없음·자동 받기 끔은 요청 0개다.
- C2: `AppConfigurationTests`로 기본값 2와 옵션 옮겨 담기, 0·3이면 설정 오류를 본다. `FotaDownloaderTests`로 `ImageUrl`을 본다. `RunnerFotaTests`에서 `RunningSlot = 1`이면 `/fw/b540_2`를 요청하는지 본다. 종료 코드 2는 기존 `ProgramTests`의 설정 오류 경로가 덮는다(검증 예외가 같은 경로).
- C3: `RunnerFotaTests` 명령 시험. `fota`가 `/fw/b540_1` 한 요청을 보내고, 두 번 보내면 두 번 요청하는지 본다. 도움말 문자열은 `ConsoleCommandTests`에 더할 시험으로 본다(지금은 도움말 시험이 없음).
- C4: 같은 명령 시험에서 `get`이 `/fw/b540`(가공 없음)을 요청하는지 본다.
- C5: `RunnerFotaTests`에서 응답 항목의 `url`이 접미사를 포함하고, `body`가 없고, `status`·`contentType`·`contentLength`·`bytes`·`sha256`·`file`(`-image.bin`)이 맞는지 본다. 이미지 404면 상태만 기록하고 파일이 없는지 본다. 시간 초과·연결 실패·크기 초과·느린 받기·종료 취소 시험은 그대로 통과해야 한다.
- C6: `dotnet test`로 가짜 장비 테스트 프로젝트 전체를 돌려 모두 통과하는지 본다. 솔루션 빌드 경고·오류도 본다.
- C7: 실제 다원 서버로 수동 실행한다(펌웨어 버전 1.01.30, 그 밖 기본값). FOTA URL을 받으면 자동 받기 기록에서 요청 URL·상태·바이트 수·SHA-256을 작업 문서에 적고 기준값과 대조한다.
- C8: `b540-fota.md`를 읽어 spec C8의 항목이 관찰·추정 표기와 근거 주소와 함께 있는지, 열거한 모순 서술이 남지 않았는지, 실장비 값·덤프 경로가 없는지 본다.
- C9: 기준 spec을 읽어 개요·범위 밖·R1·R9·R13~R16·이력이 새 동작과 맞는지 본다.

## 원칙 점검

- 적용 규칙: 전역 `workflow/spec-driven.md`(변경 문서·검토·기준 spec 반영), `workflow/repository-content.md`(저장소에 실장비 값·덤프·받은 펌웨어를 넣지 않음), `git/github-issue.md`(GitHub #24), 전역 언어 규칙(문서는 한국어, 코드 식별자·로그는 영어), 저장소 기존 코드 관례(설정 섹션 PascalCase, `Property` 특성 검증, MSTest)
- 충돌: 없음. C7 실행 기록·받은 이미지는 scratchpad와 작업 문서에만 둔다.
