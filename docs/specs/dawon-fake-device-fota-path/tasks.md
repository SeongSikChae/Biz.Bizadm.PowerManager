# 다원 가짜 장비 FOTA 요청 경로 맞춤 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

경로 앞의 `src/…App/`는 `src/Biz.Bizadm.PowerManager.FakeDevice.App/`, `tests/…Tests/`는 `tests/Biz.Bizadm.PowerManager.FakeDevice.App.Tests/`다.

## 단계 1: 설정

- [x] T001 `DeviceSection.RunningSlot`(int, `Property` INT, 필수 아님, 기본 2)을 더하고 `Validate`에서 1·2가 아니면 `ArgumentException`을 던진다 (src/…App/AppConfiguration.cs) — C2
- [x] T002 `FakeDeviceOptions`에 `RunningSlot`(기본 2)과 계산 속성 `FotaSuffix`(2 → `_1`, 1 → `_2`)를 더하고 설정에서 옮겨 담는다 (src/…App/FakeDeviceOptions.cs) — C2
- [x] T003 [P] `Device.RunningSlot: 2`와 설명을 더한다 (src/…App/config.example.yaml) — C2
- [x] T004 `RunningSlot` 기본값 2, 옵션 옮겨 담기, 0·3이면 설정 오류 시험을 더한다 (tests/…Tests/AppConfigurationTests.cs) — C2, C6

체크포인트: 가짜 장비 테스트 프로젝트가 빌드되고 `AppConfigurationTests`가 통과한다.

## 단계 2: 받기 동작

- [x] T005 `PurposeVersion`·`PurposeUrl`·`VersionUrl`·`DownloadPairAsync`·본문 보관(`keepBody`)을 지우고 `PurposeImage = "image"`, `ImageUrl(url, suffix)`(문자열 끝에 그대로 붙임), `DownloadImageAsync(url, token)`을 더한다. 클래스 주석도 고친다 (src/…App/Fota/FotaDownloader.cs) — C1, C3, C5
- [x] T006 `FotaResponseRecord.Body`와 그 기록 코드를 지우고, `FotaRequestAsync`의 purpose 설명을 `image`·`get`으로 고친다 (src/…App/Recording/FakeDeviceRecorder.cs) — C5
- [x] T007 자동 받기와 `fota` 명령이 `DownloadImageAsync`를 부르게 한다. `get`은 그대로 둔다 (src/…App/FakeDeviceRunner.cs) — C1, C3, C4
- [x] T008 [P] `fota` 도움말(`fota <url>  GET {url}{slot suffix} (RunningSlot 2 -> _1, 1 -> _2)`)과 열거·주석을 새 동작으로 고친다 (src/…App/Console/ConsoleCommand.cs) — C3

체크포인트: 솔루션이 경고·오류 없이 빌드된다.

## 단계 3: 테스트

- [x] T009 [P] `VersionUrl` 시험을 `ImageUrl` 시험(끝 `/` 있음·없음, 접미사 `_1`·`_2`)으로 바꾼다 (tests/…Tests/FotaDownloaderTests.cs) — C2, C6
- [x] T010 FOTA 러너 시험을 새 동작에 맞춘다 (tests/…Tests/RunnerFotaTests.cs) — C1, C2, C3, C4, C5, C6
  - 가짜 서버 응답: `/fw/b540_1`·`/fw/b540_2`·`/fw/b540`은 이미지 200, 나머지 404
  - 자동 받기: 요청이 `/fw/b540_1` 하나이고 `/version`이 없으며 고정 헤더가 그대로다. 중첩 배열 URL(끝 `/`)은 `/fw/b540/_1`, 같은 URL 두 번은 요청 1개, URL 후보 없음·자동 받기 끔은 요청 0개
  - 실행 슬롯 1(`RunningSlot = 1`)이면 `/fw/b540_2`를 요청한다
  - 명령: `fota`가 `/fw/b540_1` 한 요청이고 두 번 하면 두 번, `get`은 `/fw/b540` 그대로. purpose는 `image`·`get`
  - 응답 기록: `url`이 접미사를 포함하고 `body`가 없으며 `status`·`contentType`·`contentLength`·`bytes`·`sha256`·`file`(`-image.bin`)이 맞다
  - 버전 404 시험을 이미지 404 시험으로 바꾼다: 상태만 기록하고 파일이 없다
  - 시간 초과·연결 실패·크기 초과·느린 받기·종료 취소·HTTPS 시험은 그대로 둔다
- [x] T011 [P] 도움말(`UsageText`)의 `fota` 줄이 새 동작을 보이고 `/version`이 없는지 보는 시험을 더한다 (tests/…Tests/ConsoleCommandTests.cs) — C3, C6
- [x] T012 가짜 장비 테스트 프로젝트 전체를 돌려 모두 통과하는지 본다 (tests/…Tests/) — C6

체크포인트: `dotnet test`로 가짜 장비 테스트가 모두 통과하고 솔루션 빌드에 경고·오류가 없다.

## 단계 4: 문서

- [x] T013 [P] spec C8대로 고친다: 접미사 분기(관찰)·실행 슬롯 의미(추정)·슬롯 A 코드(미확인)·두 슬롯의 상수 위치, 서버가 `_1`·`_2`로 user1·user2 이미지를 주고 `_2`가 덤프 슬롯 B와 같다는 것, 지난 404의 원인. 열거한 모순 서술(머리말 근거 줄, 3절 4항, 다원 URL 예, 4절, 5절 마지막 항목)을 고치거나 지운다. 근거 주소를 붙이고 실장비 값·덤프 경로는 넣지 않는다 (docs/reference/b540-fota.md) — C8
- [x] T014 spec C9대로 개요·범위 밖·R1(`RunningSlot`)·R9(본문 항목 삭제)·R13·R14·R15·R16·이력을 고친다. `파일:` 줄은 이번에 파일을 더하거나 지우지 않으므로 그대로 둔다 (docs/features/dawon-fake-device.md) — C9

체크포인트: 두 문서를 읽어 spec C8·C9의 항목이 모두 반영되고 모순 서술이 남지 않았다.

## 단계 5: 실제 실행

- [x] T015 실제 다원 서버로 실행한다(수동, 펌웨어 버전 1.01.30, 그 밖 기본값, 설정·기록은 scratchpad). FOTA URL을 받으면 자동 받기 기록의 요청 URL·상태·바이트 수·SHA-256을 작업 문서에 적고 사전 확인 기준값과 대조한다. 기준값이 없거나 다르면 그 사실을 적는다. 다원이 FOTA 메시지를 보내지 않으면 그 사실을 적는다 (작업 문서) — C7

체크포인트: C7 결과가 작업 문서에 적혀 있다.

## 의존 관계

- T001 → T002(옵션이 설정 값을 옮겨 담음) → T004. T003은 다른 파일이라 단계 1 안에서 동시에 할 수 있다.
- 단계 2는 T002 뒤(`FotaSuffix`를 씀). T005 → T007(새 메서드를 부름). T006은 T005와 같이 끝나야 빌드된다(`Body` 인자). T008은 다른 파일이라 동시에 할 수 있다.
- 단계 3은 단계 2 뒤. T009·T011은 서로·T010과 독립. T012는 T009~T011 뒤.
- 단계 4는 코드와 독립이지만 기준 spec(T014)은 구현된 동작을 적으므로 단계 3 뒤에 한다. T013은 단계 1~3과 동시에 할 수 있다.
- T015는 단계 3 뒤(고친 프로그램으로 실행). 구현 후 검토(spec-driven 5절)는 T015 뒤에 기준점(`34dd1de`)부터의 diff로 한다.
