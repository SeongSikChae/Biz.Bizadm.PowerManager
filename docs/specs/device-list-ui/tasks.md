# 장비 목록 화면 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

클라이언트 경로는 `src/Biz.Bizadm.PowerManager.Client/` 기준이다.

## 단계 1: 서버 목록 API

- [x] T001 관리 포트가 아닌 로컬 포트에서 처리되는 요청을 404 ProblemDetails로 끝내는 리소스 필터 attribute (src/Biz.Bizadm.PowerManager.Server.Spa/Admin/AdminPortOnlyAttribute.cs) — C3
- [x] T002 [P] 응답 record, 필드 여섯 개 (src/Biz.Bizadm.PowerManager.Server.Spa/Devices/AdminDeviceResponse.cs) — C1
- [x] T003 `[AdminPortOnly]` `GET /api/admin/devices`, `LastRequestedAt` 내림차순·`Mac` 오름차순 정렬 (src/Biz.Bizadm.PowerManager.Server.Spa/Devices/AdminDevicesController.cs) — C1, C2, C3
- [x] T004 목록 API 테스트: DB 직접 삽입으로 시각 지정, 관리 포트(80·443) 200·`application/json`·필드 여섯 개·MQTT 키 없음·정렬·동점·ISO 8601·빈 배열, 장치 포트(18446·18443) 404 `application/problem+json`·본문에 mac 없음, `LocalPort` 80·443·18446·18443 `DataRow`로 register·connect 200 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/AdminDeviceEndpointTests.cs) — C1, C2, C3, C15

체크포인트: `dotnet test tests/Biz.Bizadm.PowerManager.Server.Spa.Tests` 전체 통과(기존 `DeviceEndpointTests` 포함).

## 단계 2: 클라이언트 기반

- [x] T005 `@fontsource-variable/noto-sans-kr` 설치, `index.css`에 import와 `--font-sans` 한글 대체 글꼴 (package.json, package-lock.json, src/index.css) — C12
- [x] T006 `npx shadcn add button input table skeleton alert` (src/components/ui/button.tsx, input.tsx, table.tsx, skeleton.tsx, alert.tsx, package.json, package-lock.json) — C4, C5, C10, C13
- [x] T007 RTK Query `devicesApi`(`baseUrl` = `${window.location.origin}/api/admin`, `getDevices`)와 store 등록, 자리 표시 reducer 제거, `makeStore()` 내보내기 (src/features/devices/devicesApi.ts, src/app/store.ts) — C10

체크포인트: `npm run build` 통과.

## 단계 3: 목록 로직

- [x] T008 [P] 순수 함수: 빈 값 "—", 절대 시각 `YYYY-MM-DD HH:mm`, 상대 시간 경계, 정렬(기본·첫 클릭 내림차순·토글·동점 MAC 오름차순), 검색(대소문자 무시, MAC의 `:`·`-` 무시), 50대 페이지 자르기와 페이지 보정 (src/features/devices/deviceList.ts) — C5, C6, C7, C8, C9
- [x] T009 순수 함수 경계값 테스트 (src/features/devices/deviceList.test.ts) — C5, C6, C7, C8, C9, C15

체크포인트: `npm test` 중 `deviceList.test.ts` 통과.

## 단계 4: 화면

- [x] T010 앱 틀: 사이드바(제품명 "PowerManager", "장비" `aria-current="page"`)와 본문, 바깥 컨테이너에 `flex-wrap`, 본문에 flex-basis와 `min-w-0`을 줘 390px에서 사이드바가 위로 쌓이고 페이지 가로 스크롤 없음 (src/components/AppShell.tsx) — C4, C11, C13
- [x] T011 장비 목록 화면: 머리(제목·대수·새로고침), 레이블 있는 검색, 표(MAC 고정폭, 정렬 가능한 열 머리 버튼과 `aria-sort`), 표를 감싼 가로 스크롤 상자, 페이지 이동, `data`/`isFetching`/`error !== undefined` 기준 상태 화면(스켈레톤·빈 목록·검색 결과 없음과 지우기·목록 자리 오류·화면 위 오류·버튼 비활성) (src/features/devices/DevicesPage.tsx) — C4, C5, C6, C7, C8, C9, C10, C11, C13
- [x] T012 `AppShell` 안에 `DevicesPage` (src/App.tsx) — C4
- [x] T013 화면 테스트(`fetch` 모의, `makeStore()`): 앱 틀, 머리 문구·대수(검색으로 행이 줄어도 전체 대수 유지), 표 열 순서와 MAC 셀이 서버 값 그대로(소문자 12자리)·빈 값 "—", 처음 불러올 때 스켈레톤·대수 숨김, 빈 목록, 처음 실패와 다시 시도 중 스켈레톤, 새로고침 중 기존 표 유지·버튼 비활성, 새로고침 실패 시 화면 위 오류, 실패 뒤 다시 불러오는 동안 오류 유지(지연 응답), 성공 뒤 해제, 다시 불러온 뒤(성공·실패 모두) 검색어·정렬·현재 페이지 유지, 성공한 새 목록이 줄어 현재 페이지가 전체 페이지 수를 넘으면 마지막 페이지, `aria-sort`·정렬 토글, 검색·결과 없음·지우기, 페이지 버튼·"현재 / 전체 페이지"·검색·정렬 변경 시 첫 페이지, 레이블·`aria-label`, 키보드 조작 (src/App.test.tsx, src/features/devices/DevicesPage.test.tsx) — C4, C5, C7, C8, C9, C10, C13, C15

체크포인트: `npm test`, `npm run build`, `npm run lint` 모두 통과.

## 단계 5: 기준 spec 반영

- [x] T014 [P] device-list 기준 spec 새로 작성: `파일:` 줄(T002·T003·T004·T007·T008·T009·T011·T013의 파일), 요구 R1~(C1~C13), 이력 한 줄 (docs/features/device-list.md) — C14
- [x] T015 [P] admin-ui 기준 spec 새로 작성: `파일:` 줄(T001·T005·T006·T010·T012의 파일), 요구(테마 토큰 출처 `src/index.css`, 글꼴, 앱 틀 구조, 표·버튼·상태 화면 규칙, 관리 API는 관리 포트에서만, 시안 링크), 이력 한 줄 (docs/features/admin-ui.md) — C14

체크포인트: 두 파일을 읽어 C14 항목이 모두 들어 있는지 확인.

## 단계 6: 실행 확인

- [x] T016 스크래치에 임시 설정(높은 포트, 임시 DB·KMS)으로 서버 실행, 등록 API로 장비 60대 넣기, `SERVER_URL`로 Vite 개발 서버 실행, 내장 브라우저로 1440px·390px에서 C4~C13 확인(한글 글꼴 계산값, 키보드·포커스 표시 포함), 실제 Kestrel 장치 포트로 목록 요청 시 404 확인, 결과를 작업 문서에 기록 (작업 대상 파일 변경 없음) — C3, C11, C12, C13, C16
- [x] T017 전체 자동 검증 다시 실행: `dotnet test`(Server.Spa.Tests), 클라이언트 `npm test`·`npm run build`·`npm run lint` (작업 대상 파일 변경 없음) — C15

체크포인트: T016 기록과 T017 결과가 모두 통과.

## 의존 관계

- 단계 1(서버)과 단계 2~4(클라이언트)는 서로 의존하지 않는다. 클라이언트 테스트는 `fetch` 모의를 쓴다.
- T003은 T001·T002에 의존한다. T004는 T003에 의존한다.
- T005와 T006은 같은 `package.json`·`package-lock.json`을 고치므로 차례로 한다.
- T008은 다른 작업에 의존하지 않는다. T009는 T008에 의존한다.
- T011은 T006(UI 부품)·T007(API)·T008(로직)에 의존한다. T010은 T006(UI 부품)에 의존한다. T012는 T010·T011에, T013은 T012에 의존한다.
- 단계 5는 단계 1~4의 파일 목록이 정해진 뒤 한다(마지막 구현 단계).
- 단계 6은 단계 1~5가 끝난 뒤 한다. T016은 단계 1과 단계 4가 모두 있어야 한다.
