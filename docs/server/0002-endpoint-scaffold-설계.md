---
status: done
category: server
feature: endpoint-scaffold
phase: 설계
---

# endpoint-scaffold 설계

선행: [0001-endpoint-scaffold-방안](0001-endpoint-scaffold-방안.md)

## 1. 솔루션 구조

규칙 `dotnet/project-creation.md`, `dotnet/server-api-template.md`, `dotnet/server-spa-template.md`, `frontend/react-project-creation.md`를 따른다.

기존 솔루션(`Biz.Bizadm.PowerManager.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`)에 DeviceConfig 프로젝트가 이미 있다. 루트 파일은 새로 만들지 않고 **병합**한다. 프로젝트는 `dotnet sln add`로 기존 `/src/`·`/tests/` 폴더에 넣는다. 테스트 프로젝트는 기존과 같이 MTP(`global.json` runner, `OutputType Exe`, `EnableMSTestRunner true`)를 쓴다.

템플릿 예외(장치 고정 포트 요구 때문):

| 예외 | 템플릿 기본 | 이 프로젝트 |
|---|---|---|
| 실행 프로젝트 | `Server`(+ 선택 `Server.Spa`) | `Server.Spa` 하나(SPA 스킬이 `Server` 동반 여부를 요청에 맡김) |
| 포트 설정 | 필수 단일 `Port` | `Port` 제거, 포트별 키 6개(§4). 필수가 아니라 `DefaultValue`가 있는 선택 키 |
| TLS 시 HTTP 리스너 | TLS면 그 포트로 HTTPS만 | TLS를 켜도 HTTP 포트(80·18446)는 계속 리슨하고 Redirect한다. 단 `/fota` 세그먼트 경로는 Redirect하지 않고 HTTP로 처리 |
| 테스트 프로젝트 | `Server.Tests`(`AppConfigurationTests`·`PipelineTests`) | `Server.Tests` 없음. `AppConfigurationTests`를 `Server.Spa.Tests`로 옮기고 포트 기본값 케이스 추가 |
| `AppConfiguration` DI | 등록하지 않음 | `MqttBrokerHostedService`가 받도록 싱글턴 등록(복호화된 설정 객체가 DI에 노출, §3) |
| Kestrel 바인딩 | `ConfigureServerKestrel(ushort port, ...)` | HTTP·HTTPS 포트 목록을 받는 멀티 포트 시그니처 |
| HTTPS 리다이렉트 | 없음 | 포트 매핑 리다이렉트 미들웨어(§2) |
| TLS 인증서 로드 | `ConfigureServerKestrel` 내부 | Server.Core `TlsCertificateLoader`로 추출. 경로 계산(`ResolvePath`)은 Kestrel·MQTTS 공유, 인증서 로드(`Load`)는 MQTTS 전용(Kestrel은 템플릿 로드 방식 유지)(§3) |
| MQTTS TLS 버전 | — (Kestrel은 TLS 1.2/1.3) | TLS 1.2 고정(§3) |
| Vite 프록시 | 기본 대상 `http://localhost:5000`, `/api` | 기본 대상 `http://localhost:80`(포트 설정 변경에 따름), `/api`만, `secure: false`(React가 호출하는 경로만 둔다) |

```
Biz.Bizadm.PowerManager.slnx
Directory.Build.props / Directory.Packages.props (CPM, net10.0)
src/
  Biz.Bizadm.PowerManager.Core/          도메인: 메시지 모델, 토픽 규칙, 핸들러 인터페이스
  Biz.Bizadm.PowerManager.Server.Core/   기동 구성 요소(ExitCode, OsKms, Tls, KmsSecretProtector, Serilog, Kestrel, TlsCertificateLoader)
  Biz.Bizadm.PowerManager.Server.Spa/    실행 진입점: React 호스팅, 장치 REST, FOTA, 내장 MQTT 브로커
  Biz.Bizadm.PowerManager.Client/        React(Vite) 프로젝트 생성 수준
tests/
  Biz.Bizadm.PowerManager.Core.Tests/
  Biz.Bizadm.PowerManager.Server.Core.Tests/
  Biz.Bizadm.PowerManager.Server.Spa.Tests/
```

참조: `Server.Spa` → `Server.Core`, `Core`. `Core`는 ASP.NET Core·MQTTnet에 의존하지 않는다. 테스트 → 대상 프로젝트.

## 2. 포트

| 용도 | 포트 | 비고 |
|---|---|---|
| HTTP | 80, 18446 | `/fota` 세그먼트 경로를 제외한 모든 요청을 HTTPS로 Redirect(80→443, 18446→18443). `/fota` 세그먼트 경로는 HTTP로 그대로 처리 |
| HTTPS | 443, 18443 | `Tls` 섹션 인증서 |
| MQTT | 1803 | 비TLS |
| MQTTS | 8883 | `Tls` 섹션 인증서 공유 |

`Tls.UseTls`가 `true`가 아니면 HTTPS·MQTTS 리스너를 열지 않고 Redirect도 하지 않는다.

## 3. TLS

템플릿 `Tls`(`TlsConfiguration`: PKCS#12 + KMS 보호 패스워드)를 Kestrel HTTPS와 MQTT 8883이 공유한다.

- 인증서 경로 계산과 로드는 Server.Core의 `TlsCertificateLoader` 하나로 추출한다. Kestrel은 경로 계산(`ResolvePath`, 템플릿과 같은 절대 경로)만 쓴다. 템플릿 `ListenAnyIP` 안의 HTTPS 인라인 람다(체인·cipher 포함)는 포트 목록마다 쓰도록 `ConfigureHttps` private 메서드로 추출하되 내용은 바꾸지 않는다. `MqttBrokerHostedService`는 `Load`로 인증서를 받아 브로커 수명 동안 보관하다 종료 시 Dispose한다. 패스워드는 템플릿과 같이 KMS로 복호화된 설정 객체에만 존재하고, 로더는 그 값을 호출 시에만 읽는다. 추가 필드에 보관하거나 로그·예외 메시지에 남기지 않는다.
- MQTTS에는 서버 인증서만 적용한다. `CertificateChain`·`IncludeCipherSuites`는 Kestrel에만 적용하고 MQTTS에서는 쓰지 않는다(이 단계 범위).
- MQTTS는 TLS 1.2로 고정한다. 장치 mbedTLS가 TLS 1.0~1.2만 지원하기 때문이다.

위험(미확인): Kestrel HTTPS는 TLS 1.2/1.3과 OS 기본 cipher suite를 쓴다. ESP8266 mbedTLS가 지원하는 suite와 겹치지 않으면 장치 HTTPS 요청이 실패할 수 있다. 실장비 연동 시 확인하고, 필요하면 `Tls.IncludeCipherSuites`로 조정한다. 단 템플릿은 Windows에서 `IncludeCipherSuites`를 경고 후 무시하므로, Windows 호스트에서는 조정할 수 없다(Linux만 가능).

위험(미확인): MQTTS도 OS 기본 cipher suite를 쓰고 이 단계에서는 `IncludeCipherSuites`를 적용하지 않는다. MQTTnet TLS 엔드포인트가 `CipherSuitesPolicy`를 지원하는지는 미확인이며 구현 시 확인한다(개발 U6). ESP8266 mbedTLS의 TLS 레코드 크기 제약도 문제가 될 수 있다. 또 근거 문서는 "TLSv1.0~1.2" 문자열만 관찰했으므로, 장치가 실제로 1.2 미만만 협상하면 TLS 1.2 고정 MQTTS 접속이 실패한다. 실장비 연동 시 확인하고, 실패하면 MQTTS 구성을 다시 설계한다.

위험(수용): 장치는 HTTP 요청을 직접 조립하므로 30x를 따라가지 못할 수 있다. 장치가 등록 REST를 HTTP로 호출하면 실패할 수 있으며, 장치에는 `ssl_support`·HTTPS URL을 설정하는 것을 전제로 한다. FOTA는 SDK(`upgrade_lib`)가 기본 HTTP이고 HTTPS 다운로드 지원이 미확인(`b540-fota.md` §5)이므로 `/fota` 세그먼트 경로를 Redirect에서 제외해 HTTP로 계속 받는다(방안 결정). 펌웨어 이미지가 평문으로 전송되는 점은 수용한다. 또 `UseHsts`(템플릿: `IncludeSubDomains`, 365일) 때문에 운영자 브라우저가 HTTPS에 한 번 접속하면 같은 호스트의 HTTP `/fota` 요청을 HTTPS로 승격해 실패한다. 장치에는 영향이 없으며, `/fota` 수동 확인은 브라우저가 아닌 curl 등으로 한다.

전제: FOTA endpoint는 `/fota` 세그먼트 접두로 고정한다(§5). 장치 TCP 설정의 `LFota_*` 경로는 `/fota/` 아래 하위 경로(예: `/fota/b540`)로 지정하고 `/fota` 단독은 쓰지 않는다. FOTA SDK는 이미지 요청을 구분자 없이 `GET %s%s`로 결합하므로(`b540-fota.md` §3-5), 경로가 `/fota`이고 이미지명이 `/`로 시작하지 않으면 `/fotauser2.bin`이 되어 라우팅·Redirect 제외 모두에서 벗어난다. 이미지명이 `/`로 시작하는지는 미확인이므로 실장비 연동 시 확인한다.

위험(미확인): 서버 운영 OS가 정해져 있지 않다. Linux에서 실행하면 80·443 바인딩에 root 또는 `CAP_NET_BIND_SERVICE`가 필요하다(Windows는 불필요). 운영 OS를 정할 때 배포 방식에 반영한다.

개발 모드: `dotnet run`(SpaProxy·Vite)은 `Tls` 없이(`UseTls` 미설정) 실행하는 것을 전제로 한다. TLS를 켜면 `/fota` 세그먼트 경로 외 HTTP 요청이 307로 HTTPS 포트로 넘어가므로 Vite 프록시 대상(`SERVER_URL`)을 HTTPS 포트로 지정해야 한다.

## 4. 설정 스키마 (`AppConfiguration`, YAML)

템플릿의 단일 `Port`를 포트별 개별 키로 바꾼다(§1 예외). `Tls` 키는 템플릿 `TlsConfiguration`을 그대로 쓴다.

```yaml
ContentRootPath: .
WebRootPath: ClientApp
HttpPort: 80            # → HttpsPort로 Redirect
HttpsPort: 443
DeviceHttpPort: 18446   # → DeviceHttpsPort로 Redirect
DeviceHttpsPort: 18443
MqttPort: 1803
MqttsPort: 8883
OsKms:                  # 템플릿 OsKmsConfiguration 그대로(CredentialAccount, Iterations, SecretsDekFilePath 등 필수 키 포함)
  CredentialService: https://powermanager.local
  # ... 나머지 키는 템플릿과 같음 ...
Tls:                    # 선택. UseTls가 true가 아니면 HTTPS·MQTTS 리스너 비활성, Redirect 없음
  UseTls: true
  ServerCertificate: cert.pfx
  ServerCertificatePassword: ...   # KMS로 기동 시 암호화
  CertificateChain: ...            # 선택, Kestrel만
  IncludeCipherSuites: ...         # 선택, Kestrel만
```

## 5. 컴포넌트

### Core (`Biz.Bizadm.PowerManager.Core`)

| 영역 | 타입(예정) | 역할 |
|---|---|---|
| 장치 REST | `DeviceRegisterRequest`(`account.user_id`, `device.mac/model_id/company/lati/long/status/verify`) | 등록 요청 모델. JSON 이름은 snake_case 원문을 `JsonPropertyName`으로 고정하고, 값은 모두 문자열(`verify`도 `"true"` 문자열)이므로 `string?`로 둔다 |
| MQTT 메시지 | `MqttEnvelope`(`sid`, `msg.o`, `msg.e[]{n, sv}`), `MqttOperation` 상수(`rg`,`drg`,`tisyn`,`notify`,`w`,`chg`,`reb`) | SenML 유사 JSON |
| MQTT 토픽 | `DeviceTopic` | `{prefix}.v1/{deviceKey}/iot-server/{접미}`(json, timesync 등) 파싱, `{prefix}.v1/iot-server/{deviceKey}` 생성, 끝 토큰 `lwt` 판별. LWT 토픽의 정확한 결합 형태는 근거 문서에서 미확인(`/lwt` 접미만 관찰)이므로 같은 접두 + `/lwt`로 추정하고, 실장비 연동 시 확인한다 |
| 수신 처리 | `IDeviceMessageHandler` | `RegisterAsync`, `DeregisterAsync`, `TimeSyncAsync`, `NotifyAsync`, `OfflineAsync` |
| 발행 | `IDeviceCommandPublisher` | `WriteAsync`, `ChangeAsync`, `RebootAsync` |

### Server.Spa (`Biz.Bizadm.PowerManager.Server.Spa`)

| 컴포넌트 | 내용 |
|---|---|
| `DevicesController` | `POST /api/v1/devices/register/create`, `GET /api/v1/device/connect/{deviceKey}`(방안의 `{a}-{b}-{c}` 전체를 `deviceKey` 하나로 받음) |
| `FotaController` | `GET`·`HEAD /fota/{**path}`: 끝이 `/version`이면 버전 조회, 그 외 이미지 다운로드(접두사 전제는 §3). HEAD도 컨트롤러가 받아 로그 + 501을 돌려준다. GET만 받으면 운영 모드에서 확장자 없는 경로의 HEAD가 SPA fallback으로 가서 404가 되고, SDK가 404에서도 GET으로 재시도하는지는 미확인(`b540-fota.md` §3-5는 HEAD 미지원 시 GET 재시도만 기재)이기 때문이다 |
| `MqttBrokerHostedService` | MQTTnet 서버, 1803 + (`UseTls` 시) 8883, 수신 메시지를 `DeviceTopic`·`MqttEnvelope`로 해석해 `IDeviceMessageHandler`로 분배 |
| `LoggingDeviceMessageHandler` | `IDeviceMessageHandler` 껍데기: 로그만 |
| `MqttDeviceCommandPublisher` | `IDeviceCommandPublisher` 껍데기: `NotImplementedException` |
| Redirect | HTTP 포트별 대응 HTTPS 포트로 Redirect. `/fota` 세그먼트로 시작하는 경로(`/fota`, `/fota/...`, 대소문자 무시 — 라우팅과 같은 기준)는 제외하고 HTTP로 `FotaController`가 처리한다. Host 헤더가 없는 요청은 `Location`을 만들 수 없어 Redirect 없이 통과한다(실제 장치 요청에는 Host가 있음). 포트 키에 같은 값을 넣으면 Kestrel 중복 바인딩으로 기동 실패(종료 코드 3)하며 별도 검사는 하지 않는다 |

## 6. 껍데기 동작

- REST·FOTA: 요청 바인딩 후 로그를 남기고 `501 Not Implemented` ProblemDetails. 예외: 본문이 비었거나 JSON 형식이 깨지면 `[ApiController]`가 액션 전에 400 ValidationProblem을 돌려주고 로그는 남지 않는다(동작 유지).
- MQTT 수신: 해석 결과를 로그로만 남긴다. 해석 실패(JSON 오류, `msg`·`e` 누락)도 로그. LWT는 브로커가 대신 발행하는 Will 메시지로 받는다.
- MQTT 발행: `NotImplementedException`.
- 브로커 접속 인증은 하지 않는다(모든 클라이언트 수용).
- 위험(수용): 인증·ACL이 없으므로 브로커에 접속할 수 있는 누구나 서버→장치 토픽으로 `reb`·`w`·`chg`를 발행해 장치를 제어할 수 있다. 이 단계의 서버는 시험망(장치와 운영자만 접근 가능한 내부망)에서만 운영하고 인터넷에 노출하지 않는다. 인증은 후속 단계에서 다룬다(방안 비범위).
- 해석되지 않는 토픽(`DeviceTopic.TryParse` 실패)은 Information 로그로 남긴다. LWT 등 실제 토픽 형태를 실장비 연동 때 확인하기 위함이다. 템플릿 Serilog 최소 레벨이 Information이라 Debug는 기록되지 않는다. 서버→장치 명령 토픽도 매번 기록되어 로그 양이 늘어나는 점은 수용한다.

## 7. 의존성

- MQTTnet(서버 기능 포함 버전) — 버전은 `nuget-package-selection` 규칙으로 개발 단계에서 선정.
- 나머지는 템플릿 기본(CommandLineParser, YamlDotNet, Serilog, Biz.Bizadm.KMS 등). 기존 `Directory.Packages.props`에 없는 템플릿 패키지(`Microsoft.AspNetCore.OpenApi`, `Microsoft.AspNetCore.TestHost`, `Microsoft.AspNetCore.SpaProxy`)를 추가한다. 버전은 10.0.x 최신 패치.
