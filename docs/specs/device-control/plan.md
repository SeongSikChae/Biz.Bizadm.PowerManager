# 장치 제어 화면·기능 계획

## 요약

서버에 장비별 상태를 메모리에 두는 `DeviceStateStore`를 만들고, 브로커가 장비의 메시지·접속·끊김을 이 저장소에 알린다. 서버 → 장치 명령 발행(`MqttDeviceCommandPublisher`)을 구현해, 기존 `tisyn` 응답과 같은 주입 경로로 제어 메시지를 보내고 그 메시지가 대상 장비 클라이언트의 큐에 들어갔는지를 브로커 전달 이벤트로 판정한다. 관리 포트에 상태 조회·상태 스트림(SSE)·설정 조회·제어 API를 더한다.

클라이언트는 라우팅(`/`, `/devices`, `/devices/{mac}`)을 들이고, 상태 스트림을 앱 전체에서 한 번 받아 Redux에 두며, 대시보드와 장비 제어 화면을 만든다. 실장비에서 예약을 확인해 근거 문서와 `docs/reference/b540-mqtt-topic.md`에 적고, 마지막에 기준 spec 다섯 개를 반영한다.

## 바꿀 파일·구조

서버(Core):

- `src/Biz.Bizadm.PowerManager.Core/Mqtt/MqttOperation.cs`: `Execute`(`e`)·`Read`(`r`) 상수를 더한다.
- `src/Biz.Bizadm.PowerManager.Core/Mqtt/IDeviceCommandPublisher.cs`: 쓰지 않는 `WriteAsync`·`ChangeAsync`·`RebootAsync`를 빼고, `Task<DevicePublishResult> PublishAsync(DeviceCommandTarget target, string operation, IReadOnlyList<MqttElement> elements, CancellationToken ct)` 하나로 바꾼다.
- `src/Biz.Bizadm.PowerManager.Core/Mqtt/DeviceCommandTarget.cs`(새 파일): `record DeviceCommandTarget(string Mac, string Prefix, string DeviceKey)`.
- `src/Biz.Bizadm.PowerManager.Core/Mqtt/DevicePublishResult.cs`(새 파일): `record DevicePublishResult(bool Enqueued, string Topic, string Sid)`.

서버(Server.Spa):

- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceStateStore.cs`(새 파일): 장비별 메모리 상태(온라인, 릴레이, 계량 값, 마지막 보고 시각, 토픽 정보, 이번 접속의 읽기 상태, 마지막으로 보낸 설정)와 변경 알림. C1·C2·C3·C13·C30의 상태 규칙을 여기에 둔다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceRelayReader.cs`(새 파일): C30의 릴레이 읽기를 보낸다(저장소의 판정을 받아 발행기로 보내고 결과를 저장소에 돌려줌).
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceStatusService.cs`(새 파일): DB 등록 정보와 저장소 상태를 합쳐 C4 원소를 만들고, 스트림용 변경 구독을 준다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceStatusResponse.cs`(새 파일): C4 원소.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceSettingsModels.cs`(새 파일): 제어 요청 본문과 설정 조회 응답(C8·C10~C13).
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceCommandService.cs`(새 파일): 입력 검사, 제어 메시지 요소 만들기, 발행, 성공한 설정 기록(C6~C13).
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/AdminDeviceStatusController.cs`(새 파일): 상태 조회·상태 스트림·설정 조회 API(C4·C5·C13·C15).
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/AdminDeviceControlController.cs`(새 파일): 제어 API(C6~C12·C15).
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceRegistrationService.cs`: 등록으로 장비가 새로 생기거나 `modelId`가 바뀌면 저장소에 알린다(C5 "장비 등록").
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/DownlinkDeliveryTracker.cs`(새 파일): 발행한 제어 메시지의 `sid`별 대기와, 브로커 전달 이벤트로 받은 결과(대상 클라이언트 큐에 넣음 / 구독자 없음) 짝짓기.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttDeviceCommandPublisher.cs`: 껍데기를 구현으로 바꾼다(C6·C14).
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs`: 접속·끊김 이벤트(`ClientConnectedAsync`·`ClientDisconnectedAsync`)를 저장소에 알리고, 해석한 장치 → 서버 메시지를 분배하기 전에 보낸 ClientId와 함께 저장소에 알리며, 큐 넣음·구독자 없음 이벤트를 `DownlinkDeliveryTracker`에도 넘긴다. 기존 로그와 분배는 그대로 둔다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Program.cs`: 새 서비스를 등록한다.

서버 테스트:

- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TestMqtt.cs`(새 파일): 등록 장치로 접속하는 테스트 MQTT 클라이언트, 받은 메시지 모음, 기다리기·표시 메시지 헬퍼(기존 `TimeSyncResponseTests`의 헬퍼와 같은 방식으로 새로 만든다).
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TimeSyncResponseTests.cs`: `tisyn`을 보내는 장치가 그 전에 `n` `/100/0/31` 상태 보고를 먼저 보내게 준비 단계만 바꾼다. 릴레이 상태를 알게 되어 C30 읽기가 나가지 않으므로 응답 하나(`Single()`) 등 기존 단정은 그대로 둔다(D019).
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttPacketFragmentationTests.cs`: 위와 같이 `tisyn` 전에 상태 보고를 먼저 보내게 준비 단계만 바꾼다. TLS 레코드 하나 단정은 그대로 둔다(D019). `MqttSubscribeLogTests.cs`는 고칠 것이 없다. C5 테스트(`NonDownlinkTopics_HaveNoDeliveryLogs`)는 `/100/0/31` `true`를 먼저 보내므로, C1 반영을 C30 판정보다 먼저 하면 C30 읽기가 나가지 않는다. C3·C4 테스트(`DownlinkEnqueued_LogsSenderAndEachReceiver`·`DownlinkWithoutSubscriber_LogsNotConsumedWarning`)에서는 C30 읽기가 더 나간다. 그래도 기다리는 줄은 그대로 나타나고, 큐 넣음이 없다는 단정도 맞는다(C4는 구독자가 없어 읽기도 큐에 들어가지 않는다).
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DeviceStateTests.cs`(새 파일): C1·C2·C3·C30과 장비 판정(용어).
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DeviceStatusApiTests.cs`(새 파일): C4·C5·C15(상태·스트림).
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DeviceControlApiTests.cs`(새 파일): C6~C14·C15(제어·설정).
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TestDevices.cs`: `modelId`를 받는 등록 헬퍼를 더한다(재부팅 모델 검사용).

클라이언트:

- `src/Biz.Bizadm.PowerManager.Client/package.json`, `package-lock.json`: `react-router` 7.x를 더한다.
- `src/Biz.Bizadm.PowerManager.Client/src/index.css`: 상태용 색 토큰 `--success`와 `--color-success`를 더한다(C17).
- `src/Biz.Bizadm.PowerManager.Client/src/components/ui/card.tsx`, `switch.tsx`, `alert-dialog.tsx`, `label.tsx`, `checkbox.tsx`(새 파일): shadcn CLI로 더한다. `Switch`·`Label`·`Checkbox`는 `React.forwardRef`로 보정한다.
- `src/Biz.Bizadm.PowerManager.Client/src/App.tsx`: 라우터와 세 경로, 상태 스트림 시작.
- `src/Biz.Bizadm.PowerManager.Client/src/main.tsx`: `BrowserRouter`로 감싼다.
- `src/Biz.Bizadm.PowerManager.Client/src/components/AppShell.tsx`: 메뉴를 "대시보드"·"장비" 두 개의 `Link`로 바꾸고, 현재 메뉴를 `useMatch`로 판정해 `aria-current="page"`를 붙인다. `/devices`면 "장비", `/`와 `/devices/:mac`이면 "대시보드"다(C16).
- `src/Biz.Bizadm.PowerManager.Client/src/app/store.ts`: 상태 slice와 제어 API를 더한다.
- `src/Biz.Bizadm.PowerManager.Client/src/features/device-status/types.ts`(새 파일): 상태·설정·요청 타입.
- `src/Biz.Bizadm.PowerManager.Client/src/features/device-status/deviceStatusApi.ts`(새 파일): RTK Query로 상태 조회·설정 조회·제어 API.
- `src/Biz.Bizadm.PowerManager.Client/src/features/device-status/deviceStatusSlice.ts`(새 파일): 장비별 상태, 스트림 연결 상태, 받은 적이 있는지.
- `src/Biz.Bizadm.PowerManager.Client/src/features/device-status/useDeviceStatusStream.ts`(새 파일): `EventSource`로 스트림을 받아 slice에 넣는다.
- `src/Biz.Bizadm.PowerManager.Client/src/features/device-status/format.ts`, `format.test.ts`(새 파일): 계량 값·요약·상대 시간 표시(상대 시간은 `features/devices/deviceList.ts`의 `formatRelative`를 쓴다).
- `src/Biz.Bizadm.PowerManager.Client/src/features/device-status/RelayControl.tsx`, `RelayControl.test.tsx`(새 파일): 스위치·상태 모름 버튼·끄기 확인 창·진행 중·10초 시간 초과(C21·C22).
- `src/Biz.Bizadm.PowerManager.Client/src/features/device-status/ConfirmDialog.tsx`(새 파일): 장비를 밝힌 확인 창(릴레이 끄기·전원 재투입·재부팅·예약 삭제).
- `src/Biz.Bizadm.PowerManager.Client/src/features/dashboard/DashboardPage.tsx`, `DeviceCard.tsx`, `DashboardPage.test.tsx`(새 파일): C18~C23.
- `src/Biz.Bizadm.PowerManager.Client/src/features/device-control/DeviceControlPage.tsx`, `PowerSection.tsx`, `ReportConditionForm.tsx`, `StandbyCutoffForm.tsx`, `ScheduleForm.tsx`(새 파일): C24~C27·C32. 네 묶음이 함께 쓰는 틀(제목·안내·입력·버튼 줄)은 `PowerSection.tsx`의 `Section`에 둔다.
- `src/Biz.Bizadm.PowerManager.Client/src/features/device-control/validation.ts`, `validation.test.ts`, `DeviceControlPage.test.tsx`(새 파일): 입력 범위(C27)와 화면 동작.
- `src/Biz.Bizadm.PowerManager.Client/src/test/setup.ts`: 테스트용 가짜 `EventSource`를 둔다(jsdom에는 없음).
- `src/Biz.Bizadm.PowerManager.Client/src/App.test.tsx`: 경로·메뉴·`aria-current`(C16) 확인으로 바꾼다.

문서:

- `docs/features/device-dashboard.md`(새 파일): 기준 spec.
- `docs/features/device-control.md`(새 파일): 기준 spec.
- `docs/features/mqtt-broker.md`: R5 바꿈(명령 발행 구현), 상태 보고 처리·접속 추적·제어 메시지 발행 요구 추가, `파일:` 줄.
- `docs/features/admin-ui.md`: R3(메뉴 둘, 경로), R1(상태용 색 토큰), R4(새 UI 부품), `파일:` 줄.
- `docs/features/device-list.md`: R4(화면 경로 `/devices`), `파일:` 줄.
- `docs/reference/mqtt-device-control.md`: 실장비 확인 결과와 2026-11-22 관찰을 해당 절(머리말의 근거·표기, 2절·3.1·3.2·3.3·3.4·6절·7절)에 적는다(C31).
- `docs/reference/b540-mqtt-topic.md`: B540이 시간 동기로 시계를 맞추는 관찰과 약 7초 늦은 보고를 시간 동기와 미확인 절에 적고, 머리말과 5절에서 B540 실제 통신을 관찰하지 않았다는 문장을 맞춘다(C31).

## 결정

- 제어 메시지는 `InjectApplicationMessage`(기존 `tisyn` 응답과 같은 경로, 구독에 따라 전달)로 보내고, 성공 판정은 브로커의 큐 넣음 이벤트(`ApplicationMessageEnqueuedOrDroppedAsync`)에서 받는 쪽이 `DAWONDNS-{mac}`인 것을 받았는지로 한다: 장치가 구독한 토픽으로만 보내 기존 R12·R13 로그가 그대로 남고, C6의 "그 장비 클라이언트의 전송 큐"와 맞는다 / 검토한 대안: 그 장비 세션에 직접 넣기(`MqttSessionStatus.TryEnqueueApplicationMessage`)는 결과를 바로 알 수 있으나 구독과 상관없이 넣고, 기본 넘침 정책에서는 늘 `true`라서 판정에 쓸 수 없다. 근거는 둘이다. MQTTnet.Server 5.2 XML 문서의 `TryEnqueueApplicationMessage` 설명은 넘침 정책이 `DropOldestQueuedMessage`이면 늘 `true`라고 적는다. 그 정책이 기본값이라는 것은 DLL에서 `new MqttServerOptions().PendingMessagesOverflowStrategy`를 읽어 확인했고, 저장소는 이 값을 바꾸지 않는다.
- 큐 넣음 이벤트와 발행을 짝짓는 열쇠는 서버가 만드는 `sid`다. 서버 실행마다 100부터 1씩 늘린 10진 문자열을 쓴다(장치가 보고에 쓰는 `"0"`·`"1"`·`"2"`와 겹치지 않게). 이벤트에서는 보낸 쪽이 `power-manager`이고 서버 → 장치 토픽인 메시지만 페이로드의 `sid`를 읽는다 / 검토한 대안: 메시지 객체 참조 비교는 브로커가 구독자마다 복사하는지 모른다(미확인). MQTT 5 사용자 속성은 MQTT 3.1 구독자에게 보내는 메시지여도 큐 넣음 이벤트에서 읽히지만(MQTTnet.Server 5.2.0.1603에 `V310` 구독자를 붙여 실행해 확인), 제어 메시지에는 C14 로그에 남길 `sid`가 이미 있어 표시를 하나 더 둘 까닭이 없다 (D024).
- 주입 뒤 결과를 최대 3초 기다리고, 그동안 대상 큐 넣음이 없으면(구독자 없음 이벤트를 받은 경우 포함) 넣지 못한 것으로 본다: `InjectApplicationMessage`가 전달 이벤트를 다 부른 뒤 돌아오는지 확인하지 못해(미확인), 그 동작에 기대지 않는다.
- 온라인은 `ClientConnectedAsync`에서 켜고, `ClientDisconnectedAsync`에서 끊김 종류가 `Takeover`가 아니면 끈다(C2). 접속할 때마다 그 장비의 "이번 접속에서 읽기를 넣음" 표시를 지운다(C30): MQTTnet.Server 5.2에 `MqttClientDisconnectType.Takeover`가 있다(DLL 메타데이터, REA `inspect_managed_members`) / 검토한 대안: 접속 수 세기는 교체 때 옛 연결의 끊김 이벤트가 오지 않으면 오프라인으로 돌아가지 않는다.
- 장비의 메시지 판정(용어)·토픽 기억(C3)·상태 반영(C1)·읽기 판정(C30)은 브로커의 `OnPublishAsync`에서 분배 전에 `DeviceStateStore`에 ClientId와 함께 넘겨 한다: `IDeviceMessageHandler`에는 ClientId가 없고, 바꾸면 기존 핸들러·테스트가 모두 바뀐다. 기존 핸들러의 로그·`tisyn` 응답은 그대로다. 한 메시지 안에서는 C3 토픽 기억 → C1 상태 반영 → C30 읽기 판정 순서로 한다. 그래서 릴레이 요소가 든 상태 보고에는 읽기를 보내지 않는다(D019).
- C30의 읽기 발행은 `OnPublishAsync` 안에서 기다리지 않고 따로 실행한다. 저장소는 보내기 전에 "보내는 중"으로 표시해 같은 접속에서 겹쳐 보내지 않고, 큐에 넣었으면 "넣음", 못 넣었으면 표시를 지워 다음 메시지 때 다시 보낸다: 결과를 최대 3초 기다리는 동안 그 클라이언트의 메시지 처리를 막지 않기 위해서다.
- 상태 스트림은 ASP.NET Core 10의 `TypedResults.ServerSentEvents`로 보낸다. 연결마다 먼저 `snapshot` 이벤트(C4 배열), 그 뒤 바뀐 장비마다 `device` 이벤트(C4 원소)를 보낸다. 변경 알림은 장비 mac만 담고, 보낼 때 DB 등록 정보와 저장소 상태로 원소를 다시 만든다(같은 장비의 알림이 쌓이면 하나로 합침). 요청 취소(`RequestAborted`)와 서버 종료(`ApplicationStopping`)에 스트림을 끝낸다: 추가 패키지가 없다(D003).
- 클라이언트 라우팅은 `react-router` 7.x(React 18을 지원)를 쓴다: 세 경로, 주소 직접 열기, 뒤로 가기를 직접 만들지 않아도 되고, 현재 메뉴(C16)는 `useMatch`로 판정한다 / 검토한 대안: `history` API로 직접 만든 작은 라우터는 의존성이 없지만 링크·뒤로 가기·경로 판정을 따로 만들고 시험해야 한다. `NavLink`는 `/`에서만 "대시보드"가 활성이고 `/devices/{mac}`에서 "장비"가 활성이라 C16과 다르다(react-router 7.9.1 `lib/dom/lib.tsx`의 `NavLink`) (D023).
- 상태 스트림은 `App`에서 한 번 열고, 대시보드와 장비 제어 화면이 같은 slice를 본다. 네트워크 오류로 끊기면 브라우저가 다시 연결한다. 그러나 응답이 200이 아니거나 `text/event-stream`이 아니면 `EventSource`는 `CLOSED`가 되어 다시 시도하지 않으므로(HTML 표준 「Server-sent events」), `error` 때 `readyState`가 `CLOSED`면 스트림 훅이 3초 뒤 새 `EventSource`를 만든다. 어느 쪽이든 다시 연결되면 서버가 `snapshot`을 먼저 보내므로 전체 상태를 다시 맞춘다(C20).
- 릴레이 제어의 진행 중 판정(C22)은 카드마다 "보낸 값과 보낸 시각"을 두고, slice의 그 장비 릴레이가 보낸 값이 되면 끝내며, API 실패나 10초가 지나면 오류 안내와 함께 끝낸다.
- 장비 제어 화면의 네 묶음은 `max-w-[70rem]`(1120px) 격자에 2열로 놓고(열 최소 폭은 1120px에 세 열이 들어가지 않는 `27rem`이다: 432×3+16×2=1328px. 두 열이 들어가지 않으면 1열), 격자의 늘림(`stretch`)으로 같은 줄의 높이를 맞춘다. 묶음 틀 `Section`이 제목·안내·내용·버튼 줄을 받아, 내용이 남는 높이를 채우고 버튼 줄이 맨 아래에 오게 한다. 성공 안내는 버튼 줄 안에, 실패 안내는 길어질 수 있어 버튼 줄 위에 보인다 / 검토한 대안: 1열 설정 목록은 세로로 길어지고, 화면 폭을 다 쓰는 2~4열은 폭에 따라 열 수가 달라진다 (D032, D036).
- 대기전력 기준 전력은 소수 둘째 자리가 넘는 입력을 400으로 거르고, 보낼 때는 늘 소수 둘째 자리까지 채운 문자열(`"5.00"`)로 보낸다(C11).
- 예약 요일은 API에서 일~토 순서의 불리언 7개로 받고, 서버가 C12의 7글자로 바꾼다(첫 글자 `0`, 둘째 글자는 월 1과 일 2의 합, 나머지는 화~토의 `0`·`1`). 바꾸는 곳은 `DeviceCommandService`의 변환 한 곳이다: 장치가 첫 글자(일)를 저장하지 않으므로 둘째 글자로 일요일 비트를 만든다 / 검토한 대안: 일요일 선택을 막으면 일요일 예약을 쓸 수 없고, 서버가 그 시각에 전원 재투입을 보내면 서버 쪽 예약 실행·저장이 필요하고 장치가 오프라인이면 동작하지 않는다 (D027).

## 인터페이스·데이터

관리 API(모두 `[AdminPortOnly]`, JSON은 camelCase):

| 메서드·경로 | 본문 | 응답 |
|---|---|---|
| `GET /api/admin/devices/status` | — | 200, C4 원소 배열(mac 오름차순) |
| `GET /api/admin/devices/status/stream` | — | 200 `text/event-stream`, `snapshot`·`device` 이벤트 |
| `GET /api/admin/devices/{mac}/settings` | — | 200 설정, 400·404 |
| `POST /api/admin/devices/{mac}/relay` | `{"on":true}` | 204, 400·404·409 |
| `POST /api/admin/devices/{mac}/power-cycle` | `{"seconds":10}` | 204, 400·404·409 |
| `POST /api/admin/devices/{mac}/reboot` | — | 204, 400·404·409 |
| `PUT /api/admin/devices/{mac}/report-condition` | `{"minInterval":60,"maxInterval":600,"changeThreshold":5}` | 204, 400·404·409 |
| `PUT /api/admin/devices/{mac}/standby-cutoff` | `{"enabled":true,"thresholdWatts":5.0,"delay":60}` | 204, 400·404·409 |
| `PUT /api/admin/devices/{mac}/schedule` | `{"enabled":true,"days":[false,true,true,true,true,true,false],"time":"03:00:00","seconds":10}` | 204, 400·404·409 |
| `DELETE /api/admin/devices/{mac}/schedule` | — | 204, 400·404·409 |

- 판정 순서: mac 형식(400) → 등록(404) → 본문 범위·모델(400) → 제어 가능(409) → 발행 결과(큐에 못 넣으면 409). C6의 "입력 값이 범위를 벗어나면 400"과 "제어 가능하지 않으면 409"가 함께 해당하면 400이다.
- C4 원소: `mac`, `modelId`, `online`, `relay`(`"on"`·`"off"`·`"unknown"`), `voltage`, `current`, `powerFactor`, `power`(숫자 또는 null), `lastReportAt`(ISO 8601 또는 null), `controllable`.
- 설정 응답: `reportCondition`(`minInterval`·`maxInterval`·`changeThreshold`) / `standbyCutoff`(`enabled`·`thresholdWatts`·`delay`) / `schedule`(`enabled`·`days`·`time`·`seconds`), 보낸 적이 없으면 각각 null.
- SSE 형식: `event: snapshot` + `data: [<C4 원소>...]`, `event: device` + `data: <C4 원소>`.
- 제어 메시지 요소(C7~C12)와 C14 로그 항목: `Device command sent: mac={Mac}, topic={Topic}, sid={Sid}, operation={Operation}, elements={Elements}`(Information), 큐에 넣지 못함 `Device command not enqueued: ...`(Warning), 브로커 없음·예외 `Device command failed: ...`(Error).

장비 상태(메모리, mac별):

- `Online`, `Relay`(켜짐·꺼짐·모름), `Voltage`·`Current`·`PowerFactor`·`Power`(`double?`), `LastReportAt`, `Topic`(접두·장치 키, 처음 null), `RelayRead`(없음·보내는 중·넣음), `ReportCondition`·`StandbyCutoff`·`Schedule`(마지막으로 보낸 값).

## 오류·위험

- `InjectApplicationMessage`가 전달 이벤트를 부른 뒤 돌아오는지 미확인이다. 결정대로 3초 기다림으로 덮는다. 짐작과 달리 이벤트가 3초보다 늦으면 큐에 넣었는데 409가 되며, 제어 API 테스트(큐 넣음 → 204)가 실패해 드러난다.
- 브로커가 같은 ClientId의 새 접속으로 옛 접속을 끊을 때 `ClientDisconnectedAsync`가 `Takeover`로 오는지, 새 접속의 `ClientConnectedAsync`보다 먼저인지 미확인이다. C2 테스트(같은 ClientId로 다시 접속한 뒤 온라인)로 확인한다. 실패하면 끊김 때 그 연결의 세션 항목이 지금 연결의 것인지 비교하는 방식으로 고친다.
- 장치가 요청 `sid`를 얼마나 긴 문자열까지 받는지 미확인이다(근거 문서 3.4는 응답 `sid` 복사만 관찰). 짧은 10진 문자열로 둔다. 실장비 확인(C31)에서 예약 저장이 반응하지 않으면 원인 후보로 본다.
- 장치가 `e`·`r` 응답을 `notify/json` 토픽에 `n`으로 보낸다는 것은 근거 문서 3.4의 토픽 추정이다. 서버는 토픽 접미와 상관없이 `msg.o` `n`·`notify`를 상태 보고로 보므로(C1) 접미가 달라도 반영된다.
- 상태 스트림 원소를 만들 때마다 DB에서 등록 정보를 읽는다. 장비 수가 수십 대 수준이라 부담이 작다고 보고, 문제가 되면 등록 정보를 메모리에 둔다.
- 개발 모드에서 SSE는 Vite 프록시(`/api`)를 지난다. 프록시가 스트림을 모아 보내면 실시간 반영이 늦어지며, 브라우저 화면 확인(C20)에서 드러난다.
- shadcn CLI가 부품을 내려받으려면 네트워크가 필요하다. 생성물이 React 19 기준이면 규칙대로 `forwardRef`로 보정한다.

## 검증 전략

- C1·C2·C3·C30: `DeviceStateTests`. 테스트 MQTT 클라이언트로 등록 장비가 접속해 `notify`·`n`·`tisyn`을 보내고, 상태 조회 API와 받은 서버 → 장치 메시지(`r` `/100/0/30`)를 확인한다. 장치 키에 mac이 없는 메시지는 Warning 로그와 미반영을, 같은 ClientId 재접속은 온라인 유지를, 구독 없이 보낸 첫 메시지 뒤 구독하고 보낸 다음 메시지에서 읽기를 다시 보내는 것을 본다.
- C4·C5·C15: `DeviceStatusApiTests`. 상태 조회 필드·값·MQTT 키 없음, 스트림의 `snapshot`과 상태 보고·접속·끊김·토픽 처음 알게 됨·등록 때의 `device` 이벤트, 요청 취소로 끝남, 장치 포트 404.
- C6~C14·C15: `DeviceControlApiTests`. API마다 받은 페이로드(`o`, 요소 순서와 값), 400·404·409, 구독 없는 장비 409, 설정 조회 값과 예약 삭제 뒤 null, C14 로그 항목, 장치 포트 404와 메시지 없음.
- C16·C17(글자)·C18~C27(C23 배치 제외)·C32(안내·버튼 순서와 종류): Vitest 테스트(`App.test.tsx`, `format.test.ts`, `RelayControl.test.tsx`, `DashboardPage.test.tsx`, `validation.test.ts`, `DeviceControlPage.test.tsx`). `fetch`와 가짜 `EventSource`로 스트림 이벤트를 넣고 화면을 본다. 10초 시간 초과는 가짜 타이머로 본다. 가짜 `EventSource`를 `CLOSED`로 만든 뒤 3초 뒤 새로 열리고, 새 `snapshot`으로 화면이 맞춰지는 것도 본다(C20).
- C23의 390px 배치와 C17의 색 토큰: 개발 서버를 띄워 내장 브라우저로 1440px·390px 화면을 보고, 가로 스크롤 없음(`document.documentElement.scrollWidth`)과 온라인 점의 계산된 색이 `--success` 값인지 본다.
- C32(배치): 개발 서버를 띄워 내장 브라우저로 1440px·2000px·390px 화면에서 네 묶음의 좌표와 크기를 읽어, 1440px와 2000px에서 열이 둘이고 첫 줄이 전원·보고 조건, 둘째 줄이 대기전력 차단·예약 전원 재투입인지, 격자 폭이 1120px이고 왼쪽 끝이 본문 내용의 왼쪽 끝과 같은지, 두 화면의 묶음 폭이 같은지, 같은 줄의 높이가 같은지, 입력 칸의 폭과 안내의 계산된 글자 크기가 네 묶음에서 같은지, 버튼 줄이 묶음 아래에 붙는지, 네 묶음 모두 보내기에 성공한 뒤 높이가 그대로인지(전원은 "실행", 나머지는 "저장"), 390px에서 한 열이고 가로 스크롤이 없는지 본다. 보내기 성공은 가짜 장비로 만든다(실장비에 보내지 않음).
- C28: 기존 MQTT 테스트(`TimeSyncResponseTests`·`MqttPacketFragmentationTests`·`MqttSubscribeLogTests`)가 C30을 더한 뒤에도 단정을 바꾸지 않고 통과하는지 포함해 `dotnet test`(솔루션 전체)와 `npm test`, `npm run build`, `npm run lint`가 통과한다.
- C29: 기준 spec 다섯 개의 머리말·요구·이력·`파일:` 줄을 읽어 이번 변경과 맞는지 본다(구현 후 검토 diff).
- C31: 사용자가 장치를 정해 준 뒤, 그 장치가 이 서버에 접속한 상태에서 장비 제어 화면으로 그 날의 요일만 고른 예약을 몇 분 뒤 시각으로 저장하고, 서버 로그의 보낸 문자열, 그 시각에 본체에서 릴레이가 꺼졌다가 N초 뒤 켜지는 것(사용자가 봄), 로그의 다시 켤 때 보고(`/100/0/31` `true`, `/100/0/32` `16`)를 본다. 보낸 문자열의 요일이 C12대로인지도 본다. 끝나면 예약을 삭제하고 결과와 2026-11-22 관찰을 근거 문서와 `docs/reference/b540-mqtt-topic.md`의 해당 절에 적는다. 실장비에 보내기 전에 대상 장치를 사용자에게 확인받는다.

## 원칙 점검

- 적용 규칙: `workflow/spec-driven.md`(및 하위 문서) — 기준 spec 반영을 마지막 구현 단계에 두고, 미확인 동작을 "오류·위험"에 적었다. 충돌 없음.
- 적용 규칙: `git/github-issue.md` — 이슈 #28을 쓰고 커밋에 참조한다. 범위가 바뀌지 않아 이슈 갱신은 없다. 충돌 없음.
- 적용 규칙: `workflow/repository-content.md` — 문서·코드에 로컬 경로·개인정보를 넣지 않는다. C31 기록에는 장치 mac 대신 모델·펌웨어 버전만 적는다. 충돌 없음.
- 적용 규칙: `frontend/react-project-creation.md` — 기존 프로젝트 구조·버전을 따른다. 상태는 Redux Toolkit slice와 RTK Query, 컴포넌트에서 직접 fetch하지 않음(스트림은 `features/device-status`의 훅), 새 feature 폴더는 kebab-case(`device-status`·`dashboard`·`device-control`), shadcn CLI 생성물과 `forwardRef` 보정, npm. 라우터는 규칙 8의 `src/app/` 대신 `App.tsx`에 둔다(경로가 셋뿐이라 파일을 나누지 않음). 충돌 없음(규칙 8은 "기본으로 한다").
- 적용 규칙: `dotnet/nuget-package-selection.md` — 새 NuGet 패키지가 없다. 충돌 없음.
- 적용 규칙: `dotnet/server-spa-template.md` — 운영 모드 HTML fallback으로 `/devices/{mac}`을 직접 열 수 있다(기존 `ServeIndexHtmlAsync`). 충돌 없음.
- 적용 규칙: `shell/powershell-windows.md` — 명령은 UTF-8로 실행한다. 충돌 없음.
- 적용 규칙: admin-ui R1~R6(저장소 기준 spec) — 토큰 색만, shadcn 부품, 관리 API `[AdminPortOnly]`. 라우터는 UI 라이브러리가 아니라 R1과 충돌하지 않는다. 충돌 없음.
- 적용 규칙: mqtt-broker R5·R7·R12·R13 — 기존 로그와 `tisyn` 응답을 그대로 두고, 제어 메시지도 주입 경로라 R7 로그에서 빠지고 R12·R13 로그가 남는다. R5의 "`NotImplementedException`" 문장은 이번에 바꾼다(기준 spec 반영). 충돌 없음.
- 적용 규칙: 사용자 메모(실장비 대상 확인) — 실장비 제어 메시지(C31) 전에 대상 장치를 확인받는다. 충돌 없음.
