# M130-W 상태 보고(msg.o "n")를 Notify로 분배 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 분배 코드

- [x] T001 `MqttOperation`에 상수 `NotifyShort = "n"` 추가, XML 주석에 M130-W 관찰·`notify`의 짧은 형태 명시 (src/Biz.Bizadm.PowerManager.Core/Mqtt/MqttOperation.cs) — C1
- [x] T002 분배 `switch`의 `MqttOperation.Notify` 분기를 `MqttOperation.Notify or MqttOperation.NotifyShort`로 변경 (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs) — C1, C2

체크포인트: `dotnet build` 성공

## 단계 2: 테스트

- [x] T003 `RecordingHandler.NotifyAsync`가 받은 envelope를 `ConcurrentQueue<MqttEnvelope> Notified`에 모으도록 변경(`Calls`에는 지금처럼 `"notify"`) (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs) — C1, C3
- [x] T004 `UnknownOperation_LogsClientIdAndRawPayload`의 오퍼레이션을 `n`에서 `zz`로 바꾸고 기대 로그 문자열을 맞춤 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs) — C2, C6
- [x] T005 새 테스트 `M130NotifyShortOperation_DispatchesToNotify`: ClientId `DAWONDNS-2ef4322ee918`로 토픽 `dwd.v1/DAWONDNS-M130-W-2ef4322ee918/iot-server/notify/json`에 plan.md의 줄바꿈·`ti` 포함 `n` 페이로드, `notify` 페이로드, `rg`를 차례로 QoS 1 발행. `Calls`가 `notify, notify, rg`, `Notified` 오퍼레이션이 `n`·`notify` 순서, 첫 envelope 요소 경로·값이 페이로드와 같음, 로그에 `Unknown device operation`·`not valid JSON`·`no msg.o or msg.e`가 없음을 확인 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs) — C1, C2, C3, C6

체크포인트: `dotnet test` 전체 통과

## 단계 3: 문서

- [x] T006 [P] 선행 조건: 작업 문서에 적은 근거 문서 수정 전 사본(스크래치 `mqtt-notify-short-op-baseline/docs/.tmp/b540-mqtt-topic.md`, plan 작성 직후 복사함)이 있는지 확인하고, 없으면 고치기 전에 복사한다. 근거 문서에 M130-W 관찰 절 추가(토픽·ClientId·`msg.o` `n`·`ti`·리소스 경로, 의미는 추정·모르는 것은 미확인), 3절 오퍼레이션 표 `n` 행(관찰 위치 "M130-W 실통신", 함수 이름 "—"), 머리말 근거 줄·표기 줄(관찰 = 덤프 또는 실장비 통신 로그에 직접 존재)·4절 미확인 항목 정합 (docs/.tmp/b540-mqtt-topic.md) — C4
- [x] T007 기준 spec 반영: 설명 문단에 `n` 분배 반영, R4에 `n` → `NotifyAsync` 추가, R6을 "R4의 다섯 값"으로, 이력에 이번 작업 한 줄(`파일:` 줄은 그대로) (docs/features/mqtt-broker.md) — C5

체크포인트: 두 문서를 읽어 C4·C5 문구 확인, 근거 문서는 수정 전 사본과 `git diff --no-index`로 바뀐 부분 확인

## 의존 관계

- T002는 T001의 상수를 쓴다.
- 단계 2는 단계 1이 끝나야 테스트가 통과한다. T003~T005는 같은 파일이라 순서대로 한다.
- T006은 코드와 독립이라 언제든 할 수 있지만, 근거 문서는 .gitignore 대상이라 수정 전 사본이 유일한 비교 기준이므로 사본 확인(T006 선행 조건)을 고치기보다 먼저 한다. T007은 기준 spec 반영이라 구현의 마지막 단계에 둔다(spec-driven 3절 2번).
