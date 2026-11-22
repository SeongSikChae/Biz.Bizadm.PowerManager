# 장치 제어 화면·기능 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 서버 기반(Core 계약·장비 상태)

- [x] T001 [P] `MqttOperation`에 `Execute`(`e`)·`Read`(`r`) 상수를 더한다 (src/Biz.Bizadm.PowerManager.Core/Mqtt/MqttOperation.cs) — C7, C30
- [x] T002 [P] `DeviceCommandTarget`·`DevicePublishResult` 레코드를 만든다 (src/Biz.Bizadm.PowerManager.Core/Mqtt/DeviceCommandTarget.cs, src/Biz.Bizadm.PowerManager.Core/Mqtt/DevicePublishResult.cs) — C6
- [x] T003 `IDeviceCommandPublisher`를 `PublishAsync(DeviceCommandTarget, string operation, IReadOnlyList<MqttElement>, CancellationToken)` 하나로 바꾸고, `MqttDeviceCommandPublisher`는 이 단계에서 컴파일만 되게 맞춘다 (src/Biz.Bizadm.PowerManager.Core/Mqtt/IDeviceCommandPublisher.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttDeviceCommandPublisher.cs) — C6
- [x] T004 `DeviceStateStore`를 만든다. 내용은 다음과 같다. 장비별 온라인·릴레이·계량 값·마지막 보고 시각·토픽 정보·이번 접속의 읽기 상태(없음·보내는 중·넣음)·마지막으로 보낸 설정, mac 변경 알림(구독·해제). 장비의 메시지 판정(장치 키 요소 가운데 ClientId mac과 같은 것, 대소문자 무시, 아니면 Warning 로그)과 한 메시지 안의 C3 → C1 → C30 순서. 접속(온라인·읽기 상태 지움)과 끊김(`Takeover`면 무시) (src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceStateStore.cs) — C1, C2, C3, C13, C30
- [x] T005 [P] `DeviceStatusResponse`와 제어 요청·설정 응답 모델을 만든다 (src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceStatusResponse.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceSettingsModels.cs) — C4, C8, C10, C11, C12, C13
- [x] T006 [P] 테스트 MQTT 클라이언트·받은 메시지 모음·기다리기·표시 메시지 헬퍼를 만들고, `TestDevices`에 `modelId`를 받는 등록 헬퍼를 더한다 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TestMqtt.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TestDevices.cs) — C28

체크포인트: `dotnet build`가 경고 없이 성공하고, 기존 서버 테스트가 모두 통과한다(이 단계에서는 브로커에 아직 연결하지 않음).

## 단계 2: 브로커 연결·명령 발행

- [x] T007 `DownlinkDeliveryTracker`를 만든다. `sid`별 대기를 등록하고, 큐 넣음(받는 쪽 ClientId)·구독자 없음 결과를 짝짓는다. 이벤트에서는 보낸 쪽이 `power-manager`이고 서버 → 장치 토픽인 메시지만 페이로드 `sid`를 읽는다 (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/DownlinkDeliveryTracker.cs) — C6
- [x] T008 `MqttDeviceCommandPublisher`를 구현한다. 서버 실행마다 100부터 늘린 `sid`를 쓰고, retain 없이 QoS 0으로 주입하며, 대상 `DAWONDNS-{mac}` 큐 넣음을 최대 3초 기다린다. C14 Information·Warning·Error 로그를 남긴다 (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttDeviceCommandPublisher.cs) — C6, C14
- [x] T009 `DeviceRelayReader`를 만든다. 저장소가 읽기가 필요하다고 판정하면 `OnPublishAsync`를 기다리게 하지 않고 따로 `r` `/100/0/30`(값 빈 문자열)을 보낸다. 결과는 넣음, 또는 표시 지움으로 저장소에 돌려준다 (src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceRelayReader.cs) — C30
- [x] T010 `MqttBrokerHostedService`에 다음을 더한다. `ClientConnectedAsync`·`ClientDisconnectedAsync`를 저장소에 넘기고, 해석한 장치 → 서버 메시지를 분배하기 전에 ClientId와 함께 저장소에 넘긴다. LWT는 페이로드를 해석하지 않고 토픽과 ClientId만 넘긴다(D021). 큐 넣음·구독자 없음 이벤트는 `DownlinkDeliveryTracker`에도 넘긴다. 기존 로그·분배는 그대로 둔다 (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs) — C1, C2, C3, C6, C30
- [x] T011 새 서비스(`DeviceStateStore`·`DownlinkDeliveryTracker`·`DeviceRelayReader`)를 등록한다 (src/Biz.Bizadm.PowerManager.Server.Spa/Program.cs) — C1, C2, C3, C6, C14, C30
- [x] T012 기존 시간 동기·패킷 분할 테스트에서, 장치가 `tisyn` 전에 `n` `/100/0/31` 상태 보고를 먼저 보내게 준비 단계만 바꾼다(단정은 그대로) (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TimeSyncResponseTests.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttPacketFragmentationTests.cs) — C28
체크포인트: 기존 MQTT 테스트(`TimeSyncResponseTests`·`MqttPacketFragmentationTests`·`MqttSubscribeLogTests`·`MqttBrokerTests`)가 통과한다. 장비 상태 테스트(T013)는 상태 조회 API(T017)가 있어야 하므로 단계 3에서 한다(D020).

## 단계 3: 관리 API

- [x] T014 `DeviceStatusService`를 만든다. DB 등록 정보와 저장소 상태를 합친 C4 원소(mac 오름차순)를 만들고, 변경 구독을 준다. 같은 장비 알림은 하나로 합친다. `Program.cs`에 등록한다(D025) (src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceStatusService.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Program.cs) — C4, C5
- [x] T015 `DeviceRegistrationService`가 장비를 새로 만들거나 `modelId`를 바꾸면 저장소에 알리게 한다 (src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceRegistrationService.cs) — C5
- [x] T016 `DeviceCommandService`를 만든다. 판정 순서는 mac 형식(400) → 등록(404) → 본문 범위·모델(400) → 제어 가능(409) → 큐 넣음(409)이다. 요소는 C7~C12대로 만든다(기준 전력 소수 둘째 자리 채움, 요일 불리언 7개를 일~토 `0`·`1` 문자열로. T038에서 C12의 7글자로 바꿈). 성공한 설정을 기록하고, 예약 삭제가 성공하면 예약을 null로 둔다. `Program.cs`에 등록한다(D025) (src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceCommandService.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Program.cs) — C6, C7, C8, C9, C10, C11, C12, C13
- [x] T017 [P] 상태 조회(`GET /api/admin/devices/status`)·상태 스트림(`GET /api/admin/devices/status/stream`, `TypedResults.ServerSentEvents`, 처음 `snapshot` 다음 `device`, `RequestAborted`·`ApplicationStopping`에 끝남)·설정 조회(`GET /api/admin/devices/{mac}/settings`) API를 `[AdminPortOnly]`로 만든다 (src/Biz.Bizadm.PowerManager.Server.Spa/Devices/AdminDeviceStatusController.cs) — C4, C5, C13, C15
- [x] T018 [P] 제어 API(`relay`·`power-cycle`·`reboot`·`report-condition`·`standby-cutoff`·`schedule` PUT·DELETE)를 `[AdminPortOnly]`로 만들고, 실패는 ProblemDetails로 반환한다 (src/Biz.Bizadm.PowerManager.Server.Spa/Devices/AdminDeviceControlController.cs) — C6, C7, C8, C9, C10, C11, C12, C15
- [x] T013 장비 상태 테스트를 쓴다. 상태 조회 API(T017)와 받은 서버 → 장치 메시지로 다음을 확인한다(D020).
  - C1: `notify`·`n`의 릴레이·계량 반영, 없는 항목 유지, 숫자가 아닌 값 무시, 마지막 보고 시각
  - C2: 접속·끊김·같은 ClientId 재접속 뒤 온라인
  - C3: `tisyn`만 보낸 장비의 토픽 기억
  - 장치 키에 ClientId mac이 없으면 Warning 로그와 미반영
  - C30: 읽기를 큐에 넣으면 같은 접속에서 다시 보내지 않음, 넣지 못하면 다음 메시지 때 다시 보냄, 재접속 뒤 다시 보냄

  (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DeviceStateTests.cs) — C1, C2, C3, C30, C28
- [x] T019 [P] 상태 API 테스트를 쓴다. 다음을 확인한다.
  - C4: 필드·값·MQTT 키 없음·보고 없던 장비의 모름·null
  - C5: 스트림 `snapshot`, 그리고 상태 보고·접속·끊김·토픽 처음 알게 됨·등록 때의 `device`, 요청 취소로 끝남
  - C15: 장치 포트 404

  (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DeviceStatusApiTests.cs) — C4, C5, C15, C28
- [x] T020 [P] 제어 API 테스트를 쓴다. 다음을 확인한다.
  - API마다 받은 페이로드(`o`, 요소 순서와 값)
  - 400·404·409와 판정 순서, 구독 없는 장비 409, 비 B540 재부팅 400
  - 범위 경계 값(C8·C10·C11·C12)
  - 설정 조회 값과 예약 삭제 뒤 null
  - C14 로그 항목
  - 장치 포트 404와 메시지 없음

  (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DeviceControlApiTests.cs) — C6, C7, C8, C9, C10, C11, C12, C13, C14, C15, C28

체크포인트: `dotnet test`(솔루션 전체)가 통과한다.

## 단계 4: 클라이언트 기반

- [x] T021 `react-router` 7.x(React 18 peer 확인, 최신 패치)를 npm으로 더한다 (src/Biz.Bizadm.PowerManager.Client/package.json, src/Biz.Bizadm.PowerManager.Client/package-lock.json) — C16
- [x] T022 [P] shadcn CLI로 `card`·`switch`·`alert-dialog`·`label`·`checkbox`를 더하고, `Switch`·`Label`·`Checkbox`를 `React.forwardRef`로 보정한다 (src/Biz.Bizadm.PowerManager.Client/src/components/ui/card.tsx, switch.tsx, alert-dialog.tsx, label.tsx, checkbox.tsx) — C17
- [x] T023 [P] 상태용 색 토큰 `--success`·`--color-success`를 더한다 (src/Biz.Bizadm.PowerManager.Client/src/index.css) — C17
- [x] T024 [P] 상태·설정·요청 타입과 RTK Query API(상태 조회·설정 조회·제어 mutation)를 만든다 (src/Biz.Bizadm.PowerManager.Client/src/features/device-status/types.ts, src/Biz.Bizadm.PowerManager.Client/src/features/device-status/deviceStatusApi.ts) — C18~C27
- [x] T025 상태 slice(장비별 상태, 스트림 연결 상태, 받은 적 있음)와 `useDeviceStatusStream`을 만든다. 훅은 `snapshot`·`device` 이벤트를 반영하고, `CLOSED`이면 3초 뒤 새 `EventSource`를 연다. store에 slice와 API를 더한다 (src/Biz.Bizadm.PowerManager.Client/src/features/device-status/deviceStatusSlice.ts, src/Biz.Bizadm.PowerManager.Client/src/features/device-status/useDeviceStatusStream.ts, src/Biz.Bizadm.PowerManager.Client/src/app/store.ts) — C20
- [x] T026 [P] 테스트용 가짜 `EventSource`를 setup에 둔다 (src/Biz.Bizadm.PowerManager.Client/src/test/setup.ts) — C28
- [x] T027 [P] 계량 값·요약·상대 시간 표시 함수와 테스트를 만든다 (src/Biz.Bizadm.PowerManager.Client/src/features/device-status/format.ts, src/Biz.Bizadm.PowerManager.Client/src/features/device-status/format.test.ts) — C18, C19, C24
- [x] T028 라우터(`/`·`/devices`·`/devices/:mac`)와 스트림 시작, `BrowserRouter`, 메뉴 "대시보드"·"장비" `Link`(현재 메뉴는 `useMatch`로 판정. `/devices`면 "장비", `/`와 `/devices/:mac`이면 "대시보드"에 `aria-current="page"`)를 만들고, 앱 테스트를 경로·메뉴·`aria-current`·뒤로 가기 확인으로 바꾼다. `DashboardPage`·`DeviceControlPage`는 이 단계에서 제목만 있는 화면으로 만들고 T030·T032에서 채운다(D026) (src/Biz.Bizadm.PowerManager.Client/src/App.tsx, src/Biz.Bizadm.PowerManager.Client/src/main.tsx, src/Biz.Bizadm.PowerManager.Client/src/components/AppShell.tsx, src/Biz.Bizadm.PowerManager.Client/src/App.test.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/dashboard/DashboardPage.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/DeviceControlPage.tsx) — C16

체크포인트: `npm test`·`npm run build`·`npm run lint`가 통과하고, 기존 장비 목록 테스트가 `/devices`에서 그대로 통과한다.

## 단계 5: 대시보드·장비 제어 화면

- [x] T029 장비를 밝힌 확인 창과 릴레이 제어를 만든다. 릴레이 제어는 스위치·상태 모름 때 켜기·끄기 버튼, 끄기 확인 창(Esc·취소는 보내지 않음), 진행 중 비활성, 스트림으로 보낸 값을 받으면 끝, API 실패·10초면 되돌리고 오류 안내다. 테스트(가짜 타이머)도 쓴다 (src/Biz.Bizadm.PowerManager.Client/src/features/device-status/ConfirmDialog.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-status/RelayControl.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-status/RelayControl.test.tsx) — C21, C22, C28
- [x] T030 대시보드를 만든다. 요소는 다음과 같다.
  - C18: 제목·요약·연결 상태
  - C19: mac 오름차순 카드(모델·mac 고정폭·온라인 점과 글자·전력·전압·전류·역률·릴레이 제어·"제어" 버튼·상대 시간·오프라인 흐림)
  - C20: 스트림 반영
  - C23: 스켈레톤·오류·"다시 시도"·빈 목록, 390px 한 줄 배치

  테스트는 스트림 이벤트·끊김·`CLOSED` 뒤 다시 열림을 확인한다 (src/Biz.Bizadm.PowerManager.Client/src/features/dashboard/DashboardPage.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/dashboard/DeviceCard.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/dashboard/DashboardPage.test.tsx) — C17, C18, C19, C20, C21, C22, C23, C28
- [x] T031 [P] 입력 범위 검사 함수와 테스트를 만든다(C8·C10·C11·C12 범위와 이유 문구) (src/Biz.Bizadm.PowerManager.Client/src/features/device-control/validation.ts, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/validation.test.ts) — C27
- [x] T032 장비 제어 화면을 만든다. 요소는 다음과 같다.
  - C24: 머리, 실시간 반영, 미등록 안내
  - C25: "전원" 묶음(릴레이·전원 재투입 기본 10과 단위 안내(문구는 T039에서 바꿈)·B540만 재부팅, 확인 창)
  - C26: 세 설정 묶음(서버가 마지막으로 보낸 값으로 채움·안내, 예약 삭제 확인 창)
  - C27: 범위 밖 입력은 보내지 않고 이유 표시, 보내는 동안 비활성, 성공·실패 안내, 제어 불가 장비는 모든 버튼 비활성과 까닭

  테스트도 쓴다 (src/Biz.Bizadm.PowerManager.Client/src/features/device-control/DeviceControlPage.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/PowerSection.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/ReportConditionForm.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/StandbyCutoffForm.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/ScheduleForm.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/DeviceControlPage.test.tsx) — C24, C25, C26, C27, C28

체크포인트: 다음을 확인한다.
- `npm test`·`npm run build`·`npm run lint`가 통과한다.
- 개발 서버와 내장 브라우저에서 1440px·390px 대시보드와 장비 제어 화면을 본다. 390px에서 가로 스크롤이 없고 카드가 한 줄로 쌓이는지(C23), 온라인 점의 계산된 색이 `--success`인지(C17) 본다.
- 서버를 다시 시작한 뒤 화면이 다시 연결되는지(C20) 본다.

## 단계 5-1: 요일 글자(D027)와 전원 재투입 안내 변경(의도 변경)

- [x] T038 [P] 예약 요일 변환을 C12의 7글자(첫 글자 `0`, 둘째 글자는 월 1과 일 2의 합, 나머지는 화~토의 `0`·`1`)로 바꾸고, 제어 API 테스트의 예약 기대 페이로드를 고친다. 일요일만·월요일만·일요일과 월요일·모든 요일·화~토만의 글자를 확인한다 (src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceCommandService.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DeviceControlApiTests.cs) — C12, C28
- [x] T039 [P] 전원 재투입 안내를 C25의 문구로 바꾸고 화면 테스트를 고친다 (src/Biz.Bizadm.PowerManager.Client/src/features/device-control/PowerSection.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/DeviceControlPage.test.tsx) — C25, C28

체크포인트: `dotnet test`(솔루션 전체)와 `npm test`·`npm run build`·`npm run lint`가 통과한다.

## 단계 5-2: 장비 제어 화면의 PC 배치(의도 변경, D032)

- [x] T040 장비 제어 화면의 네 묶음을 C32의 규격으로 바꾸고 C25의 안내 문구를 맞춘다. 묶음 틀 `Section`을 제목·안내·내용·버튼 줄로 나누고, 네 묶음이 이 틀을 쓰게 하며, 화면 테스트를 고친다(안내·버튼 순서와 종류, 성공 안내가 버튼 줄에 있음) (src/Biz.Bizadm.PowerManager.Client/src/features/device-control/DeviceControlPage.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/PowerSection.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/ReportConditionForm.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/StandbyCutoffForm.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/ScheduleForm.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/DeviceControlPage.test.tsx) — C25, C32, C28

체크포인트: `npm test`·`npm run build`·`npm run lint`가 통과하고, 1440px·2000px·390px 브라우저 화면에서 C28이 정한 C32의 항목을 확인한다.

## 단계 6: 실장비 확인

- [x] T033 실장비 한 대를 사용자에게 정해 받는다(대상 장치를 확인받기 전에는 보내지 않는다). 그 장치가 이 서버에 접속한 상태에서 다음 순서로 확인한다.
  1. 장비 제어 화면에서 그 날의 요일만 고른 예약을 몇 분 뒤 시각으로 저장한다.
  2. 서버 로그의 보낸 문자열과 그 요일이 C12대로인지 본다.
  3. 그 시각에 릴레이가 꺼졌다가 N초 뒤 켜지는 것을 본체에서 보고(사용자), 다시 켤 때의 보고(`/100/0/31` `true`, `/100/0/32` `16`)를 서버 로그에서 본다.
  4. 동작하지 않으면 원인을 가려 고치고 다시 확인한다.
  5. 끝나면 예약을 삭제하고, 결과(모델·펌웨어 버전·보낸 문자열·관찰, mac은 적지 않음)와 spec에 적은 2026-11-22 관찰(요일 글자, 전원 재투입 단위, 본체 조작 때와 예약 시각 전후의 보고, 장치 시계)을 근거 문서의 해당 절(머리말의 근거·표기, 2절·3.1·3.3·3.4·6절·7절)과 `b540-mqtt-topic.md`의 시간 동기·미확인 절에 적고, `b540-mqtt-topic.md` 머리말과 5절에서 B540 실제 통신을 관찰하지 않았다는 문장을 맞춘다.

  (docs/reference/mqtt-device-control.md, docs/reference/b540-mqtt-topic.md) — C31

체크포인트: 근거 문서와 `b540-mqtt-topic.md`의 해당 절에 확인 결과와 관찰이 적혀 있고, 확인에 쓴 예약이 삭제됐다.

## 단계 7: 기준 spec 반영

- [x] T034 [P] 새 기준 spec 두 개를 만든다. `docs/features/device-dashboard.md`(C16~C23, 대시보드 쪽 파일)와 `docs/features/device-control.md`(C1~C15·C24~C27·C30, 서버 제어·장비 제어 화면 파일)이며, 요구 R번호·이력·`파일:` 줄을 넣는다 (docs/features/device-dashboard.md, docs/features/device-control.md) — C29
- [x] T035 [P] `mqtt-broker` 기준 spec을 고친다. 머리말의 "서버 → 장치 명령 발행(`MqttDeviceCommandPublisher`)은 껍데기다" 서술을 이번 구현에 맞게 고치고(D022), R5(명령 발행 구현)를 바꾸고, 상태 보고 처리·접속 추적·제어 메시지 발행 요구를 더하며, 이력과 `파일:` 줄(DownlinkDeliveryTracker.cs·DeviceCommandTarget.cs·DevicePublishResult.cs·TestMqtt.cs)을 맞춘다 (docs/features/mqtt-broker.md) — C29
- [x] T036 [P] `admin-ui` 기준 spec을 고친다. R1(상태용 색 토큰)·R3(메뉴 둘·경로)·R4(새 UI 부품)를 바꾸고, 이력과 `파일:` 줄(`main.tsx`·새 `components/ui` 부품)을 맞춘다 (docs/features/admin-ui.md) — C29
- [x] T037 [P] `device-list` 기준 spec을 고친다. 머리말의 "관리 화면 `/`"를 `/devices`로 고치고(D022), R4(화면 경로 `/devices`)를 바꾸고, 이력과 `파일:` 줄을 맞춘다 (docs/features/device-list.md) — C29

체크포인트: 다섯 기준 spec의 머리말·요구·이력·`파일:` 줄이 이번 변경과 맞고, 나열한 파일이 모두 저장소에 있다.

## 단계 8: 근거 문서와 기준 spec 다시 반영(의도 변경, D032·D033)

- [x] T042 [P] 근거 문서에 대기전력 차단 관찰(차단 때의 보고와 원인 `1`, 기준 전력 `1.00`에 대한 응답 `1.10`, B540 `1.01.34`의 판정 코드)을 적는다(머리말의 근거, 2절·3.2·3.4·7절) (docs/reference/mqtt-device-control.md) — C31
- [x] T041 [P] `device-control` 기준 spec을 고친다. R17(안내 문구)을 바꾸고 C32의 요구를 더하며, R20의 테스트 범위를 맞춘다. R12에 기준 전력의 응답 관찰을 더한다. 이력은 이번 작업의 줄을 고친다 (docs/features/device-control.md) — C29

체크포인트: `device-control` 기준 spec의 요구·이력이 C25·C32와 맞고, 근거 문서에 B540의 대기전력 차단 때 보고를 미확인으로 적은 문장이 남지 않는다(M130-W와 배율·지연 단위·범위는 미확인으로 남긴다).

## 의존 관계

- 단계 1 → 단계 2 → 단계 3: 브로커 연결(T010)은 저장소(T004)와 발행 계약(T003)이 있어야 하고, 관리 API(T016~T018)는 발행기(T008)와 상태 서비스(T014)가 있어야 한다.
- T012는 T010 뒤에 한다. C30 읽기가 들어간 뒤에야 기존 테스트가 영향을 받는지 확인할 수 있다.
- T013은 단계 3의 T017 뒤에 한다. 장비 상태를 상태 조회 API로 확인하기 때문이다(D020).
- 단계 4·5는 단계 3의 API 형식(T005·T017·T018)을 따른다. 클라이언트 테스트는 `fetch`와 가짜 `EventSource`를 쓰므로 서버 없이 돌지만, 단계 5 체크포인트의 브라우저 확인은 단계 3까지 끝나야 한다.
- 단계 5-1은 구현을 시작한 뒤의 의도 변경으로 더한 단계다. T038은 예약 요일(D027)로 T016·T020의 결과를, T039는 2026-11-22 전원 재투입 관찰(spec.md 「근거의 한계」, C25)로 T032의 결과를 고친다.
- 단계 5-2는 단계 6·7을 끝낸 뒤의 의도 변경(D032)으로 더한 단계이며, 이미 끝난 T032·T039의 결과를 고친다. 제어 메시지는 바뀌지 않으므로 T033의 실장비 확인은 다시 하지 않는다.
- 단계 8은 단계 5-2가 끝난 뒤 한다. 단계 7에서 만든 `device-control` 기준 spec을 이번 변경에 맞춘다.
- 단계 6은 단계 5의 장비 제어 화면과 단계 5-1의 요일 변환이 있어야 한다.
- 기준 spec 반영(단계 7, 의도 변경 뒤에는 단계 8)이 마지막 구현 단계다. 앞 단계가 모두 끝난 뒤 바뀐 파일과 요구를 기준 spec에 반영한다.
