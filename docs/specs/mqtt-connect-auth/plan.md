# MQTT 접속 인증 강제 계획

## 요약

`MqttBrokerHostedService.OnValidatingConnectionAsync`가 지금 만드는 R9 판정에 따라 `ValidatingConnectionEventArgs.ReasonCode`를 정한다. `Match`면 그대로 성공으로 두고, `Mismatch`·`Unknown`이면 `BadUserNameOrPassword`, 확인 중 예외면 `ServerUnavailable`로 둔다. 판정 로그(R9)는 지금 형식 그대로 접속마다 하나 남긴다. 설정 항목은 더하지 않는다.

기존 브로커 테스트는 대부분 임의 ClientId(`test-…`, `subscriber`, `writer` 등)로 비밀번호 없이 접속하므로 강제 후에는 모두 거부된다. 테스트용 장치 등록 도우미를 새로 두고, 접속하는 테스트 클라이언트마다 장치를 등록해 그 ClientId·키로 접속하게 고친다. 원시 TCP로 보내는 MQTT 3.1 CONNECT에도 사용자명·비밀번호를 넣는다. 인증 동작은 새 테스트로 확인한다. 마지막으로 기준 spec R1·R9를 고치고, 배포본에서 B540 실장비 로그로 C8을 확인한다.

## 바꿀 파일·구조

- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs`: `OnValidatingConnectionAsync`에서 판정마다 `e.ReasonCode`를 정한다(아래 인터페이스). 예외 때 Error 로그를 남기고 `ServerUnavailable`. 클래스 주석의 "접속 인증은 하지 않으며"와 메서드 주석의 "접속은 모두 받는다"를 새 동작으로 고친다.
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TestDevices.cs`(새 파일): `DeviceRegistrationService.RegisterAsync`로 mac을 등록하고 ClientId `DAWONDNS-{mac}`, 사용자명 `DAWONDNS-B540_W-{mac}`, 비밀번호(발급 `mqtt_key`)를 돌려주는 도우미와, `MqttClientOptionsBuilder`에 그 ClientId·자격 증명을 넣는 확장 메서드.
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs`:
  - 기존 테스트의 접속을 등록 장치로 바꾼다. 로그 문자열에 쓰던 ClientId(`unknown-op-test`, `lwt-raw-test` 등)는 등록 장치 ClientId로 바꾼다.
  - `BuildMqtt31Connect`에 사용자명·비밀번호 인자를 더해 CONNECT 플래그(사용자명 0x80·비밀번호 0x40)와 페이로드에 넣는다.
  - `ClientNamedPowerManager_PublishToDownlink_LogsUnparsedTopic`은 ClientId `power-manager`로는 이제 접속할 수 없으므로, 등록 장치 클라이언트가 서버 → 장치 토픽에 PUBLISH하면 R7 로그가 남는지로 바꾼다(아래 결정).
  - `ValidatingConnection_LogsPasswordCheckAndAcceptsAll`을 인증 테스트로 바꾸고, 예외·원시 CONNACK·MQTT 3.1.1 테스트를 더한다(검증 전략).
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttSubscribeLogTests.cs`: 접속 클라이언트를 등록 장치로 바꾸고, 로그 문자열의 ClientId(`sub-client`, `unsub-client`, `sub-a`, `sub-b`, `writer`, `uplink-watcher`)를 등록 장치 ClientId로 바꾼다.
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TimeSyncResponseTests.cs`: `subscriber`·`late-subscriber`·장치 클라이언트를 등록 장치로 바꾸고, 큐 넣음 로그 문자열의 받는 ClientId를 맞춘다. 등록에 앱 서비스가 필요하므로 `SendTimeSyncAsync`가 `WebApplication`을 받게 한다.
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttPacketFragmentationTests.cs`: 원시 TLS 클라이언트(`fragtest-raw`)와 장치 클라이언트를 등록 장치로 바꾸고, `BuildMqtt31Connect`에 사용자명·비밀번호를 넣는다.
- `docs/features/mqtt-broker.md`: R1(접속 인증 문구), R9(거부·반환 코드·예외 때 판정 `Unknown`·거부한 접속도 로그), 머리말, `파일:` 줄(`TestDevices.cs` 추가), 이력 한 줄(C9).

## 결정

- 반환 코드는 MQTTnet 5의 `MqttConnectReasonCode`로 정한다: `Mismatch`·`Unknown` → `BadUserNameOrPassword`, 예외 → `ServerUnavailable`. MQTTnet이 MQTT 3.1·3.1.1 클라이언트에는 이를 CONNACK 반환 코드 4·3으로 바꿔 보낸다 / 검토한 대안: `NotAuthorized`(3.1.1 코드 5) — spec C2·C3이 코드 4(잘못된 사용자명 또는 비밀번호)를 정해 고르지 않음
- 판정 로직은 지금 함수 안에서 판정 문자열과 반환 코드를 함께 정한다(별도 클래스로 떼지 않음): 바뀌는 줄이 적고, 판정은 이미 R9 테스트로 확인하는 구조라 / 검토한 대안: 순수 판정 함수를 `Core`로 분리 — 판정에 DB 조회가 끼어 있어 분리 이득이 작음
- 테스트 클라이언트는 모두 등록 장치로 접속한다(테스트 전용 인증 우회를 두지 않음): C5가 설정과 상관없이 늘 강제를 요구하므로 우회 경로를 만들지 않는다 / 검토한 대안: 테스트에서 `ValidatingConnectionAsync`를 바꿔 끼우기 — 실제 인증 경로를 거치지 않는 테스트가 생겨 고르지 않음
- `power-manager` ClientId 테스트는 등록 장치 ClientId의 PUBLISH로 바꾼다: injected-message-log C2의 뜻(주입 판정이 ClientId가 아니라 `CustomSessionItems`로 한다, 클라이언트 PUBLISH는 R7 로그에 남는다)은 그대로 확인되고, `power-manager`는 `DAWONDNS-{mac}` 형식이 아니라 이제 접속 자체가 거부되기 때문이다. 이 경우는 인증 테스트의 `Unknown` 사례로 넣는다. 기준 spec R7 문장("ClientId와 상관없이(ClientId가 `power-manager`여도)")은 여전히 참이므로 바꾸지 않는다
- 예외(C4)는 시작한 앱의 DB에서 `Devices` 테이블을 지워(`DROP TABLE Devices`) `FindMqttKeyAsync`가 SQLite 예외를 내게 해 일으킨다: 운영 코드에 시험용 주입점을 더하지 않는다 / 검토한 대안: `IDbContextFactory`를 예외를 내는 장식자로 바꿔 끼우기 — 시작 시 마이그레이션도 같은 팩터리를 써서 켜고 끄는 조건이 더 필요해 고르지 않음

## 인터페이스·데이터

`OnValidatingConnectionAsync` 처리(판정 정의는 R9 그대로):

| 상황 | 판정 로그 | `e.ReasonCode` | 3.1·3.1.1 CONNACK |
|---|---|---|---|
| 형식 맞음·등록됨·비밀번호 같음 | `Match` | 바꾸지 않음(`Success`) | 0 |
| 형식 맞음·등록됨·비밀번호 다르거나 없음 | `Mismatch` | `BadUserNameOrPassword` | 4 |
| 형식 아님 또는 미등록 | `Unknown` | `BadUserNameOrPassword` | 4 |
| `TryParseClientId`·`FindMqttKeyAsync` 중 예외 | `Unknown` | `ServerUnavailable` | 3 |

- 예외 때는 지금처럼 `MQTT connection credential check failed: {ClientId}` Error 로그를 남긴다.
- 판정 로그 형식은 그대로다: `MQTT client connecting: clientId=…, userName=…, hasPassword=…, passwordCheck=…`.
- 설정(`AppConfiguration`)은 바꾸지 않는다.

테스트 도우미(`TestDevices`):

```csharp
internal sealed record TestDevice(string Mac, string ClientId, string UserName, string Password);

internal static class TestDevices
{
    // mac(12자리 16진수 소문자)을 등록하고 발급 키를 비밀번호로 담아 돌려준다.
    public static Task<TestDevice> RegisterAsync(WebApplication app, string mac);
    // ClientId·사용자명·비밀번호를 넣는다.
    public static MqttClientOptionsBuilder WithDevice(this MqttClientOptionsBuilder builder, TestDevice device);
}
```

## 오류·위험

- 등록하지 않은 실장비(예: V1으로 설정한 M130-W)와 시험 클라이언트는 배포 즉시 접속하지 못한다. spec D001에 따른 의도한 결과다(spec 검토 1회차에서 확인하지 않기로 정함).
- 장치는 거부되면 재접속을 되풀이한다. 접속마다 R9 로그가 하나씩 쌓이므로 거부된 장치가 많으면 로그가 늘어난다. 이번 범위에서 줄이지 않는다.
- MQTTnet의 반환 코드 변환(`BadUserNameOrPassword` → 4, `ServerUnavailable` → 3)은 패키지 소스로 확인하지 못했다. 원시 TCP로 받은 CONNACK 바이트를 테스트에서 직접 확인해 이 전제를 검증한다. 다르면 구현을 고친다(구현 결함).
- 서버 로그로 실장비를 확인하는 C8은 배포가 필요하다. 배포는 #25와 같은 방식(서버 중지·시작은 사용자, 백업·복사는 에이전트, 설정·DB·`secrets.dek`·인증서·log는 덮어쓰지 않음)으로 한다.

## 검증 전략

- C1: `MqttBrokerTests` 인증 테스트에서 `Match` 클라이언트(ClientId 대문자 mac 포함)의 접속 결과가 `Success`. 기존 발행·분배·LWT·R7 로그 테스트와 TLS 1.2 테스트(`Mqtts_Tls12_PublishRegister_CallsRegister_AndTls13OnlyFails`)를 등록 장치로 접속해 통과(비TLS·TLS, 발행·구독). `MqttSubscribeLogTests`의 구독·전달 테스트 통과.
- C2: 인증 테스트에서 틀린 비밀번호·비밀번호 없음의 MQTTnet V310 접속 결과가 `BadUserNameOrPassword`. 원시 TCP MQTT 3.1 CONNECT(틀린 비밀번호)의 CONNACK 넷째 바이트가 `0x04`. V311 클라이언트(틀린 비밀번호)도 `BadUserNameOrPassword`.
- C3: 미등록 mac, `client-not-device`, `power-manager` ClientId의 접속 결과가 `BadUserNameOrPassword`. 원시 TCP(미등록 mac)의 CONNACK 넷째 바이트가 `0x04`.
- C4: 새 테스트에서 장치를 등록하고 `DROP TABLE Devices` 뒤 접속하면 결과가 `ServerUnavailable`이고, 원시 TCP CONNACK 넷째 바이트가 `0x03`이며, 로그에 `MQTT connection credential check failed`와 그 ClientId의 `passwordCheck="Unknown"` 판정 로그가 있다.
- C5: 구현 후 diff에서 `AppConfiguration`·설정 파일에 바뀐 곳이 없음을 확인한다. 테스트는 기본 설정(`TestApp.Configuration`)에서 C1~C4를 확인한다.
- C6: 인증 테스트의 경우마다(거부 포함) 판정 로그 한 줄을 확인하고, 로그 전체에 발급 키·틀린 비밀번호 문자열이 없음을 확인한다. C4 테스트에서 판정 `Unknown` 확인.
- C7: `TimeSyncResponseTests`(구독 장치가 응답을 받음, 큐 넣음 로그 `sender="power-manager"`)와 `MqttPacketFragmentationTests`(TLS 구독자가 응답을 받음)가 등록 장치로 통과.
- C8: Release 빌드를 서버 로컬 배포 폴더에 배포하고 사용자가 서버를 시작한 뒤, 서버 로그에서 B540 실장비 ClientId의 `passwordCheck="Match"` 판정 로그, 그 장치 키의 `Time sync response sent` 로그, 받는 쪽이 그 ClientId인 `Downlink message enqueued` 로그를 찾아 작업 문서에 적는다.
- C9: 기준 spec diff로 R1·R9·머리말·`파일:` 줄·이력 반영을 확인한다.
- 전체: `dotnet build` 경고 0, `dotnet test` 실패 없음.

## 원칙 점검

- 적용 규칙:
  - workflow/spec-driven.md: 변경 문서, 기준 spec 반영(마지막 구현 단계), 네 검토, 커밋·push 승인
  - workflow/repository-content.md: 저장소 파일·커밋에 로컬 경로·개인정보·실제 키를 쓰지 않음(테스트는 예시 mac과 실행 중 발급한 키만 씀)
  - git/github-issue.md: 이슈 #26 참조, 종결 조건
  - 전역 language-preference: 대화는 한국어, 코드 주석·테스트 메시지는 저장소 관례(한국어 주석)
  - 사용자 메모: 실장비 상태를 바꾸는 명령 전에 대상 확인(이 작업은 장비에 명령을 보내지 않고 서버 로그만 읽음), 배포 때 설정·DB·비밀·인증서·log 덮어쓰지 않음
- 충돌: 없음
