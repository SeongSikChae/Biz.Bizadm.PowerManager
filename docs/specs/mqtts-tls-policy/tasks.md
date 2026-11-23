# MQTTS TLS 버전·암호 목록 적용 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 암호 목록 해석 분리(Server.Core)
- [x] T001 `ResolveCipherSuites(string?, ILogger, bool warnIfNoTls13, bool isWindows)`를 `internal`로 추가(공백 확인 → `isWindows`면 Windows 경고 후 `null` → 이름 해석·잘못된 이름 경고 → `warnIfNoTls13`이면 HTTP/3 경고 → 유효한 이름이 없으면 `null`). `CreateCipherSuitesPolicy`를 `public`으로 바꾸고 `bool warnIfNoTls13 = true`를 더해 `ResolveCipherSuites(…, OperatingSystem.IsWindows())` 뒤 `if (suites is null || OperatingSystem.IsWindows()) return null;` 다음에만 정책 생성. `ConfigureHttps` 호출은 그대로 (src/Biz.Bizadm.PowerManager.Server.Core/ServerKestrelExtensions.cs) — C3·C4·C5
- [x] T002 `ResolveCipherSuites` 검사 추가: `isWindows: false`로 잘못된 이름 경고·제외, 남은 이름 없으면 `null`, `warnIfNoTls13: false`면 TLS 1.2 암호만 든 목록에도 HTTP/3 경고 없음, `true`면 경고 있음, 공백뿐이면 `null`·경고 없음, `isWindows: true`면 이름을 해석하지 않고 Windows 경고 하나 (tests/Biz.Bizadm.PowerManager.Server.Core.Tests/ServerKestrelExtensionsTests.cs) — C3·C4·C5

체크포인트: `dotnet build -warnaserror`(솔루션) 경고 0·오류 0, `dotnet test tests/Biz.Bizadm.PowerManager.Server.Core.Tests` 전체 통과(기존 `KestrelTlsTests`·`ServerKestrelExtensionsTests` 포함)

## 단계 2: MQTTS 옵션(Server.Spa)
- [x] T003 `BuildServerOptions(AppConfiguration, X509Certificate2?, ILogger)`로 로거 인자 추가, 인증서가 있으면 `WithEncryptionSslProtocol(SslProtocols.Tls12 | SslProtocols.Tls13)`, `Build()` 뒤 `CreateCipherSuitesPolicy(configuration.Tls?.IncludeCipherSuites, logger, warnIfNoTls13: false)`가 `null`이 아니면 `options.TlsEndpointOptions.CipherSuitesPolicy`에 넣음(인증서가 없으면 정책을 만들지 않음). `StartAsync`에서 `logger`를 넘기고, 클래스·메서드 주석의 "TLS 1.2 엔드포인트"·"체인·cipher는 적용하지 않는다"를 고침 (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs) — C1·C2·C3·C4·C5
- [x] T004 `BuildServerOptions` 호출에 `NullLogger.Instance` 추가 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttPacketFragmentationTests.cs) — C5
- [x] T005 [P] 새 테스트: 인증서 있음일 때 `TlsEndpointOptions.SslProtocol == Tls12 | Tls13`, 목록 없음·빈 값·공백뿐이면 정책 `null`·경고 없음, Windows면 목록이 있어도 정책 `null`·HTTPS와 같은 문구의 경고 하나·TLS 엔드포인트 켜짐, Windows가 아니면 정책 null 아님(OS 분기), TLS를 켠 서버에 `SslStream`으로 TLS 1.2·1.3 핸드셰이크 성공(TLS 1.0·1.1 거절은 T009에서만 확인) (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttTlsPolicyTests.cs) — C1·C2·C3·C4
- [x] T006 [P] `Mqtts_Tls12_PublishRegister_CallsRegister_AndTls13OnlyFails`의 TLS 1.3 기대를 접속 성공으로 바꾸고 TLS 1.3 클라이언트를 `ClientOptions(port, device)`로 접속, 테스트 이름을 맞춤 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs) — C1

체크포인트: `dotnet build -warnaserror` 경고 0·오류 0, `dotnet test`(솔루션 전체) 통과

## 단계 3: 기준 spec 반영
- [x] T007 R1의 "TLS 1.2 고정 엔드포인트"를 TLS 1.2·1.3으로 바꾸고, R18(MQTTS 암호 목록: C2~C4의 내용)을 추가, `파일:` 줄에 `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttTlsPolicyTests.cs` 추가, 이력에 `mqtts-tls-policy` 한 줄(R1 바꿈, R18 추가) (docs/features/mqtt-broker.md) — C1·C2·C3·C4

체크포인트: 기준 spec의 R1·R18이 spec.md C1~C4와 맞고, `파일:` 줄의 경로가 모두 있음

## 단계 4: 리눅스 서버 배포 확인(D004)
- [x] T008 linux-x64 게시본을 만들고, 사용자 승인 뒤 리눅스 서버 `/PowerManager`에서 실행 파일만 교체(설정·DB·인증서·로그·`secrets.dek` 유지, 교체 전 백업)하고 서비스 재시작 — C6
- [x] T009 리눅스 서버에서 `openssl s_client -connect 127.0.0.1:8883`로 `-tls1_2 -cipher AES256-SHA256`·`AES128-SHA256` 성공, `-tls1_3` 성공, `-tls1_2 -cipher ECDHE-RSA-AES128-SHA`·`AES128-SHA` 실패, `-tls1 -cipher 'AES128-SHA:@SECLEVEL=0'`과 `-tls1_1 -cipher 'AES128-SHA:@SECLEVEL=0'`이 서버 경고(`alert protocol version`)로 실패(클라이언트 쪽 실패면 판정 못함으로 적음) 확인. 서버 로그에서 B540(`DAWONDNS-365f45501f96`) 재접속의 `passwordCheck="Match"`와 구독 로그 확인 — C1·C2·C6

체크포인트: T009 결과를 작업 문서 `## 검증`에 C번호와 함께 적음

## 의존 관계
- T001 → T003: MQTTS가 `public CreateCipherSuitesPolicy(…, warnIfNoTls13)`를 부르므로 먼저 있어야 한다.
- T001 → T002: 검사할 `ResolveCipherSuites`가 먼저 있어야 한다.
- T003 → T004·T005·T006: `BuildServerOptions` 시그니처와 TLS 1.3 수락이 바뀐 뒤에 테스트가 컴파일·통과한다.
- T005·T006은 서로 다른 파일이라 동시에 할 수 있다.
- T007은 단계 1·2의 동작이 정해진 뒤에 쓴다(구현 후 검토 diff에 함께 들어가게 4절 검증 전에 한다).
- T008·T009는 단계 1~3과 단계 2 체크포인트 통과 뒤에 한다. T008의 교체는 사용자 승인을 받은 뒤에만 한다.
