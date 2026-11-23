# MQTTS TLS 버전·암호 목록 적용 결정

### D001 MQTTS는 TLS 1.2와 1.3을 받는다
- 단계: spec
- 질문: MQTTS 엔드포인트의 TLS 버전을 어떻게 할지. 지금은 TLS 1.2 고정이고 HTTPS(Kestrel)는 TLS 1.2·1.3을 받는다. 장치(B540·M130-W)는 TLS 1.2만 제시한다(2026-11-23 ClientHello 캡처).
- 선택지:
  - A. 1.2·1.3: HTTPS와 같게 TLS 1.2와 1.3을 받는다. 1.0·1.1은 계속 막고, 암호 목록의 TLS 1.3 항목도 MQTTS에서 쓰인다.
  - B. OS에 맡김: 버전을 고정하지 않고 MQTTnet 기본값(`SslProtocols.None`)으로 OS가 고르게 한다.
  - C. 1.2 유지: 버전은 TLS 1.2 고정으로 두고 암호 목록만 적용한다.
- 결정: A
- 이유: 사용자가 말하지 않음
- 반영: spec.md C1

### D002 서버 인증서 체인도 MQTTS에 적용한다
- 단계: spec
- 질문: `Tls.CertificateChain`을 MQTTS에도 적용할지. 질문할 때 "MQTTS는 서버 인증서만 보낸다"고 안내했다(이 안내는 틀렸음, D003).
- 선택지:
  - A. 이번엔 빼기: 이번 작업은 TLS 버전과 암호 목록만 다룬다.
  - B. 함께 적용: MQTTS도 HTTPS와 같은 체인을 보낸다.
- 결정: B
- 이유: 사용자가 말하지 않음
- 반영: D003으로 대체

### D003 인증서 체인은 이번 범위에서 뺀다
- 단계: spec
- 질문: D002 대체. 확인해 보니 리눅스 서버의 MQTTS는 이미 HTTPS와 같은 체인(`iot.bizadm.biz` ← `AzureLeaf`)을 보낸다(`openssl s_client -showcerts`로 8883·18443 확인). `AzureLeaf`·`AzureRoot`가 OS 신뢰 저장소(`/etc/pki/ca-trust/source/anchors/`)에 있어 .NET이 그곳에서 체인을 만들기 때문이다. MQTTnet 5.2 TLS 엔드포인트 옵션에는 서버 인증서 제공자(`CertificateProvider`)만 있고 체인을 넘기는 항목이 없어(리플렉션으로 `MqttServerTlsTcpEndpointOptions` 속성 확인), 설정의 `CertificateChain`으로 체인을 보내려면 MQTT 호스팅 구조를 바꿔야 한다(방법 미확인).
- 선택지:
  - A. 이번엔 빼기: TLS 버전과 암호 목록만 다루고, 체인이 OS 저장소에 기대는 점은 스펙에 기록한다.
  - B. 설정으로 보내기: OS 저장소와 상관없이 `CertificateChain`으로 MQTTS에도 체인을 보낸다. 호스팅 구조 변경과 설계 조사가 더 필요하다.
- 결정: A
- 이유: 사용자가 말하지 않음
- 반영: spec.md 「범위 밖」

### D004 리눅스 서버에 배포해 확인한다
- 단계: spec
- 질문: 암호 목록 제한은 리눅스에서만 동작한다(Windows는 .NET이 `CipherSuitesPolicy`를 지원하지 않아 HTTPS처럼 경고 후 무시). 작업 PC(Windows)의 테스트로는 제한이 실제로 걸리는지 확인할 수 없어, 리눅스에서 확인하는 단계를 넣을지 정해야 했다.
- 선택지:
  - A. 배포해 확인: 구현 뒤 승인을 받아 리눅스 서버에 실행 파일만 교체하고(설정·DB·인증서 유지), MQTTS에서 목록 밖 암호 거절·장치 암호 수락과 실장비 재접속을 확인한다.
  - B. 리눅스 테스트만: 배포하지 않고 리눅스 서버 임시 폴더에서 같은 테스트를 돌려 제한만 확인한다. 실장비 확인은 빠진다.
  - C. Windows 테스트만: 옵션이 만들어지는지만 확인하고, 리눅스 제한은 확인하지 않음으로 남긴다.
- 결정: A
- 이유: 사용자가 말하지 않음
- 반영: spec.md C6

### D005 목록에 TLS 1.3 암호가 없을 때 MQTTS는 경고를 남기지 않는다
- 단계: spec
- 질문: spec 검토 1회차 지적 2. 검토 의견: C3·C4에는 잘못된 이름 경고와 Windows 경고만 있고, HTTPS의 `CreateCipherSuitesPolicy`가 목록에 TLS 1.3 암호가 없을 때 내는 HTTP/3 실패 경고를 MQTTS가 내는지가 없다. 같은 함수를 다시 쓰면 HTTP/3 문구의 경고가 한 번 더 남는다. 왜 문제인지: 완료 조건으로 맞는 동작을 판정할 수 없다. MQTTS는 TLS 1.3 암호가 없어도 TLS 1.2로 연결되므로 실패로 이어지지 않는다.
- 선택지:
  - A. MQTTS는 경고 안 냄: C3에 "목록에 TLS 1.3 암호가 없을 때의 HTTP/3 경고는 MQTTS 때문에 다시 남기지 않는다"를 붙인다.
  - B. MQTTS 전용 경고: 목록에 TLS 1.3 암호가 없으면 MQTTS는 TLS 1.3 연결을 받지 않는다는 경고를 따로 남긴다.
  - 고치지 않음: 경고 동작을 정하지 않는다.
- 결정: A
- 이유: 사용자가 말하지 않음
- 반영: spec.md C3

### D006 C1의 버전별 목록 조건은 판정 조건이 아닌 범위 설명으로 둔다
- 단계: spec
- 질문: spec 검토 2회차 지적 1(회귀). 검토 의견: 1회차에 C1에 덧붙인 "암호 목록이 적용될 때(C2)는 목록에 그 버전의 암호가 있을 때만 그 버전으로 연결된다"가 "목록에 그 버전 암호가 없으면 거절"이라는 새 조건을 만드는데, Windows에서는 목록이 무시되고(C4) 리눅스 확인은 C6 배포 확인뿐이며 그 서버 목록에는 TLS 1.2·1.3 암호가 모두 있어 거절 경우가 나오지 않는다. 왜 문제인지: 완료 판정을 할 수 없다. 서버 목록의 TLS 1.3 암호 4종이 OpenSSL 기본 TLS 1.3 암호 3종을 모두 포함해, 설정을 그대로 두면 TLS 1.3 제한이 걸리는지도 가릴 수 없다.
- 선택지:
  - A. 범위 설명으로 바꿈: C1 끝 문장을 "C1의 TLS 1.3 수락은 암호 목록이 적용될 때(C2) 목록에 TLS 1.3 암호가 있는 경우를 말한다"로 바꾼다. TLS 1.3에도 목록이 걸리는지는 TLS 1.2에서 확인하는 것(C2·C6)과 같은 .NET 정책에 기댄다.
  - B. 리눅스 임시 서버로 확인: C6에 리눅스 서버 임시 폴더에서 TLS 1.3 암호를 뺀 목록으로 다른 포트에 서버를 띄워 TLS 1.3 거절·TLS 1.2 연결을 확인하는 단계를 넣고, D004를 넓힌다.
  - 고치지 않음: 판정할 수 없는 문장을 C1에 둔다.
- 결정: A
- 이유: 사용자가 말하지 않음
- 반영: spec.md C1

### D007 암호 목록 해석·경고를 OS 판단과 분리해 리눅스 경로를 작업 PC에서 테스트한다
- 단계: plan
- 질문: plan 검토 1회차 지적 3. 검토 의견: `CreateCipherSuitesPolicy`는 Windows이면 이름 해석·HTTP/3 경고 전에 끝나, 작업 PC 테스트로는 `warnIfNoTls13: false` 경로가 실행되지 않는다. plan은 이를 C6 로그로 본다고 했지만 C6 설정에는 잘못된 이름도 없고 TLS 1.3 암호도 있어 두 경고 모두 나오지 않는다. 왜 문제인지: C3의 리눅스 동작(D005 포함)이 어디서도 실행되지 않는데 확인되는 것처럼 적혀 검증 기록이 틀리게 된다.
- 선택지:
  - A. 해석·경고를 분리: Server.Core에 `internal` 함수 `ResolveCipherSuites(string?, ILogger, bool warnIfNoTls13, bool isWindows)`(암호 목록 또는 `null`)를 두고, `CreateCipherSuitesPolicy`는 `OperatingSystem.IsWindows()`를 넘겨 그 결과로 정책만 만든다. 테스트는 `isWindows: false`로 잘못된 이름 경고와 HTTP/3 경고 끔을 작업 PC에서 확인한다.
  - B. 확인 못함으로 적기: 검증 전략 C3에 "리눅스 분기(잘못된 이름 경고·HTTP/3 경고 끔)는 확인하지 못한다"고 적는다.
  - 고치지 않음: "C6에서 로그로 본다"를 둔다.
- 결정: A
- 이유: 사용자가 말하지 않음
- 반영: plan.md 바꿀 파일·구조(ServerKestrelExtensions.cs, ServerKestrelExtensionsTests.cs), 결정, 인터페이스·데이터, 검증 전략 C3

### D008 MQTTnet DLL 근거 문장을 고치고 정책 전달을 메타데이터 근거로 적는다
- 단계: plan
- 질문: plan 검토 1회차 지적 4. 검토 의견: plan 결정의 "MQTTnet.Server DLL에서 `CipherSuitesPolicy`가 들어간 이름은 속성과 `get_`·`set_` 셋뿐"이 DLL과 맞지 않는다. 형식 참조 `System.Net.Security.CipherSuitesPolicy`와 멤버 참조 `SslServerAuthenticationOptions.set_CipherSuitesPolicy`도 있다(결론인 빌더 메서드 없음은 맞음). 검토자는 DLL 메타데이터상 MQTTnet이 엔드포인트의 `CipherSuitesPolicy`를 `SslServerAuthenticationOptions`에 넘긴 뒤 `AuthenticateAsServerAsync`를 부른다고 덧붙였다(REA로 IL 확인). 왜 문제인지: 근거 문장이 출처와 맞지 않는다.
- 선택지:
  - A. 근거·위험 항목 모두 고침: 결정의 근거를 "MQTTnet 쪽 이름은 셋뿐(형식 참조와 호출 참조 제외)"으로 고치고, 오류·위험 첫 항목을 메타데이터 근거(검토자 REA 확인)와 "실행 확인은 C6"으로 바꾼다.
  - B. 근거 문장만 고침: 오류·위험 첫 항목은 미확인으로 둔다.
  - 고치지 않음: 문장을 그대로 둔다.
- 결정: A
- 이유: 사용자가 말하지 않음
- 반영: plan.md 결정(정책 적용 시점), 오류·위험 첫 항목

### D009 정책 생성 앞에 OS 확인을 다시 두어 CA1416을 피한다
- 단계: plan
- 질문: plan 검토 2회차 지적 1(회귀). 검토 의견: D007대로 OS 판단을 `ResolveCipherSuites`의 인자로만 넘기고 "목록이 있을 때만 `CipherSuitesPolicy`를 만든다"로 짜면, `CipherSuitesPolicy` 형식에 `[UnsupportedOSPlatform("windows")]`가 붙어 있어 플랫폼 분석기가 Windows 보호를 보지 못하고 CA1416을 낸다. 왜 문제인지: C5의 `dotnet build -warnaserror`가 실패한다(검토자가 같은 설정의 시험 프로젝트로 재현, 저장소에 CA1416 끄는 설정 없음).
- 선택지:
  - A. 생성 앞 보호 구문: `ResolveCipherSuites(…, OperatingSystem.IsWindows())`를 부른 뒤 `if (suites is null || OperatingSystem.IsWindows()) return null;` 뒤에서만 정책을 만든다. 원칙 점검에 둘째 확인은 분석기용 보호 구문이라고 적는다.
  - B. 특성 붙인 도우미: 생성 부분을 `[UnsupportedOSPlatform("windows")]` 도우미로 뺀다(부르는 곳에 여전히 보호 구문 필요).
  - C. CA1416 경고 끄기: `#pragma warning disable CA1416`.
  - 고치지 않음: 구현 때 빌드 실패로 다시 정한다.
- 결정: A
- 이유: 사용자가 말하지 않음
- 반영: plan.md 바꿀 파일·구조(ServerKestrelExtensions.cs), 결정, 원칙 점검

### D010 TLS 1.0 거절도 TLS 1.1과 같은 방법으로 확인한다
- 단계: tasks
- 질문: tasks 검토 1회차 지적 1. 검토 의견: spec C1은 "TLS 1.0·1.1 핸드셰이크는 거절한다"인데 plan 검증 전략 C1·요약과 tasks T005·T009는 TLS 1.1만 확인한다. 왜 문제인지: C1이 통과로 적혀도 TLS 1.0 거절은 확인되지 않은 채 남는다.
- 선택지:
  - A. plan·tasks에 TLS 1.0 추가: plan 요약·오류·위험(TLS 1.1 항목)·검증 전략 C1·C6과 tasks T005·T009에 TLS 1.0을 더한다. 작업 PC 테스트는 "TLS 1.0·1.1이 성공하지 않음", 리눅스는 `openssl s_client -tls1 -cipher 'AES128-SHA:@SECLEVEL=0'`이 서버 경고로 실패하는지(클라이언트 쪽 실패면 판정 못함). 확정된 plan의 의도 변경이라 plan 검토·확인을 다시 받는다.
  - B. 확인 못함으로 기록: 문서는 그대로 두고 검증 기록에 "C1의 TLS 1.0 거절은 확인하지 못함"으로 적는다.
  - 고치지 않음: TLS 1.0 확인 없이 C1을 판정한다.
- 결정: A
- 이유: 사용자가 말하지 않음
- 반영: plan.md 요약·오류·위험·검증 전략 C1·C6, tasks.md T005·T009

### D011 TLS 1.0·1.1 openssl 확인 이력의 출처를 밝힌다
- 단계: plan
- 질문: plan 검토 4회차 지적 1(회귀). 검토 의견: D010에서 `-tls1` 옵션을 앞에 더하면서 plan 오류·위험 넷째 항목 끝 괄호 "같은 서버에서 이 옵션으로 `alert protocol version`을 받은 적 있음"이 TLS 1.0에도 걸리게 됐는데, 변경 문서·회차 사본 어디에도 `-tls1`로 확인한 기록이 없다. 왜 문제인지: 출처 없는 주장이 확인된 것처럼 읽힌다(실제로는 2026-11-23 리눅스 서버 18443·8883에서 `-tls1`·`-tls1_1`을 `-cipher "AES128-SHA:@SECLEVEL=0"`로 돌려 `alert protocol version`을 받았으나 문서에 적지 않았음).
- 선택지:
  - A. 출처를 밝혀 고침: 괄호를 포트·옵션·결과와 "작업 문서 배경에 기록"으로 바꾸고 작업 문서 배경에 결과를 적는다.
  - B. TLS 1.1에만 걸치게 좁힘: 괄호를 `-tls1_1` 옵션에만 걸리게 한다.
  - 고치지 않음: 문장을 그대로 둔다.
- 결정: A
- 이유: 사용자가 말하지 않음
- 반영: plan.md 오류·위험 넷째 항목, 작업 문서 배경

### D012 작업 PC의 TLS 1.0·1.1 핸드셰이크 테스트는 두지 않는다
- 단계: plan
- 질문: plan 검토 4회차 지적 2. 검토 의견: 검증 C1의 "`SslStream`으로 TLS 1.0·1.1은 성공하지 않는지 확인"(tasks T005)은 클라이언트에 `SslProtocols.Tls`·`Tls11`을 지정해야 하는데 .NET 10에서 사용 중단이라 SYSLIB0039 경고가 나고 `-warnaserror`에서 오류가 된다(검토자가 시험 프로젝트에서 재현, 저장소에 억제 설정 없음). 왜 문제인지: 단계 2 체크포인트와 C5 빌드가 실패한다. 또 클라이언트(Windows)가 TLS 1.0·1.1을 막을 수 있어 이 테스트는 서버 동작을 가리지 못한다.
- 선택지:
  - A. PC의 1.0·1.1 테스트 빼기: 검증 C1에서 빼고 TLS 1.0·1.1 거절은 C6의 `openssl` 판정으로만 확인한다. 오류·위험 넷째 항목과 tasks T005도 맞춘다.
  - B. pragma로 경고 억제: 테스트에 `#pragma warning disable SYSLIB0039`를 두고 유지한다.
  - 고치지 않음: plan을 그대로 둔다.
- 결정: A
- 이유: 사용자가 말하지 않음
- 반영: plan.md 요약·오류·위험 넷째 항목·검증 전략 C1, tasks.md T005
