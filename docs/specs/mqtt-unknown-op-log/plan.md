# MQTT 해석 불가 메시지의 ClientId·원본 페이로드 로그 계획

## 요약

`MqttBrokerHostedService.OnPublishAsync`의 두 로그(해석하지 못한 토픽 Information, 알 수 없는 오퍼레이션 Warning)에 ClientId(`InterceptingPublishEventArgs.ClientId`)와 원본 페이로드를 더한다. 페이로드는 4096바이트까지만 UTF-8로 디코딩하고, 원래 바이트 수와 잘림 여부를 별도 속성으로 같은 로그 항목에 남긴다. 분배 로직은 건드리지 않는다. 테스트는 `MqttBrokerTests`에 세 개를 더하고, `mqtt-broker` 기준 spec을 새로 만든다.

## 바꿀 파일·구조

- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs`: 해석 불가 토픽 로그와 `LogUnknown`에 ClientId·페이로드 속성 추가, 페이로드 서식 도우미(`DescribePayload`)와 상수 `MaxLoggedPayloadBytes = 4096` 추가, `switch`의 기본 분기(`_ => LogUnknown(...)`) 인자만 바꿈
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs`: 테스트 3개 추가(알 수 없는 오퍼레이션, `/lwt` Will, 잘림), `ClientOptions`에 고정 ClientId 선택 인자 추가, 로그 대기 도우미(`WaitForLogAsync`) 추가
- `docs/features/mqtt-broker.md`: 새 기준 spec. 기존 MQTT 브로커 요구(`docs/server/0001~0003` 문서와 현재 코드 기준)와 이번 요구, 이력, 파일 목록을 처음 쓴다

## 결정

- 페이로드를 메시지 템플릿 속성 셋으로 남김(`PayloadBytes`, `PayloadTruncated`, `Payload`): 잘림 표시와 원래 바이트 수를 같은 로그 항목에 구조화된 값으로 둘 수 있다 / 검토한 대안: 문자열 끝에 `…(truncated)`를 붙이는 방식 — 원본 페이로드와 표시가 섞여 원본을 구분하기 어렵다
- `Payload`는 `{Payload:l}`(literal) 서식으로 렌더링: Serilog 기본 렌더링은 문자열을 따옴표로 감싸고 내부 따옴표를 이스케이프해 C1의 "이스케이프하지 않음"을 어긴다 / 대안: 기본 서식 — 위 이유로 고르지 않음
- 디코딩은 `Encoding.UTF8.GetString(in ReadOnlySequence<byte>)`(`System.Text.EncodingExtensions`)로, 4096바이트를 넘으면 `payload.Slice(0, 4096)`만 디코딩: 페이로드 전체를 배열로 복사하지 않는다 / 대안: 문자 경계까지 맞춰 자르기 — 잘린 끝 문자가 대체 문자(U+FFFD)로 보이는 정도라 형태 확인 목적에는 불필요
- 기존 로그 문구 앞부분(`Unparsed topic passed through: {Topic}`, `Unknown device operation {Operation}: {DeviceKey}`)은 유지하고 뒤에 속성을 덧붙임: 지금까지 남은 로그와 같은 문구로 검색할 수 있다
- `LogUnknown`에 `InterceptingPublishEventArgs`를 넘김: 토픽·ClientId·페이로드를 한 인자로 받는다. 바뀌는 것은 `switch`의 기본 분기 인자뿐이고 알려진 오퍼레이션 네 분기는 그대로다(C4)
- Will 테스트는 로그 파일을 주기적으로 다시 읽어 기다림(`WaitForLogAsync`, 100ms 간격, 기존 `Timeout` 10초): Will 발행은 핸들러를 부르지 않아 기다릴 신호가 없다. 파일 sink는 `FileShare.ReadWrite`로 읽을 수 있다(기존 `ReadLogs`) / 대안: 테스트용 로거 공급자 주입 — `AddSerilog`가 로깅을 대신 맡아 추가 공급자 구성이 복잡하다

## 인터페이스·데이터

로그 템플릿(속성 이름은 Serilog 속성):

```
Unparsed topic passed through: {Topic}, clientId={ClientId}, bytes={PayloadBytes}, truncated={PayloadTruncated}, payload={Payload:l}
Unknown device operation {Operation}: {DeviceKey}, topic={Topic}, clientId={ClientId}, bytes={PayloadBytes}, truncated={PayloadTruncated}, payload={Payload:l}
```

도우미:

```csharp
private const int MaxLoggedPayloadBytes = 4096;
private static (long Bytes, bool Truncated, string Text) DescribePayload(ReadOnlySequence<byte> payload);
```

## 오류·위험

- 서버가 브로커에 직접 넣는 메시지(앞으로 구현될 명령 발행 등)는 ClientId가 비거나 서버 쪽 값일 수 있다. 로그에 그대로 남기며 실패로 보지 않는다
- 해석 불가 토픽 로그는 서버→장치 명령 토픽에도 남아, 페이로드가 더해지면 로그 양이 늘어난다. 4096바이트 상한으로 한 항목 크기를 제한하고 나머지는 수용한다(설계 문서 0002에서 이미 수용한 로그 양 증가)
- UTF-8이 아닌 바이트는 대체 문자로 디코딩된다. 예외는 나지 않는다
- 4096바이트 경계에서 여러 바이트 문자가 잘리면 끝이 대체 문자가 된다(결정 참조)
- Will 메시지에서 `ClientId`가 채워지지 않으면 C5의 Will 테스트가 실패한다. 그때는 3절 3번대로 의도 변경 여부를 판단한다

## 검증 전략

- C1: 새 테스트 `UnknownOperation_LogsClientIdAndRawPayload` — 고정 ClientId로 `{"sid":"0","msg":{"o":"n","e":[{"n":"/1/0/0","v":1}]}}`(`sv` 없는 필드 포함)를 발행한 뒤 같은 클라이언트로 `rg`를 발행해 처리 완료를 기다리고, 앱을 멈춘 뒤 로그에 `Unknown device operation`, ClientId, 토픽, 이스케이프되지 않은 원본 페이로드 문자열이 있는지 확인
- C2: 새 테스트 `WillOnUnparsedLwtTopic_LogsClientIdAndPayload` — 원시 TCP로 Will 토픽 `/lwt`, 페이로드 `offline`, 고정 ClientId인 MQTT 3.1 CONNECT 후 끊고, `WaitForLogAsync`로 `Unparsed topic passed through: "/lwt"`와 그 ClientId·`payload=offline`을 확인. 핸들러 호출이 없음도 확인
- C3: 새 테스트 `UnparsedTopic_LargePayload_IsTruncatedTo4096Bytes` — 해석 불가 토픽에 ASCII 5000바이트를 발행하고, 로그에 `bytes=5000`, `truncated=True`, 4096자 연속 문자열이 있고 4097자 연속 문자열은 없음을 확인. C1·C2 테스트에서 `truncated=False`도 확인
- C4: `git -C "$W" diff 1517b24 -- src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs`에서 `RegisterAsync`·`DeregisterAsync`·`TimeSyncAsync`·`NotifyAsync`·`OfflineAsync` 줄과 `TryDeserialize`가 바뀌지 않았음을 확인. `dotnet test`로 기존 테스트 통과 확인
- C5: `dotnet test` 전체 실행 결과로 새 테스트 3개 통과 확인

## 원칙 점검

- 적용 규칙: `workflow/spec-driven.md`(절차·기준 spec), `git/github-issue.md`(이슈 #12), `dotnet/server-spa-template.md`(Serilog Console+일별 File 로깅 구성 유지), `shell/powershell-windows.md`(검증 명령), `git/commit-message.md`(커밋 시)
- 충돌: 없음. 로깅 구성(`ServerLoggingExtensions`)은 바꾸지 않고 메시지 템플릿만 바꾼다
