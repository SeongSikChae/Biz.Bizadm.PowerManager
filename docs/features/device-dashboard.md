# device-dashboard

파일: src/Biz.Bizadm.PowerManager.Client/src/features/dashboard/DashboardPage.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/dashboard/DeviceCard.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/dashboard/DashboardPage.test.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-status/types.ts, src/Biz.Bizadm.PowerManager.Client/src/features/device-status/deviceStatusApi.ts, src/Biz.Bizadm.PowerManager.Client/src/features/device-status/deviceStatusSlice.ts, src/Biz.Bizadm.PowerManager.Client/src/features/device-status/useDeviceStatusStream.ts, src/Biz.Bizadm.PowerManager.Client/src/features/device-status/format.ts, src/Biz.Bizadm.PowerManager.Client/src/features/device-status/format.test.ts, src/Biz.Bizadm.PowerManager.Client/src/features/device-status/RelayControl.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-status/RelayControl.test.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-status/ConfirmDialog.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-status/DeviceLabel.tsx, src/Biz.Bizadm.PowerManager.Client/src/test/setup.ts

관리 화면의 첫 화면(`/`). 등록된 장비 전체의 온라인 여부, 릴레이 상태, 계량 값을 실시간으로 보이고 그 자리에서 릴레이를 켜고 끈다. 상태 조회·상태 스트림·제어 API와 제어 가능 여부는 device-control 기능(`docs/features/device-control.md`), 화면 틀·디자인 기준·메뉴는 admin-ui 기능(`docs/features/admin-ui.md`)을 따른다. 화면은 2026-11-22 디자인 시안(저장소 밖)의 "A. 카드"안으로 확정했다. 릴레이 상태는 장치가 마지막으로 알린 값이며, 장치가 알리지 않는 변경(예약으로 끄는 순간 등)은 반영되지 않을 수 있다(근거 `docs/reference/mqtt-device-control.md` 3.3·3.4).

## 요구(완료 조건)

- R1. 대시보드의 경로는 `/`다. 주소를 직접 열거나 새로 고쳐도 열리고 브라우저 뒤로 가기가 동작한다. 상태 스트림(device-control R6)은 앱 전체에서 한 번만 열어 대시보드와 장비 제어 화면이 함께 쓴다.
- R2. 온라인·오프라인은 색 점(온라인은 상태용 색 토큰 `--success`)과 함께 글자("온라인"·"오프라인")로도 보인다.
- R3. 본문 머리에 제목 "대시보드", 요약("온라인 N대 · 오프라인 N대 · 합계 X W". 합계는 온라인 장비의 전력 합, 소수 첫째 자리), 실시간 연결 상태("실시간 연결됨"·"연결 끊김 — 다시 연결 중")를 보인다.
- R4. 등록된 장비마다 카드 하나를, 장비명이 있는 장비를 장비명 오름차순(한국어 순서, 이름 안의 숫자는 수로 비교, 같으면 mac 오름차순)으로 먼저, 장비명이 없는 장비를 mac 오름차순으로 뒤에 보인다. 카드에는 장비명이 있으면 제목에 장비명과 그 아래에 모델과 mac(고정폭), 없으면 제목에 모델과 그 아래에 mac(고정폭), 온라인 여부, 전력(크게, 소수 둘째 자리, 단위 W), 전압(소수 둘째 자리, V)·전류(소수 셋째 자리, A)·역률(소수 셋째 자리), 릴레이 제어(R6), "제어" 버튼(장비 제어 화면으로), 마지막 보고의 상대 시간(device-list R6과 같은 규칙, 없으면 "보고 없음")이 있다. 값이 없으면 "—"다. 오프라인 장비의 계량 값은 흐리게 보인다.
- R5. 상태 스트림으로 받은 변경은 새로 고침 없이 카드(장비명과 순서 포함)와 요약에 반영된다. 스트림이 끊기면 연결 상태를 "연결 끊김 — 다시 연결 중"으로 바꾸고 마지막으로 받은 값을 그대로 보이며, 다시 연결되면 전체 상태를 다시 받아 맞춘다. 브라우저가 스스로 다시 연결하지 않는 끊김(`EventSource`가 `CLOSED`)이면 3초 뒤 스트림을 새로 연다.
- R6. 릴레이 제어는 릴레이 상태에 따라 모양이 다르고, 제어 가능한(device-control R5) 장비에서만 누를 수 있다.
  - 상태를 알 때: 스위치와 상태 글자("켜짐"·"꺼짐")를 보인다. 꺼진 스위치를 누르면 바로 켜기를 보낸다. 켜진 스위치를 누르면 장비를 밝힌 확인 창(장비명이 있으면 `장비명 — 모델 (mac)`, 없으면 `모델 (mac)`, mac은 고정폭)이 뜨고, "끄기"를 누르면 끄기를 보내며 "취소"나 Esc는 아무것도 보내지 않는다.
  - 상태를 모를 때: "상태 모름" 글자와 "켜기"·"끄기" 버튼을 보인다. "켜기"는 바로 켜기를 보내고, "끄기"는 위와 같은 확인 창을 거친다. 릴레이 상태를 알게 되면 스위치로 바뀐다.
- R7. 켜기·끄기를 보낸 뒤 그 장비의 릴레이 상태가 보낸 값으로 바뀐 것을 스트림으로 받을 때까지 릴레이 제어는 진행 중 표시와 함께 비활성이다. API가 실패하거나 10초 안에 상태가 바뀌지 않으면 릴레이 제어를 장비의 마지막 상태로 되돌리고, 그 카드에 원인을 담은 오류 안내를 보인다.
- R8. 처음 불러오는 동안은 카드 모양 스켈레톤, 실패하면 오류 안내와 "다시 시도", 등록된 장비가 없으면 빈 목록 안내를 보인다(admin-ui R5). 폭 390px에서 카드는 한 줄로 쌓이고, 공백 없는 30자 장비명이어도 카드와 확인 창 밖으로 넘치지 않으며 페이지 가로 스크롤이 생기지 않는다.
- R9. 클라이언트 테스트(`DashboardPage.test.tsx`, `RelayControl.test.tsx`, `format.test.ts`, `App.test.tsx`)가 R1~R8(R2의 색과 R8의 390px 배치 제외)을 확인하고 통과한다.

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-22 | device-control | R1~R9 처음 작성(대시보드 신설, #28) | docs/specs/device-control/ |
| 2026-11-22 | device-name-and-units | R4 바꿈(장비명과 카드 순서), R5 바꿈(장비명 반영), R6 바꿈(확인 창의 장비명과 고정폭 mac), R8 바꿈(긴 장비명. #32) | docs/specs/device-name-and-units/ |
