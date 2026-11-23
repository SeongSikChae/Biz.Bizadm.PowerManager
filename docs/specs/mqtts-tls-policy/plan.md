# MQTTS TLS 버전·암호 목록 적용 계획

## 요약

MQTTS 엔드포인트 옵션을 만드는 `MqttBrokerHostedService.BuildServerOptions`에서 TLS 버전을 `Tls12 | Tls13`으로 바꾸고, HTTPS가 쓰는 암호 목록 해석 함수(`ServerKestrelExtensions.CreateCipherSuitesPolicy`)로 만든 정책을 MQTTnet TLS 엔드포인트 옵션(`MqttServerTlsTcpEndpointOptions.CipherSuitesPolicy`)에 넣는다. 해석 함수에는 HTTP/3 경고를 끄는 인자를 더해 MQTTS에서는 그 경고를 남기지 않는다(D005). 테스트로 옵션 값·경고·TLS 1.2/1.3 핸드셰이크를 확인하고, 리눅스 서버 배포로 TLS 1.0/1.1 거절, 실제 암호 제한과 실장비 접속을 확인한다(D004·D012).

## 바꿀 파일·구조
- src/Biz.Bizadm.PowerManager.Server.Core/ServerKestrelExtensions.cs: 해석·경고 부분을 `internal static IReadOnlyCollection<TlsCipherSuite>? ResolveCipherSuites(string? includeCipherSuites, ILogger logger, bool warnIfNoTls13, bool isWindows)`로 나눈다(D007). 공백 확인 → Windows 경고(`isWindows`일 때) → 이름 해석(잘못된 이름 경고) → `warnIfNoTls13`이면 HTTP/3 경고 → 유효한 이름이 없으면 `null` 순서는 지금과 같다. `CreateCipherSuitesPolicy`를 `public`으로 바꾸고 인자 `bool warnIfNoTls13 = true`를 더해, `ResolveCipherSuites(…, OperatingSystem.IsWindows())`를 부른 뒤 `if (suites is null || OperatingSystem.IsWindows()) return null;` 뒤에서만 `CipherSuitesPolicy`를 만든다(Windows에서는 정책 객체를 만들지 않음. 생성 앞의 OS 확인은 플랫폼 분석기 CA1416 보호, D009). HTTPS 호출(`ConfigureHttps`)은 인자를 넘기지 않아 동작이 같다.
- src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs: `BuildServerOptions(AppConfiguration, X509Certificate2?, ILogger)`로 로거 인자를 더한다. 인증서가 있을 때 `WithEncryptionSslProtocol(SslProtocols.Tls12 | SslProtocols.Tls13)`로 바꾸고, `Build()` 뒤 `CreateCipherSuitesPolicy(configuration.Tls?.IncludeCipherSuites, logger, warnIfNoTls13: false)`가 `null`이 아니면 `options.TlsEndpointOptions.CipherSuitesPolicy`에 넣는다. 인증서가 없으면(TLS 끔) 정책을 만들지 않아 경고도 없다. `StartAsync`는 자기 `logger`를 넘긴다. 주석("체인·cipher는 적용하지 않는다", "TLS 1.2 엔드포인트")을 고친다.
- tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttPacketFragmentationTests.cs: `BuildServerOptions` 호출에 로거 인자(`NullLogger.Instance`)를 넣는다.
- tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttTlsPolicyTests.cs(새 파일): `BuildServerOptions` 옵션 검사와 MQTTS 핸드셰이크 검사(검증 전략 참조).
- tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs: `Mqtts_Tls12_PublishRegister_CallsRegister_AndTls13OnlyFails`의 TLS 1.3 거절 기대를 접속 성공으로 바꾸고 이름을 맞춘다(C1). TLS 1.3 클라이언트도 `ClientOptions(port, device)`(등록 장치)로 접속해 접속 성공을 확인한다. 지금처럼 `ClientOptions(port)`로 접속하면 TLS와 상관없이 접속 인증(mqtt-broker R9)에서 거부된다.
- tests/Biz.Bizadm.PowerManager.Server.Core.Tests/ServerKestrelExtensionsTests.cs: `ResolveCipherSuites`를 `isWindows: false`로 불러 이 PC에서도 리눅스 경로를 확인한다. 잘못된 이름은 경고하고 빼는지, `warnIfNoTls13: false`면 TLS 1.2 암호만 든 목록에도 HTTP/3 경고가 없는지, `true`면 그대로 있는지, 공백뿐이면 `null`·경고 없음인지, `isWindows: true`면 이름을 해석하지 않고 Windows 경고 하나만 남기는지 검사를 더한다(C3·C4·C5).
- docs/features/mqtt-broker.md: R1의 "TLS 1.2 고정 엔드포인트"를 C1대로 바꾸고 R18(MQTTS 암호 목록, C2~C4)을 더한다. `파일:` 줄에 새 테스트 파일을 넣고 이력에 한 줄을 더한다.

## 결정
- 해석 함수 재사용: HTTPS와 MQTTS가 같은 규칙(C3·C4)으로 목록을 해석해야 해서 `ServerKestrelExtensions.CreateCipherSuitesPolicy`를 그대로 부른다. Server.Spa가 Server.Core의 `internal` 함수를 부를 수 없어 `public`으로 바꾼다. / 검토한 대안: Server.Core에 `InternalsVisibleTo Server.Spa` 추가(제품 어셈블리끼리 내부를 여는 것이라 고르지 않음), MQTTS용 해석 함수를 따로 작성(같은 규칙이 두 곳에 생겨 어긋날 수 있어 고르지 않음)
- HTTP/3 경고 끄기: 함수에 `warnIfNoTls13` 인자(기본 `true`)를 더해 HTTPS 호출은 바꾸지 않는다(D005). / 검토한 대안: 경고를 HTTPS 쪽 호출 뒤로 옮김(HTTPS 경로를 고치게 돼 C5 범위가 커져 고르지 않음)
- 해석·경고와 정책 생성 분리: Windows에서는 `CipherSuitesPolicy`를 만들 수 없어 리눅스 경로를 이 PC에서 실행할 수 없으므로, OS 판단을 인자(`isWindows`)로 받는 해석 함수를 따로 둬 테스트에서 리눅스 경로를 실행한다(D007). / 검토한 대안: C3의 리눅스 분기를 확인 못함으로 적음(D005가 확인되지 않은 채 남아 고르지 않음)
- 정책 생성 앞 보호 구문: `CipherSuitesPolicy` 형식에 `[UnsupportedOSPlatform("windows")]`가 붙어 있어, OS 판단을 인자로만 넘기면 플랫폼 분석기가 CA1416을 내 `-warnaserror` 빌드가 실패한다(plan 검토 2회차 검토자가 시험 프로젝트로 재현). 생성 바로 앞에 `OperatingSystem.IsWindows()` 확인을 다시 둔다(`suites is null || OperatingSystem.IsWindows()` 형태가 `-warnaserror`에서 경고 0·오류 0으로 빌드됨을 시험 프로젝트로 확인, D009). / 검토한 대안: `[UnsupportedOSPlatform("windows")]` 도우미(부르는 곳에 보호 구문이 여전히 필요해 고르지 않음), `#pragma warning disable CA1416`(Windows에서 정책을 만드는 실수를 분석기가 잡지 못해 고르지 않음)
- 정책 적용 시점: MQTTnet 빌더에 `CipherSuitesPolicy`를 넣는 메서드가 없으므로(MQTTnet.Server 5.2.0.1603 DLL에서 MQTTnet 쪽 이름은 속성 이름과 `get_`·`set_` 접근자 셋뿐이고, 그 밖은 형식 참조 `System.Net.Security.CipherSuitesPolicy`와 `SslServerAuthenticationOptions.set_CipherSuitesPolicy` 호출 참조다. D008) `Build()`가 만든 `MqttServerOptions.TlsEndpointOptions`의 속성에 직접 넣는다.
- 로거 인자: `BuildServerOptions`는 정적 함수라 경고를 남기려면 로거를 받아야 한다. 선택 인자로 두지 않고 필수로 바꿔 경고가 빠지는 호출이 생기지 않게 한다. 지금 호출하는 곳은 `StartAsync`와 기존 테스트 하나(`MqttPacketFragmentationTests`)다.
- 체인(`CertificateChain`)은 다루지 않는다(D003).

## 인터페이스·데이터
- `public static CipherSuitesPolicy? ServerKestrelExtensions.CreateCipherSuitesPolicy(string? includeCipherSuites, ILogger logger, bool warnIfNoTls13 = true)`
- `internal static IReadOnlyCollection<TlsCipherSuite>? ServerKestrelExtensions.ResolveCipherSuites(string? includeCipherSuites, ILogger logger, bool warnIfNoTls13, bool isWindows)`: 제한하지 않으면(공백, Windows, 유효한 이름 없음) `null`
- `public static MqttServerOptions MqttBrokerHostedService.BuildServerOptions(AppConfiguration configuration, X509Certificate2? certificate, ILogger logger)`
- 설정 형식은 바뀌지 않는다(`Tls.IncludeCipherSuites`: 쉼표 구분 `TlsCipherSuite` 이름).

## 오류·위험
- MQTTnet이 엔드포인트 옵션의 정책을 TLS 연결에 넘기는지: DLL 메타데이터상 `TryHandleClientConnectionAsync`가 `TlsEndpointOptions.CipherSuitesPolicy`를 `SslServerAuthenticationOptions.CipherSuitesPolicy`에, `SslProtocol`을 `EnabledSslProtocols`에 넣고 `SslStream.AuthenticateAsServerAsync`를 부른다(plan 검토 1회차 검토자가 REA로 IL 확인, D008). 실행해 확인하지는 않았으므로, C6에서 `openssl s_client -cipher ECDHE-RSA-AES128-SHA`가 연결되면 넘기지 않는 것으로 알아채고 3절 3번으로 설계를 다시 본다.
- Windows에서 `CipherSuitesPolicy`를 만들면 `PlatformNotSupportedException`이 난다(.NET 동작, 기존 `CreateCipherSuitesPolicy`가 Windows에서 `null`을 돌려주는 이유). 같은 함수를 쓰므로 Windows에서는 정책을 넣지 않는다.
- Windows의 TLS 1.3 서버 지원은 OS 버전에 따른다. 작업 PC(Windows 11)에 기존 `KestrelTlsTests`의 TLS 1.3 DataRow가 있어 이 PC에서 TLS 1.3 서버가 동작할 것으로 보지만, 이번에 실행해 확인하지는 않았다(미확인). 그렇지 않으면 MQTTS TLS 1.3 테스트가 이 PC에서 실패해 드러나고, 그때는 C1의 TLS 1.3 확인을 C6의 리눅스 확인으로 옮길지 묻는다.
- TLS 1.0·1.1 거절은 작업 PC 테스트로 확인하지 않는다. 클라이언트 쪽(.NET·Windows)이 TLS 1.0·1.1을 막아 서버 판단과 상관없이 실패할 수 있어 서버 동작을 가리지 못하고, 클라이언트에 `SslProtocols.Tls`·`Tls11`을 쓰면 SYSLIB0039로 `-warnaserror` 빌드가 실패한다(plan 검토 4회차 검토자가 시험 프로젝트로 재현). 서버가 거절하는지는 C6에서만 확인한다(D010·D012). 리눅스 쪽 `openssl`도 시스템 암호 정책 때문에 클라이언트에서 먼저 실패할 수 있어 `openssl s_client -tls1 -cipher 'AES128-SHA:@SECLEVEL=0'`과 `-tls1_1 -cipher 'AES128-SHA:@SECLEVEL=0'`을 쓰고, 결과가 서버 경고(`alert protocol version`)이면 거절로, 클라이언트 쪽 실패(`no protocols available`·`no ciphers available`)이면 판정하지 못한 것으로 적는다(2026-11-23 같은 서버의 18443·8883에서 `-tls1`·`-tls1_1`에 이 `-cipher` 옵션으로 `alert protocol version`을 받음, 작업 문서 배경에 기록. D011).
- 리눅스 배포본은 재배포 때 실행 파일을 `cp -f`로 덮으면 SELinux 라벨은 남고 포트 권한(setcap)은 사라진다. 서비스(systemd)로 실행하면 영향이 없고, 직접 실행할 때만 사용자가 setcap을 다시 해야 한다.

## 검증 전략
- C1: `MqttTlsPolicyTests`에서 `BuildServerOptions`(인증서 있음)의 `TlsEndpointOptions.SslProtocol`이 `Tls12 | Tls13`인지 확인. TLS를 켠 서버에 `SslStream`으로 TLS 1.2·1.3 핸드셰이크가 성공하는지 확인. `MqttBrokerTests`의 TLS 1.3 접속 성공. C6에서 리눅스 서버에 `openssl s_client -tls1_3` 성공. TLS 1.0·1.1 거절은 C6의 `openssl` 판정으로만 확인한다(작업 PC 클라이언트는 서버 동작을 가리지 못함, 판정 방법은 C6, D010·D012).
- C2: Windows가 아닌 OS에서만 정책이 만들어지므로 작업 PC 테스트는 OS 분기 테스트로 "리눅스면 `TlsEndpointOptions.CipherSuitesPolicy`가 null 아님"을 둔다(작업 PC에서는 Windows 쪽만 실행됨). 실제 제한은 C6의 `openssl s_client`로 확인한다.
- C3: `MqttTlsPolicyTests`에서 목록 없음·빈 값·공백뿐인 값이면 정책이 `null`이고 경고가 없는지 확인(OS와 상관없음). `ServerKestrelExtensionsTests`에서 `ResolveCipherSuites(…, isWindows: false)`로 이 PC에서도 리눅스 경로를 실행해, 잘못된 이름 경고·제외, 남은 이름이 없으면 `null`, `warnIfNoTls13: false`면 TLS 1.2 암호만 든 목록에도 HTTP/3 경고 없음(D005)을 확인. `MqttBrokerHostedService`가 `warnIfNoTls13: false`로 부르는지는 구현 후 검토의 diff로 확인한다.
- C4: `MqttTlsPolicyTests`(Windows에서 실행)에서 목록이 있으면 정책이 `null`이고 HTTPS와 같은 문구의 경고가 하나 남는지, 엔드포인트가 켜지는지 확인.
- C5: 기존 테스트 전체 통과(`MqttPacketFragmentationTests`의 패킷 분할, `MqttBrokerTests`의 접속 인증, `KestrelTlsTests`·`ServerKestrelExtensionsTests`의 HTTPS). `dotnet build -warnaserror`.
- C6: 사용자 승인 뒤 리눅스 서버(`/PowerManager`)에 linux-x64 게시본의 실행 파일만 교체하고(설정·DB·인증서·로그 유지) 서비스 재시작. `openssl s_client -connect 127.0.0.1:8883`로 `-tls1_2 -cipher AES256-SHA256`·`AES128-SHA256` 성공, `-tls1_3` 성공, `-tls1_2 -cipher ECDHE-RSA-AES128-SHA`·`AES128-SHA` 실패, `-tls1 -cipher 'AES128-SHA:@SECLEVEL=0'`과 `-tls1_1 -cipher 'AES128-SHA:@SECLEVEL=0'`이 서버 경고(`alert protocol version`)로 실패(클라이언트 쪽 실패면 판정하지 못함으로 적음). 서버 로그에서 B540(`DAWONDNS-365f45501f96`)의 `passwordCheck="Match"`와 구독 로그 확인.

## 원칙 점검
- 적용 규칙: 전역 `spec-driven.md`(MCP `workflow/spec-driven.md`와 하위 문서), `git/github-issue.md`, `workflow/repository-content.md`, `dotnet/rid-specific-runtime.md`, `shell/powershell-windows.md`, 전역 `language-preference.md`
- 충돌: `dotnet/rid-specific-runtime.md` 1번은 한 어셈블리 안에서 `OperatingSystem.IsXxx()` 분기로 OS 구현을 섞지 않게 한다. 이번에 다시 쓰는 `CreateCipherSuitesPolicy`에 이미 `OperatingSystem.IsWindows()` 분기가 있다. 이 분기는 OS 전용 API·P/Invoke·OS 전용 NuGet 의존성이 아니라, 모든 OS에 있는 BCL 형식(`CipherSuitesPolicy`)이 Windows에서 예외를 내는 것을 피하는 확인이라 규칙의 적용 대상(1번)에 들지 않는다고 본다. OS별 구현은 더하지 않는다. 해석 함수에 넘기는 `OperatingSystem.IsWindows()`와 정책 생성 앞의 `OperatingSystem.IsWindows()`는 같은 확인을 두 번 하는 것으로, 둘째는 플랫폼 분석기(CA1416)가 인정하는 보호 구문일 뿐 다른 OS 구현이 아니다(D009).
- 충돌: 그 밖에 없음
