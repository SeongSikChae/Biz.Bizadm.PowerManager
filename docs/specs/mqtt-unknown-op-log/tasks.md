# MQTT 해석 불가 메시지의 ClientId·원본 페이로드 로그 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 브로커 로그

- [x] T001 상수 `MaxLoggedPayloadBytes = 4096`과 도우미 `DescribePayload(ReadOnlySequence<byte>)` 추가 — 4096바이트 초과 시 `Slice(0, 4096)`만 `Encoding.UTF8.GetString`으로 디코딩, (원래 바이트 수, 잘림 여부, 문자열) 반환 (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs) — C3
- [x] T002 해석 불가 토픽 로그를 `Unparsed topic passed through: {Topic}, clientId={ClientId}, bytes={PayloadBytes}, truncated={PayloadTruncated}, payload={Payload:l}`로 바꿈, 값은 `e.ClientId`·`DescribePayload(e.ApplicationMessage.Payload)` (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs) — C2, C3
- [x] T003 `LogUnknown`이 `InterceptingPublishEventArgs`를 받아 `Unknown device operation {Operation}: {DeviceKey}, topic={Topic}, clientId={ClientId}, bytes={PayloadBytes}, truncated={PayloadTruncated}, payload={Payload:l}`로 남기게 바꾸고, `switch` 기본 분기 인자만 바꿈(알려진 네 분기·`OfflineAsync`·`TryDeserialize`는 그대로) (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs) — C1, C3, C4

체크포인트: `dotnet build` 성공, `git -C "$W" diff 1517b24 -- src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs`에서 알려진 네 분기·`OfflineAsync`·`TryDeserialize` 줄이 바뀌지 않음

## 단계 2: 테스트

- [x] T004 `ClientOptions`에 고정 ClientId 선택 인자 추가, 로그 대기 도우미 `WaitForLogAsync`(100ms 간격, `Timeout`까지 `ReadLogs` 반복) 추가 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs) — C5
- [x] T005 `UnknownOperation_LogsClientIdAndRawPayload` 추가 — 고정 ClientId로 `{"sid":"0","msg":{"o":"n","e":[{"n":"/1/0/0","v":1}]}}` 발행 후 `rg`로 처리 완료 대기, 앱 정지 뒤 로그에 `Unknown device operation`, ClientId, 토픽, 이스케이프되지 않은 원본 페이로드, `truncated=False` 확인 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs) — C1, C5
- [x] T006 `WillOnUnparsedLwtTopic_LogsClientIdAndPayload` 추가 — 원시 TCP로 Will 토픽 `/lwt`·페이로드 `offline`·고정 ClientId인 MQTT 3.1 CONNECT 후 끊고, `WaitForLogAsync`로 `Unparsed topic passed through: "/lwt"`, 그 ClientId, `payload=offline`, `truncated=False` 확인, 핸들러 호출 없음 확인 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs) — C2, C5
- [x] T007 `UnparsedTopic_LargePayload_IsTruncatedTo4096Bytes` 추가 — 해석 불가 토픽에 ASCII 5000바이트 발행, `WaitForLogAsync`로 `bytes=5000`, `truncated=True`, 4096자 연속 문자열 있음·4097자 없음 확인 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs) — C3, C5

체크포인트: `dotnet test` 전체 통과(기존 테스트 포함)

## 단계 3: 기준 spec

- [x] T008 `docs/features/mqtt-broker.md` 새로 작성 — `파일:` 줄(MQTT 브로커·메시지 모델·관련 테스트), 기존 요구(docs/server/0001~0003·현재 코드 기준)와 이번 요구(C1~C3)를 R번호로, 이력(이전 변경 docs/server 0001~0003 #1, 이번 mqtt-unknown-op-log #12) (docs/features/mqtt-broker.md) — C1, C2, C3

체크포인트: 기준 spec 틀(파일·요구·이력)을 갖추고, R번호 요구에 C1~C3이 들어 있음

## 의존 관계

- T002·T003은 T001의 도우미를 쓴다
- 단계 2는 단계 1의 로그 형식을 확인하므로 단계 1 뒤에 한다. T005~T007은 T004의 도우미를 쓴다
- 단계 3은 spec-driven 규칙상 마지막 구현 단계이며, 구현이 끝난 실제 파일 목록을 반영하므로 단계 1·2 뒤에 한다
