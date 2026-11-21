# MQTT 브로커 패킷 분할 끄기 계획

## 요약

`MqttBrokerHostedService`가 MQTT 서버 옵션을 만들 때 `MqttServerOptionsBuilder.WithoutPacketFragmentation()`을 더해 비TLS·TLS 엔드포인트 모두 `AllowPacketFragmentation = false`로 연다. 옵션을 만드는 부분을 공개 정적 메서드 `BuildServerOptions`로 떼어 C2를 옵션 값으로 확인한다(D001). C1은 실제 tisyn 응답 경로로, TLS 엔드포인트에 원시 TLS 클라이언트를 붙여 `SslStream` 아래 원시 수신 바이트의 TLS 레코드 머리를 세어 확인한다(D003). 기준 spec에 요구 R14를 더한다. 배포 뒤 TLS 포트 캡처로 C5를 확인한다.

## 바꿀 파일·구조

- src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs: `public static MqttServerOptions BuildServerOptions(AppConfiguration configuration, X509Certificate2? certificate)` 추가. 지금 `StartAsync` 안의 옵션 빌더 코드(비TLS 엔드포인트, `certificate`가 있으면 TLS 엔드포인트·인증서·TLS 1.2)를 옮기고 `.WithoutPacketFragmentation()`을 더한다. `StartAsync`는 `Tls.UseTls`이면 인증서를 읽은 뒤 이 메서드로 옵션을 만든다. 클래스 요약 주석에 분할을 끈다는 내용과 이유를 한 줄 더한다.
- tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttPacketFragmentationTests.cs(새 파일): C1·C2 테스트.
- tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TestApp.cs: `MqttBrokerTests`의 private `WritePfx`(자체 서명 localhost 인증서 PFX 쓰기, 지문 반환)를 `TestApp.WritePfx(string path, string password)`로 옮긴다.
- tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs: private `WritePfx`를 지우고 `TestApp.WritePfx(path, PfxPassword)`를 부른다(동작 같음).
- docs/features/mqtt-broker.md: R14 추가, 머리말 한 문장, `파일:` 줄에 새 테스트 파일, 이력 한 줄.

## 결정

- 옵션 만드는 부분을 `public static` 메서드로 뗀다: C2를 브로커가 실제로 쓰는 옵션 값으로 확인하려면 테스트가 옵션을 받아야 한다. Server.Spa에는 `InternalsVisibleTo`가 없고 클래스가 이미 public이라 public 메서드로 둔다 / 검토한 대안: 반사로 private `server`의 옵션을 읽기(MqttServer는 옵션을 공개하지 않아 내부 필드에 기대야 함), `InternalsVisibleTo` 추가(이 변경만을 위해 프로젝트 설정을 늘림) (D001)
- 분할 끔은 `WithoutPacketFragmentation()`으로 한다: 두 엔드포인트 옵션을 한 번에 `false`로 둔다(디컴파일 `MqttServerOptionsBuilder` 179~183줄) / 검토한 대안: `Build()` 뒤 `DefaultEndpointOptions`·`TlsEndpointOptions`의 속성을 각각 바꾸기(같은 결과, 줄이 늘고 하나를 빠뜨릴 수 있음)
- C1은 원시 수신 바이트의 TLS 레코드 머리를 센다: `SslStream` 한 번 읽기는 도착한 레코드를 합쳐 돌려줄 수 있어 분할 여부를 가리지 못한다(plan 전 실험) (D003)
- C1의 PUBLISH는 실제 tisyn 응답으로 만든다: 비TLS 포트의 MQTTnet 클라이언트가 `tisyn`을 보내면 기본 핸들러가 서버 → 장치 토픽에 응답(117바이트 안팎, 16384바이트 이하)을 주입한다. 장비가 받는 것과 같은 경로다 / 검토한 대안: 테스트에서 `MqttServerAccessor.Server`로 직접 주입(같은 송신 경로지만 실제 응답 형태와 다를 수 있음)
- 원시 TLS 클라이언트는 MQTT 3.1(`MQIsdp`, level 3) CONNECT를 보낸다: 실장비와 같은 프로토콜이다(근거 문서, mqtt-broker R1)

## 인터페이스·데이터

- `public static MqttServerOptions MqttBrokerHostedService.BuildServerOptions(AppConfiguration configuration, X509Certificate2? certificate)`: `configuration.MqttPort`로 비TLS 엔드포인트를 켜고, `certificate`가 null이 아니면 `configuration.MqttsPort`로 TLS 엔드포인트(인증서, `SslProtocols.Tls12`)를 켠다. 두 엔드포인트 모두 `AllowPacketFragmentation = false`. 인증서 읽기·보관(`certificate` 필드, 해제)은 지금처럼 `StartAsync`·`DisposeAsync`가 맡는다.
- 기준 spec R14(안): "브로커는 비TLS·TLS 엔드포인트 모두 패킷 분할을 끈 설정(`AllowPacketFragmentation = false`)으로 열어, 클라이언트에 보내는 MQTT 패킷마다 한 번에 쓴다. TLS 엔드포인트에서 MQTT 패킷 전체가 16384바이트 이하인 PUBLISH는 TLS 레코드 하나로 나간다(장비 MQTT 라이브러리가 여러 수신에 걸친 패킷을 합치지 않는 것으로 보여서)."

C1 테스트 흐름(`MqttPacketFragmentationTests`):

1. 임시 폴더에 `TestApp.WritePfx`로 PFX를 쓰고 `Tls.UseTls = true` 설정으로 앱을 시작한다.
2. `TcpClient`로 `MqttsPort`에 접속하고, 받은 바이트를 모두 메모리에 복사하는 기록용 `Stream` 래퍼를 `SslStream` 아래에 끼운다. TLS 1.2로 인증한다(지문 일치로 서버 인증서 확인).
3. MQTT 3.1 CONNECT → CONNACK, `dwd.v1/iot-server/{장치 키}` QoS 0 SUBSCRIBE → SUBACK를 읽는다.
4. 기록 위치를 표시한 뒤, 비TLS 포트의 MQTTnet 클라이언트(V310)가 그 장치 키의 `tisyn`을 보낸다.
5. `SslStream`에서 고정 헤더의 남은 길이만큼 PUBLISH 전체를 읽고, 토픽이 서버 → 장치 토픽이고 페이로드가 `"o":"w"` 응답인지 확인한다.
6. 표시 위치부터의 원시 바이트를 TLS 레코드 머리(형식 1바이트, 버전 2바이트, 길이 2바이트)로 나눠, 레코드가 정확히 하나(형식 23)이고 남는 바이트가 없는지 확인한다.

## 오류·위험

- 분할을 끄면 페이로드가 큰 패킷마다 `Join()`으로 버퍼를 한 번 더 복사한다. 장비 메시지는 수백 바이트이고 브로커 부하가 작아 영향이 없다.
- 원시 클라이언트가 PUBLISH 전체를 읽기 전에 다음 패킷이 오면 레코드가 늘어난다. 테스트 클라이언트는 keepalive 60초이고 다른 구독이 없어, 테스트 시간 안에 다른 패킷이 오지 않는다.
- TLS 1.2 레코드 형식은 cipher와 상관없이 머리 5바이트가 같다. 서버가 TLS 1.2로 고정되어 있어(R1) 레코드 머리로 나누는 방법이 늘 맞는다.
- 실장비 결과: 이 변경 뒤에도 장비가 동기되지 않을 수 있다(원인 가설이 틀린 경우). C5는 레코드 하나로 나가는지만 판정하고, 동기 여부는 #21 C7에서 판정한다.
- 배포: 서버 로컬 배포 폴더의 설정·DB·`secrets.dek`·인증서·log는 덮어쓰지 않는다. 서버 시작은 사용자가 한다(#23 배포와 같은 방식).

## 검증 전략

- C1: `MqttPacketFragmentationTests`의 TLS 테스트 통과. 변형 확인으로 `.WithoutPacketFragmentation()`을 잠시 빼면 레코드가 둘이 되어 실패하는지 보고 되돌린다.
- C2: `BuildServerOptions`를 인증서 없이·있게 각각 불러 `DefaultEndpointOptions.AllowPacketFragmentation`과 `TlsEndpointOptions.AllowPacketFragmentation`이 모두 `false`인지 단언하는 테스트 통과. 변형 확인은 C1과 함께 한다(빼면 실패).
- C3: `dotnet build --no-incremental` 경고 0·오류 0, `dotnet test` 전체 통과(기존 건너뜀 1건 제외).
- C4: 기준 spec에 R14·머리말·`파일:` 줄·이력 줄이 들어갔는지 읽어 확인. 구현 후 검토 diff에 나온다.
- C5: Release 빌드를 서버 로컬 배포 폴더에 배포(설정·DB·비밀·인증서·log 제외)하고 사용자가 서버를 시작한 뒤, `tshark`로 TLS 포트 8883을 장비 `tisyn` 간격보다 길게(약 140초) 캡처한다. `tisyn` 요청 레코드(장비 → 서버)마다 뒤따르는 서버 → 장비 응답이 TLS 레코드 하나인지 확인한다. 캡처 중 `tisyn`을 보낸 장비가 적어도 한 대여야 한다. 재요청 간격 관찰은 작업 문서에 적는다.

## 원칙 점검

- 적용 규칙:
  - workflow/spec-driven.md: 변경 문서·기준 spec 반영·검토·커밋 승인
  - workflow/repository-content.md: 저장소 파일·커밋에 로컬 경로·개인정보를 쓰지 않음(테스트·문서는 상대 경로와 예시 장치 키만 씀)
  - git/github-issue.md: 이슈 #25 참조, 종결 조건
  - 전역 language-preference: 대화는 한국어, 코드 주석·테스트 메시지는 저장소 관례(한국어 주석)
  - 사용자 메모: 실장비 상태를 바꾸는 명령 전에 대상 확인(이 작업은 장비에 명령을 보내지 않고 캡처만 함), 배포 때 설정·DB·비밀·인증서·log 덮어쓰지 않음
- 충돌: 없음
