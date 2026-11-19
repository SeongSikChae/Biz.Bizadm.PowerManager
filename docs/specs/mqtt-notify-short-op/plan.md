# M130-W 상태 보고(msg.o "n")를 Notify로 분배 계획

## 요약

`MqttOperation`에 `n` 상수를 더하고, `MqttBrokerHostedService.OnPublishAsync`의 분배 `switch`에서 `notify`와 같은 분기로 `NotifyAsync`에 보낸다. 기존 알 수 없는 오퍼레이션 테스트는 `n` 대신 다른 값을 쓰게 바꾸고, M130-W 형태 페이로드와 `notify` 분배를 확인하는 테스트를 더한다. 근거 문서(`docs/.tmp`, 로컬 전용)에 M130-W 관찰 절을 더하고, 기준 spec의 R4·R6과 이력을 고친다.

## 바꿀 파일·구조

- `src/Biz.Bizadm.PowerManager.Core/Mqtt/MqttOperation.cs`: 상수 `NotifyShort = "n"` 추가(XML 주석에 M130-W 관찰, `notify`의 짧은 형태)
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs`: 분배 `switch`의 `MqttOperation.Notify` 분기를 `MqttOperation.Notify or MqttOperation.NotifyShort`로 바꿈
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs`:
  - `RecordingHandler`가 `NotifyAsync`로 받은 `MqttEnvelope`를 `ConcurrentQueue<MqttEnvelope> Notified`에 모은다(`Calls`에는 지금처럼 `"notify"`).
  - `UnknownOperation_LogsClientIdAndRawPayload`의 오퍼레이션을 `n`에서 `zz`로 바꾼다(기대 로그 문자열도).
  - 새 테스트 `M130NotifyShortOperation_DispatchesToNotify`: M130-W 형태 페이로드(`n`)와 `notify` 페이로드를 차례로 발행하고 `rg`로 처리 완료를 확인한다.
- `docs/.tmp/b540-mqtt-topic.md`(.gitignore 대상, 로컬 전용): M130-W 관찰 절 추가(의미는 추정, 모르는 리소스는 미확인), 3절 오퍼레이션 표 `n` 행 추가(관찰 위치 열에 덤프 오프셋 대신 "M130-W 실통신", 함수 이름 열은 "—"), 머리말 근거 줄·표기 줄(관찰 = 덤프 또는 실장비 통신 로그에 직접 존재)·4절 미확인 항목 정합
- `docs/features/mqtt-broker.md`(기준 spec): 설명 문단, R4·R6 갱신, 이력 한 줄 추가. `파일:` 줄은 바꾸지 않음(바꾸는 코드·테스트 파일이 이미 들어 있고 새 파일이 없다)

## 결정

- `n`을 상수로 더하고 `or` 패턴으로 같은 분기에 둔다: 분배 대상이 같고, 핸들러에는 원래 오퍼레이션 값(`envelope.Msg.Operation`이 `n`)이 그대로 전달돼 로그(`LoggingDeviceMessageHandler`)에서 구분된다. / 검토한 대안: 역직렬화 뒤 `n`을 `notify`로 바꿔 넣기 — 원래 값이 사라져 장치별 차이를 로그에서 볼 수 없어 고르지 않음. `IDeviceMessageHandler`에 별도 메서드 추가 — 처리 내용이 같아 인터페이스만 늘어 고르지 않음.
- 상수 이름 `NotifyShort`: 같은 의미의 짧은 형태임을 드러낸다. / 대안: `NotifyM130` — 다른 모델도 같은 형태일 수 있어 고르지 않음.
- 알 수 없는 오퍼레이션 테스트 값 `zz`: 근거 문서·코드의 어느 오퍼레이션(`rg`·`drg`·`tisyn`·`notify`·`n`·`w`·`chg`·`reb`)과도 겹치지 않는다.
- 근거 문서는 로컬 전용이라 커밋 diff에 나오지 않는다. 구현 후 검토에는 수정 전 사본(plan 작성 직후, 근거 문서를 고치기 전에 스크래치에 복사. 경로는 작업 문서에 적음)과의 `git diff --no-index` 결과를 함께 넘긴다.

## 인터페이스·데이터

- `MqttOperation.NotifyShort = "n"`(공개 상수 추가). `IDeviceMessageHandler`, `MqttEnvelope`, `MqttElement`는 바꾸지 않는다(`ti`는 넣지 않음, 모르는 필드로 무시).
- 새 테스트 페이로드(줄바꿈 포함, 값은 이슈 #14 관찰 형태):

  ```json
  {
    "sid":"1",
    "msg":{"o":"n","e":[
      {"n":"/100/0/1","sv":"228.30","ti":""},
      {"n":"/100/0/0","sv":"true","ti":""},
      {"n":"/99/0/5","sv":"3","ti":""}
    ]}
  }
  ```

  토픽은 `dwd.v1/DAWONDNS-M130-W-2ef4322ee918/iot-server/notify/json`, ClientId는 `DAWONDNS-2ef4322ee918`.

## 오류·위험

- `n`이 다른 펌웨어에서 다른 의미일 위험: 지금 관찰은 M130-W 세 대(펌웨어 `2.01.58`)뿐이다. 핸들러가 로그만 남기므로 잘못 분배돼도 부작용이 없고, 원래 오퍼레이션 값이 로그에 남는다.
- `n`은 더 이상 원본 페이로드 경고(R6)를 남기지 않아 `ti` 등 매핑하지 않는 필드를 로그로 볼 수 없게 된다: spec에서 받아들인 결과이며 관찰은 근거 문서에 남긴다.
- 테스트 순서 의존: 같은 클라이언트의 QoS 1 발행이 순서대로 처리된다는 기존 테스트의 전제(`rg` 수신으로 앞 메시지 처리 완료 확인)를 그대로 쓴다.

## 검증 전략

- C1: 새 테스트가 `n` 메시지와 `notify` 메시지에서 `Calls`가 `notify, notify, rg`이고 `Notified`의 오퍼레이션이 `n`, `notify` 순서인지 확인.
- C2: 새 테스트가 로그에 `Unknown device operation`이 없음을 확인. 바꾼 `UnknownOperation_LogsClientIdAndRawPayload`가 `zz`에 R6 경고(토픽·ClientId·원본 페이로드)가 남음을 확인.
- C3: 새 테스트가 M130-W 형태 페이로드에서 로그에 `not valid JSON`·`no msg.o or msg.e`가 없고, `Notified` 첫 envelope의 요소 경로·값이 페이로드와 같음을 확인.
- C4: 근거 문서를 읽어 관찰 절·`n` 행·머리말·4절을 확인하고, 수정 전 사본과의 diff를 본다.
- C5: 기준 spec을 읽어 R4·R6·이력 확인.
- C6: `dotnet test`로 솔루션 테스트 전체 실행, 실패 0.

## 원칙 점검

- 적용 규칙: `workflow/spec-driven.md`(절차·기준 spec), `git/github-issue.md`(이슈 #14), `workflow/conversation-language.md`·전역 언어 규칙(대화는 한국어, 코드·주석은 저장소 관례대로 주석 한국어), `dotnet/project-creation.md`(MSTest 유지, 새 프로젝트 없음), `dotnet/server-spa-template.md`(로깅 구성 유지), `shell/powershell-windows.md`(검증 명령), `git/commit-message.md`(커밋 시)
- 충돌: 근거 문서 `docs/.tmp/b540-mqtt-topic.md`가 .gitignore 대상이라 기준점·구현 후 검토 git diff에 들어가지 않는다(spec-driven 5절은 기준점부터의 diff 전체를 요구). 사용자가 그 파일에 기록하기로 정했으므로 따르지 않는 대신, 수정 전 사본과의 diff를 구현 후 검토에 따로 넘겨 검토 범위에 넣는다.
