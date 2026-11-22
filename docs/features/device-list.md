# device-list

파일: src/Biz.Bizadm.PowerManager.Server.Spa/Devices/AdminDeviceResponse.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Devices/AdminDevicesController.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/AdminDeviceEndpointTests.cs, src/Biz.Bizadm.PowerManager.Client/src/features/devices/devicesApi.ts, src/Biz.Bizadm.PowerManager.Client/src/app/store.ts, src/Biz.Bizadm.PowerManager.Client/src/features/devices/deviceList.ts, src/Biz.Bizadm.PowerManager.Client/src/features/devices/deviceList.test.ts, src/Biz.Bizadm.PowerManager.Client/src/features/devices/DevicesPage.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/devices/DevicesPage.test.tsx, src/Biz.Bizadm.PowerManager.Client/src/App.test.tsx

관리 화면의 장비 목록. 서버의 관리용 목록 API(`GET /api/admin/devices`)와 클라이언트 장비 목록 화면(관리 화면 `/devices`)이다. 화면 틀·디자인 기준과 관리 포트 제한은 admin-ui 기능(`docs/features/admin-ui.md`)을 따른다.

## 요구(완료 조건)

서버:

- R1. 관리 포트로 `GET /api/admin/devices`를 하면 200과 `application/json` 배열을 반환한다. 원소의 필드는 `mac`, `userId`, `modelId`, `company`, `registeredAt`, `lastRequestedAt` 여섯 개뿐이며, 시각은 오프셋이 붙은 ISO 8601 문자열이다. MQTT 키는 어떤 이름으로도 응답에 없다.
- R2. 배열은 저장된 장비 전체이며 `lastRequestedAt`이 최근인 순서다(같으면 `mac` 오름차순). 등록된 장비가 없으면 빈 배열이다.
- R3. 장치 포트(`DeviceHttpPort`·`DeviceHttpsPort`, HTTP·HTTPS 모두)에서 처리되는 목록 요청은 404 ProblemDetails를 반환하고 장비 정보를 내주지 않는다(admin-ui R6). 기존 장치 endpoint(register·connect)의 동작은 두 포트 모두에서 바뀌지 않는다.

화면:

- R4. 장비 목록 화면(`/devices`)은 앱 틀(admin-ui R3: 1440px 나란히, 390px 사이드바 위로 쌓임) 안에 장비 목록을 보이며, 글꼴은 admin-ui R2(한글 Noto Sans KR 웹 글꼴 포함)를 따른다. 본문 머리에는 제목 "장비", 등록 대수("서버에 등록된 장비 N대"), "새로고침" 버튼이 있다. N은 검색과 관계없이 서버가 준 전체 대수이며, 받은 목록이 아직 없을 때(R10)는 표시하지 않는다.
- R5. 표의 열은 MAC, 모델, 제조사, 사용자, 등록, 마지막 요청 순서다. MAC은 고정폭 글꼴로 서버 값 그대로(소문자 12자리) 표시하고, 빈 값(모델·제조사·사용자)은 "—"로 표시한다.
- R6. 등록·마지막 요청 시각은 브라우저 시간대의 `YYYY-MM-DD HH:mm`이다. 마지막 요청은 그 위에 상대 시간을 함께 보이며, 1분 미만이거나 미래 시각이면 "방금", 60분 미만 "N분 전", 24시간 미만 "N시간 전", 그 이상 "N일 전"이다(N은 내림).
- R7. 기본 정렬은 마지막 요청 최근 순이다. 등록·마지막 요청 열 머리를 누르면 그 열의 최근 순(내림차순)으로, 같은 열을 다시 누르면 방향이 바뀐다. 값이 같으면 MAC 오름차순이다. 정렬 중인 열과 방향을 표시한다(`aria-sort` 포함).
- R8. 검색은 MAC·모델·사용자를 대소문자 구분 없이 부분 일치로 거른다. MAC 검색은 `:`·`-`를 무시한다. 결과가 없으면 "검색 결과 없음"과 "검색어 지우기" 버튼을 보인다.
- R9. 한 페이지에 50대까지 보이고, 넘으면 이전·다음 페이지 버튼과 "현재 / 전체 페이지"를 보인다. 검색어나 정렬이 바뀌면 첫 페이지로 돌아간다.
- R10. 화면은 서버에서 받은 목록이 있는지(빈 목록 포함)로 정한다. "새로고침"과 "다시 시도"는 목록을 다시 불러온다. 받은 목록이 있으면 비었을 때 빈 목록 안내("등록된 장비가 없습니다"), 아니면 표를 보인다.
  - 받은 목록이 아직 없을 때: 불러오는 동안 행 모양 스켈레톤, 실패하면 목록 자리에 오류 안내와 "다시 시도".
  - 받은 목록이 있을 때: 다시 불러오는 동안 기존 화면·등록 대수·이미 떠 있는 오류 안내를 그대로 두고 "새로고침"·"다시 시도"를 비활성화한다. 실패하면 기존 화면·검색어·정렬·페이지를 두고 그 위에 오류 안내와 "다시 시도"를 보인다. 성공하면 오류 안내를 없애고 검색어·정렬·현재 페이지를 유지한다(현재 페이지가 전체 페이지 수를 넘으면 마지막 페이지).
- R11. 폭 390px에서 표는 테두리 상자 안에서 가로로 스크롤되고 페이지 전체에는 가로 스크롤이 생기지 않는다.
- R12. 검색 입력에 레이블이 있고, 아이콘만 있는 버튼에 `aria-label`이 있으며, 정렬·새로고침·검색·페이지 이동을 키보드로 할 수 있고 포커스 표시가 보인다.
- R13. 서버 테스트(`AdminDeviceEndpointTests`)와 클라이언트 테스트(`deviceList.test.ts`, `DevicesPage.test.tsx`, `App.test.tsx`)가 R1~R10·R12를 확인하고 통과한다.

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-19 | device-list-ui | R1~R13 처음 작성(장비 목록 API·화면 신설, #17) | docs/specs/device-list-ui/ |
| 2026-11-22 | device-control | R4 바꿈(화면 경로 `/`에서 `/devices`로) | docs/specs/device-control/ |
