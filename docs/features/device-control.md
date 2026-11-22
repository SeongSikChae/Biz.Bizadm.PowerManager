# device-control

파일: src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceStateStore.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceRelayReader.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceStatusService.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceStatusResponse.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceSettingsModels.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceCommandService.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Devices/AdminDeviceStatusController.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Devices/AdminDeviceControlController.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DeviceStateTests.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DeviceStatusApiTests.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DeviceControlApiTests.cs, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/DeviceControlPage.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/PowerSection.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/ReportConditionForm.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/StandbyCutoffForm.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/ScheduleForm.tsx, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/validation.ts, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/validation.test.ts, src/Biz.Bizadm.PowerManager.Client/src/features/device-control/DeviceControlPage.test.tsx

장비 상태 보관과 제어. 서버는 장비별 최신 상태와 마지막으로 보낸 설정을 메모리에 두고, 관리 포트의 상태 조회·상태 스트림(SSE)·설정 조회·제어 API를 내준다. 장비 제어 화면(`/devices/{mac}`)은 장비 한 대의 릴레이, 전원 재투입, 재부팅, 보고 조건, 대기전력 차단, 예약 전원 재투입을 제어한다. 제어 메시지의 오퍼레이션·리소스·값의 근거는 `docs/reference/mqtt-device-control.md`(아래 "근거 문서"), 브로커 쪽(접속 추적, 제어 메시지 발행)은 mqtt-broker 기능, 대시보드는 device-dashboard 기능이다. 관리 API에는 인증이 없고 관리 포트 제한만 둔다(admin-ui R6).

서버를 다시 시작하면 상태와 설정 값이 사라진다. 계량 값은 다음 장치 보고로, 릴레이 상태는 서버가 보내는 읽기(R4)로 다시 채우고, 설정은 빈 값으로 보이며 장치에 남은 설정은 그대로다. 설정 값의 보관과 장치에서 읽기는 GitHub #29에서 다룬다.

용어: **mac**은 소문자 12자리 16진수다. **온라인**은 그 mac의 MQTT 클라이언트(ClientId `DAWONDNS-{mac}`)가 브로커에 접속해 있는 것이다. **제어 메시지**는 서버 → 장치 토픽(mqtt-broker R2)에 retain 없이 보내는 `{"sid":"<문자열>","msg":{"o":"<op>","e":[{"n":"<경로>","sv":"<값>"}]}}`이다. **장비의 메시지**는 장치 → 서버 토픽으로 온 메시지 가운데, 토픽 장치 키를 `-`로 나눈 요소 가운데 보낸 클라이언트 ClientId의 mac과 같은 것(대소문자 무시)이 있는 것이다. 그런 요소가 없거나 ClientId에서 mac을 읽을 수 없는 메시지는 Warning 로그를 남기고 상태에 반영하지 않는다(기존 분배와 로그는 그대로다).

## 요구(완료 조건)

장비 상태(서버):

- R1. 서버는 장비의 메시지 가운데 상태 보고(`msg.o`가 `notify` 또는 `n`)의 요소에서 장비별 최신 상태를 만든다. `/100/0/30` 또는 `/100/0/31`의 `true`·`false`는 릴레이 켜짐·꺼짐, `/100/0/1`·`/100/0/2`·`/100/0/3`·`/100/0/11`은 전압(V)·전류(A)·역률·전력(W)이다. 보고에 없는 항목은 이전 값을 유지하고, 숫자로 해석할 수 없는 계량 값은 무시한다. 상태 보고를 받을 때마다 마지막 보고 시각을 그 시각으로 바꾼다.
- R2. 서버는 장비별 온라인 여부를 브로커의 접속·끊김에 맞춰 바꾼다. 같은 ClientId가 다시 접속해 이전 연결이 끊긴 경우에도 온라인으로 남는다.
- R3. 서버는 장비의 메시지(오퍼레이션과 관계없이)의 토픽에서 그 장비의 토픽 접두와 장치 키를 기억하고, 제어 메시지를 이 접두·장치 키로 만든 토픽에 보낸다.
- R4. 서버는 장비의 메시지를 받았을 때, 그 장비의 릴레이 상태를 모르고 이번 접속에서 읽기를 그 장비 클라이언트의 전송 큐에 넣은 적이 없으면, 제어 메시지 `o` `r`, 요소 `/100/0/30`(값 빈 문자열)을 보낸다. 큐에 넣었으면 응답이 없어도 그 접속 동안 다시 보내지 않고, 넣지 못했으면 다음 장비의 메시지 때 다시 시도한다. 한 메시지 안에서는 R3, R1, R4 순서로 처리해 릴레이 요소가 든 상태 보고에는 읽기를 보내지 않는다.
- R5. 관리 포트의 `GET /api/admin/devices/status`는 등록된 장비 전체를 mac 오름차순 배열로 반환한다. 원소의 필드는 `mac`, `modelId`, `online`, `relay`(`on`·`off`·`unknown`), `voltage`, `current`, `powerFactor`, `power`(숫자 또는 null), `lastReportAt`(ISO 8601 또는 null), `controllable`(온라인이고 R3의 토픽 정보가 있음)이다. MQTT 키는 어떤 이름으로도 없다. 서버 시작 뒤 보고가 없던 장비는 `relay` `unknown`, 계량 값·`lastReportAt` null이다.
- R6. 관리 포트의 `GET /api/admin/devices/status/stream`은 `text/event-stream`으로 응답한다. 연결하면 먼저 `snapshot` 이벤트로 R5와 같은 배열을 보내고, 그 뒤 어느 장비의 R5 원소가 바뀔 때마다(상태 보고, 온라인 변경, 토픽 정보를 처음 알게 됨, 장비 등록 등) `device` 이벤트로 그 장비의 원소를 보낸다. 클라이언트가 연결을 끊거나 서버가 종료하면 스트림이 끝난다.

제어(서버):

- R7. 관리 포트의 제어 API(`/api/admin/devices/{mac}/...`)는 mac으로 장비를 가리킨다. 실패는 ProblemDetails로 반환하고 제어 메시지를 보내지 않으며, 판정 순서는 mac 형식(400) → 등록(404) → 본문 범위·모델(400) → 제어 가능(409) → 전송 큐(409)다. 브로커가 그 장비의 클라이언트(ClientId `DAWONDNS-{mac}`)의 전송 큐에 메시지를 넣었으면 204다. 성공은 장치가 메시지를 받았거나 처리했다는 뜻이 아니다.
- R8. 릴레이(`POST .../relay`)는 `o` `e`, 켜기 `/100/0/31` `"true"`, 끄기 `/100/0/31` `"false"` 하나를 보낸다.
- R9. 전원 재투입(`POST .../power-cycle`)은 `o` `e`, 요소 `/100/0/31` `"false"`와 `/100/0/68` `"<N>"`을 이 순서로 보낸다. N은 1~999의 정수다(단위는 B540 `1.01.36`에서 약 1초, M130-W는 미확인. 근거 문서 3.1).
- R10. 재부팅(`POST .../reboot`)은 `o` `reb`, 빈 요소 배열을 보낸다. `modelId`가 `B540`으로 시작하는 장비만 받고, 그 밖의 모델은 400이다(M130-W는 `reb`를 읽기로 처리하는 것으로 추정된다. 근거 문서 4.1).
- R11. 보고 조건(`PUT .../report-condition`)은 `o` `w`, 요소 `/99/0/2`·`/99/0/3`·`/99/0/4`에 최소 간격·최대 간격·변화 기준을 10진 정수 문자열로 보낸다. 범위는 최소 간격 1~3600, 최대 간격 11~14400, 변화 기준 1~100이고, 최소 간격이 최대 간격보다 크면 400이다.
- R12. 대기전력 차단(`PUT .../standby-cutoff`)은 `o` `w`, 요소 `/100/0/40`에 `"true"`·`"false"`, `/100/0/41`에 기준 전력(0.00~9999.99, 소수 둘째 자리까지 채운 10진 문자열), `/100/0/42`에 지연(0~9999의 10진 정수 문자열)을 보낸다. 이 범위는 서버가 정한 입력 제한이며, 리소스의 의미는 추정이고 배율·지연 단위·펌웨어가 받아들이는 범위는 미확인이다(근거 문서 3.2). 실장비 B540 `1.01.36`은 기준 전력 `1.00`에 `1.10`으로 응답했다(보낸 값의 1.1배가 저장되는 것일 수 있음, 추정). 차단으로 끌 때는 릴레이 상태를 알린다(근거 문서 3.4).
- R13. 예약 전원 재투입(`PUT .../schedule`)은 `o` `w`, 요소 `/100/0/58` 하나에 `"1,U,<사용>,0,<요일>,<HH:MM:SS>,F,<N>"`을 보낸다. 예약은 장비마다 하나다. 사용은 `T`·`F`, 요일은 7글자이며 첫 글자는 늘 `0`, 둘째 글자는 월요일을 고르면 1과 일요일을 고르면 2를 더한 숫자(`0`~`3`), 셋째~일곱째 글자는 화·수·목·금·토 순서의 `0`·`1`이다(장치가 첫 글자를 저장하지 않으므로. 근거 문서 3.3). 요일을 하나 이상 골라야 하고, N(다시 켤 때까지의 초로 추정)은 1 이상이며 시각의 초와 N의 합이 60 미만이어야 하고, 아니면 400이다. 삭제(`DELETE .../schedule`)는 서버가 보낸 예약이 있는지와 상관없이 받고 `"1,D,F,0,1111111,00:00:00,F,0"`을 보낸다.
- R14. 서버는 장비마다 R11·R12·R13으로 마지막에 보낸 값(성공한 것만)을 기억하고 `GET /api/admin/devices/{mac}/settings`로 돌려준다. 보낸 적이 없는 항목은 null이고, 예약 삭제가 성공하면 예약은 null이 된다. mac 형식이 틀리면 400, 등록되지 않은 mac이면 404다.
- R15. R5·R6·R7·R14의 API는 장치 포트에서 404 ProblemDetails를 반환하고 장비 정보를 내주지 않으며 제어 메시지를 보내지 않는다(admin-ui R6).

장비 제어 화면:

- R16. 경로는 `/devices/{mac}`이다. 머리에 "← 대시보드" 링크, 모델과 mac, 온라인 여부, 전력, 마지막 보고 상대 시간을 보이고 스트림으로 실시간 반영한다. 등록되지 않은 mac이면 "장비를 찾을 수 없습니다" 안내와 대시보드 링크를 보인다.
- R17. "전원" 묶음에 릴레이 제어(device-dashboard R6·R7), 전원 재투입(값 입력 1~999, 기본 10, "실행"), 재부팅 버튼이 있다. 묶음 안내로 "전원 재투입 단위는 약 1초입니다(B540에서 확인)"를 보인다. 전원 재투입과 재부팅은 장비를 밝힌 확인 창을 거친다. 재부팅은 `modelId`가 `B540`으로 시작하는 장비에서만 보인다.
- R18. 설정 묶음은 "보고 조건"(최소 간격·최대 간격·변화 기준), "대기전력 차단"(사용 스위치, 기준 전력, 단위를 적지 않은 지연, "값의 의미와 단위는 추정입니다" 안내), "예약 전원 재투입"(사용 스위치, 요일 일~토 하나 이상, 시·분·초, 다시 켤 때까지의 초, "저장"·"삭제")이다. 예약 "삭제"는 서버가 보낸 예약이 있는지와 상관없이 제어 가능한 장비면 누를 수 있고 확인 창을 거친다. 세 묶음은 서버가 마지막으로 보낸 값(R14)으로 채우고, 보낸 적이 없으면 비워 두며, 장비의 현재 값과 다를 수 있다는 안내를 보인다.
- R19. 입력이 R9·R11·R12·R13의 범위를 벗어나면 보내지 않고 그 입력 옆에 이유를 보인다. 보내는 동안 그 묶음의 버튼은 비활성이다. 성공하면 그 묶음에 "장비에 보냈습니다" 안내를, 실패하면 원인을 담은 오류 안내를 보인다. 제어 가능하지 않은 장비(R5)에서는 모든 제어 버튼이 비활성이고 그 까닭("장비가 오프라인입니다" 등)을 보인다.
- R21. 네 묶음(R17·R18)은 같은 규격으로 놓는다.
  - 배치: 본문 왼쪽에 맞춘 최대 폭 1120px 안에서 2열(전원·보고 조건 / 대기전력 차단·예약 전원 재투입)로 놓는다. 두 열이 들어가지 않는 폭(390px 등)에서는 한 열로 쌓이고 페이지 가로 스크롤이 생기지 않는다. 1440px 이상의 화면에서는 폭과 상관없이 열 수와 묶음 폭이 같다. 같은 줄의 묶음은 높이가 같다.
  - 묶음 안: 제목, 안내, 입력(릴레이·사용 스위치·요일 포함), 버튼 순서다. 늘 보이는 안내는 제목 바로 아래에 같은 글자 크기로 모은다. 입력의 이유(R19)는 그 입력 아래, 실패 오류 안내(R19)는 버튼 줄 위, 릴레이 오류 안내(device-dashboard R7)는 릴레이 제어 아래에 보이며, 이 세 가지가 나타나면 묶음 높이가 늘어날 수 있다. 글자 입력 칸은 모든 묶음에서 2칸 격자의 한 칸 폭이다. 버튼은 묶음 맨 아래 한 줄에 두고, 주 버튼("실행"·"저장")은 채운 버튼, 나머지("재부팅"·"삭제")는 테두리 버튼이다. "장비에 보냈습니다" 안내는 버튼 줄에 보여 묶음 높이가 바뀌지 않는다.
- R20. 서버 테스트(`DeviceStateTests`, `DeviceStatusApiTests`, `DeviceControlApiTests`)가 R1~R15를, 클라이언트 테스트(`validation.test.ts`, `DeviceControlPage.test.tsx`)가 R16~R19와 R21의 안내·버튼 순서와 종류를 확인하고 통과한다. 그 밖의 R21은 브라우저 화면으로 확인한다.

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-22 | device-control | R1~R21 처음 작성(장비 상태·제어 API와 장비 제어 화면 신설, #28) | docs/specs/device-control/ |
