# 장비명·입력 단위 표기·제어 화면 로딩 개선 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

경로 약칭: `Server` = src/Biz.Bizadm.PowerManager.Server.Spa, `STests` = tests/Biz.Bizadm.PowerManager.Server.Spa.Tests, `Client` = src/Biz.Bizadm.PowerManager.Client/src

## 단계 1: 서버 장비명
- [x] T001 장비명의 공백 제거와 검사 `DeviceName`, 요청 본문 `DeviceNameRequest` (Server/Devices/DeviceName.cs, Server/Devices/DeviceNameRequest.cs) — C11
- [x] T002 `Name` 열(길이 30, 필수) (Server/Data/DeviceRegistration.cs, Server/Data/PowerManagerDbContext.cs) — C10
- [x] T003 마이그레이션 `DeviceName` 생성. 생성하지 못하거나 열 추가 말고 다른 내용이 있으면 멈추고 알린다. 생성된 DDL에 DB 기본값이 남는지 확인해 작업 기록에 적는다 (Server/Data/Migrations/<시각>_DeviceName.cs, <시각>_DeviceName.Designer.cs, PowerManagerDbContextModelSnapshot.cs) — C10
- [x] T004 `DeviceCommandResult.InvalidName`과 `RenameAsync`(mac → 등록 → 장비명 판정, 같은 값이면 저장·알림 없음, 저장 뒤 알림) (Server/Devices/DeviceCommandService.cs, Server/Devices/DeviceRegistrationService.cs) — C11, C12, C13
- [x] T005 `PUT /api/admin/devices/{mac}/name`과 목록·상태·스트림 원소의 `name` (Server/Devices/AdminDevicesController.cs, AdminDeviceResponse.cs, DeviceStatusResponse.cs, DeviceStatusService.cs) — C11, C13
- [x] T006 서버 테스트: 이전 DB의 마이그레이션, 저장·삭제·공백·길이·문자·판정 순서·장치 포트, 재등록과의 독립, 응답 필드와 스트림 알림(같은 값은 알리지 않음), 기존 필드 목록에 `name` (STests/DeviceNameApiTests.cs, STests/AdminDeviceEndpointTests.cs, STests/DeviceStatusApiTests.cs) — C10, C11, C12, C13, C20

체크포인트: `dotnet test --solution Biz.Bizadm.PowerManager.slnx` 통과

## 단계 2: 클라이언트 장비명 자료
- [x] T007 `DeviceStatus.name`, `saveName`(성공 뒤 `deviceRenamed`), slice의 `deviceRenamed` (Client/features/device-status/types.ts, deviceStatusApi.ts, deviceStatusSlice.ts) — C13, C15
- [x] T008 `displayName`, `sortByName`(`sortByMac` 대체), `deviceLabel` 삭제와 테스트 (Client/features/device-status/format.ts, format.test.ts) — C16, C17, C20
- [x] T009 [P] 화면의 장비명 공백 제거와 검사, 테스트 (Client/features/device-control/deviceName.ts, deviceName.test.ts) — C15, C20
- [x] T010 [P] 입력 이름·설명·`isB540`, 테스트 (Client/features/device-control/fields.ts, fields.test.ts) — C5, C6, C20
- [x] T011 [P] shadcn CLI로 `dialog` 추가. 받지 못하거나 새 의존성이 생기면 멈추고 묻는다. 필요하면 React 18 ref를 보정한다 (Client/components/ui/dialog.tsx) — C15

체크포인트: `format.test.ts`·`deviceName.test.ts`·`fields.test.ts` 통과, `package.json`·`package-lock.json`에 변경 없음

## 단계 3: 장비명 표시
- [x] T012 `DeviceLabel`·`DeviceSubtitle`와 확인 창의 장비 표시(mac 고정폭, 긴 이름 줄바꿈), 테스트 원소에 `name` (Client/features/device-status/DeviceLabel.tsx, ConfirmDialog.tsx, RelayControl.test.tsx) — C16, C19, C20
- [x] T013 카드의 이름·아래 줄·긴 이름 줄바꿈과 카드 순서, 테스트 (Client/features/dashboard/DashboardPage.tsx, DeviceCard.tsx, DashboardPage.test.tsx) — C16, C17, C19, C20
- [x] T014 장비 목록의 "이름" 열·검색·자리 표시글·표 최소 폭 840px·열 때마다 다시 읽기, 테스트. 필요하면 App 테스트 원소에 `name` (Client/features/devices/devicesApi.ts, deviceList.ts, deviceList.test.ts, DevicesPage.tsx, DevicesPage.test.tsx, Client/App.test.tsx) — C13, C18, C20

체크포인트: `DashboardPage.test.tsx`·`RelayControl.test.tsx`·`DevicesPage.test.tsx`·`deviceList.test.ts`·`App.test.tsx` 통과

## 단계 4: 장비 제어 화면
- [x] T015 `Field`의 설명·비활성, `Section`의 불러오는 중 안내(성공 안내 동안 화면에서만 숨김)와 `aria-busy`, 전원 묶음의 이름·설명과 옛 안내 삭제 (Client/features/device-control/PowerSection.tsx) — C1, C5, C6, C7, C8
- [x] T016 설정 묶음 셋: 응답 전 비활성, 준비된 첫 렌더에서 한 번 채우기, 이름·설명, 옛 안내 삭제. 예약은 준비 전 삭제 성공 뒤 채우지 않기와 "삭제" 활성 유지 (Client/features/device-control/ReportConditionForm.tsx, StandbyCutoffForm.tsx, ScheduleForm.tsx) — C1, C2, C4, C5, C6, C7, C8
- [x] T017 이름 변경 창(Enter 저장, 보내는 중 중복 제출 무시, 실패 이유, 긴 이름 줄바꿈) (Client/features/device-control/DeviceNameDialog.tsx) — C15, C16, C19
- [x] T018 설정 조회를 `DeviceControl`로 올리고 `SettingsSections` 삭제, 묶음 위의 오류 안내와 다시 시도, 머리의 장비명·"이름 변경" (Client/features/device-control/DeviceControlPage.tsx) — C1, C2, C3, C14, C15, C19
- [x] T019 화면 테스트: 기존 테스트를 새 이름·준비 완료에 맞추고, 로딩(응답 전·후·실패·다시 시도·다시 시도 중, 삭제가 응답보다 먼저·나중, 성공 안내 동안의 숨김), 모델별 이름·설명과 설명 연결, 없앤 안내, 머리·이름 변경 창(오프라인·취소·Esc·검사·저장 중·성공·실패·스트림 끊김·중복 제출) (Client/features/device-control/DeviceControlPage.test.tsx) — C1, C2, C3, C4, C5, C6, C7, C8, C14, C15, C16, C20

체크포인트: PowerShell에서 `npm test`·`npm run lint`·`npm run build` 통과

## 단계 5: 화면 확인
- [x] T020 빌드한 화면을 헤드리스 Edge(임시 프로필)와 가짜 API 응답으로 확인: 1440px·800px·390px의 설정 응답 전·후 네 묶음 좌표(390px은 응답 전 예약 삭제 성공 포함), 390px에서 공백 없는 30자 이름의 대시보드·제어 화면·확인 창·이름 변경 창. 확인용 파일은 스크래치에만 두고, 어긋나면 해당 작업의 파일을 고친다 — C2, C19

체크포인트: 좌표 차이 0, 390px `scrollWidth` 390 이하

## 단계 6: 문서
- [x] T021 [P] 최소·최대 간격과 지연의 관찰을 근거 문서에 적고 어긋나는 문장을 맞춘다. mac을 적지 않는다 (docs/reference/mqtt-device-control.md, docs/reference/b540-mqtt-topic.md) — C9
- [x] T022 [P] 기준 spec device-control 반영: R5·R6·R16~R21의 관련 요구, 장비명 저장 API, 로딩, 이력, `파일:` 줄 (docs/features/device-control.md) — C21
- [x] T023 [P] 기준 spec device-dashboard 반영: R4·R6·R9, 이력, `파일:` 줄 (docs/features/device-dashboard.md) — C21
- [x] T024 [P] 기준 spec device-list 반영: R1·R5·R8·R10·R13, 이력 (docs/features/device-list.md) — C21
- [x] T025 [P] 기준 spec device-register 반영: R5·R6의 장비명 보관, 이력, `파일:` 줄 (docs/features/device-register.md) — C21
- [x] T026 [P] 기준 spec admin-ui 반영: R4(`dialog`)·R5(입력 묶음의 비활성 틀), 이력, `파일:` 줄 (docs/features/admin-ui.md) — C21

체크포인트: 기준 spec 다섯 개의 `파일:` 줄 경로가 모두 있고, 근거 문서에 12자리 16진수 mac이 없음

## 의존 관계
- T002 → T003 → T006: 마이그레이션은 모델 변경 뒤에 만들고 테스트가 쓴다
- T001, T002 → T004 → T005 → T006: 저장은 검사와 열에, API는 저장에 기댄다
- T007, T008 → T012, T013: 표시가 `name`과 순서 함수를 쓴다. T008이 `deviceLabel`·`sortByMac`을 지우므로 T012·T013까지 마쳐야 전체 빌드가 된다
- T010 → T015, T016: 이름과 설명을 쓴다. T015 → T016: 묶음이 `Field`·`Section`을 쓴다
- T009, T011, T007, T012 → T017 → T018 → T019
- T016 → T018: 묶음의 속성이 바뀐 뒤 화면이 넘긴다
- 단계 5는 단계 4 뒤, 단계 6은 구현이 굳은 뒤(T021은 앞서 해도 된다)
- T014는 단계 2의 다른 작업에 기대지 않는다
