# 다원 서버 중계 모드 계획

## 요약

`AppConfiguration`에 `DawonRelay` 하위 설정을 더하고, 켜져 있을 때만 세 곳이 중계 경로를 탄다.

- REST: `DevicesController`가 `DawonRestRelay`로 다원에 요청한다.
- MQTT: `MqttBrokerHostedService`가 이벤트마다 `DawonMqttRelay`를 부른다. `DawonMqttRelay`는 클라이언트마다 MQTTnet 클라이언트 하나로 다원 브로커에 접속한다.
- 기록: 두 중계가 `DawonRelayRecorder`로 날짜별 JSONL에 남긴다.

꺼져 있으면 중계 서비스를 부르지 않으므로 지금 코드 경로와 같다.

MQTT 중계는 MQTTnet 5.2(`MQTTnet.Server` 5.2.0.1603)의 브로커 이벤트를 쓴다. 이벤트별 쓰임은 「인터페이스·데이터」 절의 표에 둔다.

## 바꿀 파일·구조

- `src/Biz.Bizadm.PowerManager.Server.Spa/AppConfiguration.cs`: `DawonRelay`(`DawonRelayConfiguration?`) 속성을 추가하고, `Validate`에서 있으면 검증한다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Relay/DawonRelayConfiguration.cs`(새 파일): C1 설정과 기본값.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Relay/DawonRelayRecorder.cs`(새 파일): JSONL 기록. 날짜별 파일, 동시 쓰기 직렬화, 본문 인코딩(C9~C11).
- `src/Biz.Bizadm.PowerManager.Server.Spa/Relay/TopLevelJson.cs`(새 파일): 원문 JSON 바이트에서 최상위 속성 값의 바이트 범위를 찾아 바꾼다. `verify` 교체(C2)와 `mqtt_key`·`verify` 가림(C11)에 쓴다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Relay/DawonRestRelay.cs`(새 파일): 다원 REST 요청·응답·실패 처리(C2~C4), REST 기록.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Relay/DawonMqttRelay.cs`(새 파일): 클라이언트별 다원 접속(세션) 관리, 접속·구독·구독 해제·발행·종료 중계(C5~C8), MQTT 기록.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Relay/MqttConnectPacketReader.cs`(새 파일): `ValidatingConnectionEventArgs`의 비공개 필드 `_connectPacket`에서 Will을 읽는다(아래 결정).
- `src/Biz.Bizadm.PowerManager.Server.Spa/Relay/BufferRequestBodyAttribute.cs`(새 파일): 모델 바인딩 전에 요청 본문 버퍼링을 켜는 resource filter. 등록 원문 바이트를 다원에 보내는 데 쓴다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DevicesController.cs`: 켜짐이면 `Register`·`Connect`가 `DawonRestRelay`로 처리한다. `Register`에 `[BufferRequestBody]`를 붙인다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceRegistrationService.cs`: `StoreRelayedKeyAsync`(다원 키로 저장 또는 교체)를 추가한다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs`: 켜짐이면 브로커 이벤트를 `DawonMqttRelay`에 연결한다. 다원 → 클라이언트 주입 메시지는 해석되는 토픽이어도 핸들러 분배(mqtt-broker R4)에서 빼고, 해석하지 못한 토픽 로그(R7·R8)는 지금처럼 남긴다. R2~R4는 장치 → 서버 메시지에 대한 요구라 기준 spec과 어긋나지 않는다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Program.cs`: 설정이 켜져 있으면 `DawonRelayRecorder`·`DawonRestRelay`·`DawonMqttRelay`와 이름 있는 `HttpClient`를 등록한다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Biz.Bizadm.PowerManager.Server.Spa.csproj`: `MQTTnet` PackageReference를 추가한다. 지금은 `MQTTnet.Server`를 거쳐 전이로만 들어온다. 버전은 `Directory.Packages.props`의 기존 5.2.0.1603이다.
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/AppConfigurationTests.cs`: `DawonRelay` 기본값·검증 테스트.
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TopLevelJsonTests.cs`(새 파일): 값 범위 찾기·교체·가림.
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DawonRelayRecorderTests.cs`(새 파일): 파일 이름, 줄 형식, Base64, 동시 쓰기.
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DawonRelayRestTests.cs`(새 파일): 가짜 다원 HTTPS 서버로 C2~C4·C9·C11(REST).
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DawonRelayMqttTests.cs`(새 파일): 가짜 다원 MQTT 브로커로 C5~C9·C11(MQTT).
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/FakeDawon.cs`(새 파일): 가짜 다원 REST 서버(Kestrel HTTPS, 자체 서명 인증서)와 가짜 다원 브로커(MQTTnet TLS)를 띄우는 테스트 지원 코드. 가짜 브로커도 MQTTnet이라 SUBACK을 요청 QoS 내림차순으로 만든다. 그래서 받은 SUBSCRIBE의 필터 순서를 수신 패킷 이벤트에서 기록하고, 필터별 결과는 토픽으로 정하며(테스트가 지정), 나가는 SUBACK은 `InterceptingOutboundPacketAsync`에서 받은 순서로 다시 배열한다.
- `docs/features/dawon-relay.md`(새 기준 spec): R1~R13, 파일 목록, 이력.
- `docs/features/device-register.md`: 이번 작업의 영향을 반영한다.
  - `DevicesController.cs`·`DeviceRegistrationService.cs`에 중계 분기가 생긴다는 설명
  - R1·R2·R6·R8에 "중계 모드 켜짐이면 dawon-relay 기준"이라는 단서
  - 이력 한 줄
- `docs/features/mqtt-broker.md`: R1·R9에 "중계 모드 켜짐이면 dawon-relay 기준" 단서와 이력 한 줄.

## 결정

- **MQTT 중계 방식은 MQTTnet 브로커 이벤트로 한다.**
  - 이유:
    - 장치는 지금처럼 내장 브로커에 접속한다.
    - 장치 메시지의 로컬 처리(mqtt-broker R2~R9, C7)를 그대로 쓴다.
    - MQTTnet이 다원 쪽 클라이언트(3.1 지원, TLS)도 제공한다.
  - 검토한 대안: 바이트 수준 TCP/TLS 프록시(MQTTS 포트에서 다원으로 바이트를 넘기며 패킷을 해석). 장치가 보는 바이트는 가장 충실하다. 하지만 로컬 브로커를 거치지 않아, R2~R9 로컬 처리를 위한 MQTT 패킷 해석을 따로 만들어야 하고 비TLS 포트와도 이원화된다. 그래서 고르지 않았다.
- **장치 → 다원 발행은 `InterceptingInboundPacketAsync`의 `MqttPublishPacket`에서 보낸다(`InterceptingPublishAsync`가 아님).**
  - 이유:
    - 브로커가 대신 발행하는 Will도 `InterceptingPublishAsync`를 탄다(mqtt-broker R7 관찰, #12). 그래서 그 이벤트로는 Will과 장치 발행을 순서와 무관하게 구분할 수 없다.
    - 수신 패킷 이벤트는 클라이언트가 실제로 보낸 PUBLISH에만 불리므로, Will과 주입 메시지는 다원으로 가지 않는다(C6·C8).
  - 검토한 대안: `InterceptingPublishAsync`에서 Will 토픽·페이로드를 비교해 거르기. 장치가 Will과 같은 내용을 직접 발행하면 잘못 거르므로 고르지 않았다.
- **Will은 `ValidatingConnectionEventArgs`의 비공개 필드 `_connectPacket`(`MqttConnectPacket`)을 리플렉션으로 읽는다.**
  - 이유: MQTTnet 5.2 공개 API에 Will 토픽·페이로드·QoS·retain이 없다(리플렉션으로 확인한 공개 속성은 `WillDelayInterval`뿐). C5는 Will을 그대로 다원에 지정해야 한다.
  - 버전 변경 대비: 필드를 찾지 못하면 시작 시가 아니라 첫 접속 때 Error 로그를 남기고 그 접속을 서버 사용 불가로 거부한다. 패키지 버전은 중앙 관리로 고정돼 있다. 이 필드를 읽는 단위 테스트를 두어, 패키지를 올렸을 때 깨지면 바로 드러나게 한다.
  - 검토한 대안:
    - `InterceptingInboundPacketAsync`로 CONNECT 받기: CONNECT는 연결 확정 전에 세션 관리자가 직접 읽어 수신 패킷 이벤트를 타지 않는 구조로 보이고, 확인하지 못했다.
    - MQTTnet 포크: 유지 비용이 크다.
- **다원 → 클라이언트 메시지는 `InjectApplicationMessage`로 넣는다.**
  - `SenderClientId`를 `dawon-relay/<세션 ID>`로 두고, `InterceptingClientEnqueueAsync`에서 그 세션의 클라이언트 ID로 가는 것만 받는다(C6 "그 클라이언트에만").
  - retain은 보존해야 하지만 로컬 retained 저장소에 남기면 안 된다. 그래서 주입은 항상 `Retain=false`로 하고, 원래 retain 값을 세션의 대기 목록에 둔다. `InterceptingOutboundPacketAsync`에서 그 클라이언트로 나가는 PUBLISH 패킷의 `Retain`을 원래 값으로 되돌린다. 대기 목록은 토픽·페이로드·QoS로 짝짓고 FIFO로 꺼낸다.
  - 검토한 대안: retain을 그대로 주입. 로컬 브로커가 retained로 저장해 다른 클라이언트와 이후 구독에 퍼지고, 기존 구독자 전달 때 retain 비트를 0으로 바꾼다. 그래서 고르지 않았다.
- **세션은 브로커 접속 하나에 하나다. ClientId별 "현재 세션" 지도를 두고, 세션 객체마다 고유 ID를 두며, 현재 세션인지는 참조로 비교한다.**
  - 이유: 같은 ClientId가 다시 접속(takeover)할 때, MQTTnet은 새 접속을 확정한 뒤 옛 접속에 대해 `Takeover` 종류의 `ClientDisconnectedAsync`를 같은 ClientId로 발생시킨다. 또 새 접속의 다원 접속이 다원 쪽 옛 접속을 먼저 끊는다. 그래서 ClientId만으로는 옛 세션과 새 세션을 구분할 수 없다.
  - 새 접속의 `ValidatingConnectionAsync`에서 같은 ClientId의 현재 세션이 있으면, 그 세션을 "교체 중"으로 표시하고 다원에 접속한다.
    - 교체 중인 세션의 다원 접속이 끊기면, 로컬 클라이언트는 끊지 않고 끊겼다는 사실만 남긴다.
    - 새 세션의 다원 접속이 성공하면 현재 세션을 새 것으로 바꾸고, 옛 세션의 다원 클라이언트는 정리한다(다원이 이미 끊음). 이어 오는 `Takeover` 종류의 종료 이벤트는 무시한다.
    - 실패하면 옛 세션의 교체 중 표시를 지운다. 그사이 옛 다원 접속이 끊겼으면 옛 클라이언트를 `DisconnectClientAsync`로 끊는다(C8).
  - 다원 클라이언트의 `DisconnectedAsync`는 그 세션이 지도의 현재 세션(참조 비교)이고 교체 중이 아닐 때만 로컬 클라이언트를 끊는다.
  - `ClientDisconnectedAsync`(`Clean`·`NotClean`)도 지도의 현재 세션에만 적용하고, 지도에서 그 세션을 뺀다.
  - **접속 단위 구분**: MQTTnet은 새 접속 검증이 끝난 뒤(`CreateClientConnection`)에야 옛 접속을 takeover로 표시한다. 그 사이 옛 접속이 끊기면 `Clean`·`NotClean` 종료가 같은 ClientId로 온다.
    - 그래서 세션에 접속의 `RemoteEndPoint`(`ValidatingConnectionEventArgs`에서 얻음)를 둔다.
    - `RemoteEndPoint`가 있는 이벤트(`ClientDisconnectedAsync`, `InterceptingInboundPacketAsync`, `InterceptingOutboundPacketAsync`)는 ClientId와 끝점이 모두 맞는 세션에만 적용한다. 지도의 현재 세션과 교체 중인 옛 세션 둘 다 찾는다.
    - 맞는 세션이 없으면 무시한다.
  - 구독 가로채기(`InterceptingSubscriptionAsync`)에는 끝점이 없다. 그래서 아래 SUBACK 항목대로 수신 패킷 이벤트에서 세션에 저장한 "대기 SUBSCRIBE"(패킷 식별자별)로 세션을 찾는다.
  - 검토한 대안: `SessionItems`로 구분하기. clean session이 아니면 옛 접속과 새 접속이 같은 객체를 써서 구분되지 않으므로 고르지 않았다.
- **다원 접속을 DISCONNECT 없이 끊는 방법**: 다원 쪽 MQTTnet 클라이언트를 `DisconnectAsync` 없이 `Dispose`한다.
  - MQTTnet 5의 `Dispose`가 DISCONNECT 패킷을 보내지 않는다는 것은 확인하지 못했다. 그래서 T단계 첫 테스트(가짜 다원 브로커가 Will을 발행하는지)로 확인한다.
  - 보낸다면 다원 쪽 클라이언트의 TCP 스트림을 직접 만드는 방식으로 바꾼다. 이 경우 MQTTnet `WithTcpServer` 대신, 소켓을 닫아 끊을 수 있는 스트림 공급자를 쓴다. 이 대체는 구현 결함 처리(설계 범위 안)로 본다.
- **CONNACK 반환 코드 대응**: MQTTnet 클라이언트는 3.1 반환 코드를 `MqttClientConnectResultCode`로 바꿔 준다.
  - 이를 3.1 코드(1 프로토콜 버전, 2 식별자 거부, 3 서버 사용 불가, 4 사용자명·비밀번호 오류, 5 권한 없음)로 되돌려 기록한다.
  - 같은 코드에 대응하는 `MqttConnectReasonCode`를 `ValidatingConnectionEventArgs.ReasonCode`에 넣는다. 로컬 브로커는 3.1 클라이언트에 이를 다시 3.1 코드로 쓴다.
  - 대응표에 없는 값은 3(서버 사용 불가)으로 본다.
- **SUBACK: 다원 구독은 SUBSCRIBE 패킷 단위로 수신 패킷 이벤트에서 하고, 구독 가로채기는 그 결과를 적용하기만 한다.**
  - 이유:
    - MQTTnet은 SUBSCRIBE의 필터를 요청 QoS 내림차순으로 다시 정렬해 가로채기를 부르고, SUBACK 결과 코드도 그 순서로 쌓는다(`MqttClientSubscriptionsManager.Subscribe`, `MqttSubAckPacketFactory`). 필터별로 다원에 구독하면 다원 구독 순서·기록 순서·SUBACK 자리가 장치가 보낸 순서와 어긋난다.
    - 수신 패킷 이벤트는 구독 지도 잠금 밖, 그 클라이언트의 수신 흐름 안에서 불린다. 그래서 기다려도 된다.
  - 수신 패킷 이벤트에서 `MqttSubscribePacket`을 받으면 다음 순서로 처리한다.
    1. ClientId·끝점으로 세션을 찾는다.
    2. 패킷 식별자로 "대기 SUBSCRIBE"(원래 필터 순서의 필터·요청 QoS 목록)를 세션에 넣는다. 이 시점부터 보관이 시작된다(아래 보관 항목).
    3. 필터 전체를 같은 순서로 다원 SUBSCRIBE 하나로 보내고 SUBACK을 기다린다(10초).
    4. 필터별 결과를 대기 SUBSCRIBE에 저장한다.
    - 연결 오류·시간 초과면 그 대기 SUBSCRIBE를 "실패"로 표시한다.
  - 구독 가로채기는 같은 ClientId의 세션(현재 세션과 교체 중인 옛 세션)의 대기 SUBSCRIBE 가운데, 그 토픽 필터가 아직 적용되지 않은 첫 항목을 찾아 적용 표시하고 결과를 반영한다.
    - 허용 QoS 0~2이면 `TopicFilter`의 QoS를 그 값으로 바꾸고 `Response.ReasonCode`를 같은 `GrantedQoS`로 둔다.
    - 다원이 실패 값(0x80)을 주면 `ProcessSubscription=false`와 `UnspecifiedError`로 둔다.
    - 대기 SUBSCRIBE가 "실패"이거나 맞는 항목이 없으면 `CloseConnection=true`로 둔다(C6).
  - 그 클라이언트로 나가는 `MqttSubAckPacket`이 `InterceptingOutboundPacketAsync`에 보이면, 같은 식별자의 대기 SUBSCRIBE를 찾아 다음을 한다.
    - 결과 코드 목록을 원래 필터 순서의 다원 결과로 다시 배열한다.
    - 그 대기 SUBSCRIBE를 지운다. 지우는 것은 같은 식별자의 SUBACK에서만 한다.
  - 기록은 `subscribe`(원래 순서의 필터 목록)와 `suback`(다원 SUBACK 값 목록)이 SUBSCRIBE 패킷마다 한 쌍이다. 값은 MQTTnet 결과 코드의 숫자 값(0·1·2·128)이다.
  - 검토한 대안: 구독 가로채기에서 필터마다 다원 구독. 위 순서 문제와, 잇단 구독에서 세션을 찾지 못하는 문제가 있어 고르지 않았다.
- **구독 중 다원 메시지 보관**: 다원이 SUBACK 직후 보내는 메시지(retained 등)가 로컬 구독이 생기기 전에 주입되면 버려진다. `Subscribe`는 가로채기가 끝난 뒤에야 로컬 구독을 만들기 때문이다.
  - 세션에 대기 SUBSCRIBE(위 SUBACK 항목, 다원 구독 전에 넣음)가 하나라도 있는 동안, 다원에서 받은 메시지는 주입하지 않고 세션 목록에 순서대로 보관한다.
  - 그 클라이언트로 나가는 SUBACK에서 같은 식별자의 대기 SUBSCRIBE를 지운다. MQTTnet은 SUBACK에 SUBSCRIBE의 식별자를 그대로 쓰고, SUBACK은 그 SUBSCRIBE의 모든 필터 처리가 끝나 로컬 구독이 만들어진 뒤에 나간다.
  - 대기 SUBSCRIBE가 모두 없어지면 보관한 메시지를 순서대로 주입한다.
  - 식별자로 짝짓는 이유: 클라이언트가 SUBACK을 기다리지 않고 SUBSCRIBE를 잇달아 보내면, 앞 SUBACK이 뒤 SUBSCRIBE 처리 중에 나간다. 아무 SUBACK에서나 풀면 뒤 구독 직후의 메시지가 유실된다.
  - 기록은 받은 시점에 남긴다.
  - 접속이 끊기면 보관 목록을 버린다. 구독 중 접속을 끊는 경우(C6 실패)도 같다.
  - 검토한 대안: `ClientSubscribedTopicAsync`(필터별)에서 풀기. SUBSCRIBE 하나에 필터가 여럿이면 필터마다 풀 시점을 맞춰야 해서 고르지 않았다.
- **구독 해제는 `InterceptingInboundPacketAsync`의 `MqttUnsubscribePacket`에서 다원에 보낸다(`InterceptingUnsubscriptionAsync`는 쓰지 않는다).**
  - 이유: MQTTnet은 `InterceptingUnsubscriptionAsync`를 구독 지도의 쓰기 잠금(`ReaderWriterLockSlim`)을 잡은 채로 부른다. 이 안에서 다원 응답을 기다리면, 다른 스레드로 이어진 뒤 잠금 해제 예외나 교착이 생기고, 기다리는 동안 분배가 막힌다. 수신 패킷 이벤트는 그 잠금 밖에서 클라이언트 수신 흐름 안에 있다.
  - 패킷의 필터 목록 전체를 다원 UNSUBSCRIBE 하나로 보내고 UNSUBACK을 기다린다(10초).
  - 연결 오류·시간 초과면 다원 접속 끊김과 같이 클라이언트를 끊는다(C8 "다원 접속이 끊기면").
- **REST 원문 처리**
  - 등록 본문은 `[BufferRequestBody]` resource filter로 모델 바인딩 전에 `EnableBuffering`해 두었다가, 액션에서 처음으로 되감아 원문 바이트로 읽는다. mac 검증은 지금처럼 바인딩된 모델로 한다(C3).
  - 응답은 원문 바이트를 그대로 쓰고, `TopLevelJson`으로 최상위 `verify` 문자열 값의 바이트 범위만 바꾼다(C2).
  - 검토한 대안: 액션 인자를 원문으로 바꾸고 직접 역직렬화하기. 꺼짐 경로의 400 처리(`[ApiController]` 자동 응답)를 다시 만들어야 해서 고르지 않았다.
- **시간 제한은 상수 10초로 둔다**: REST 요청 전체, 다원 MQTT 접속, 구독, 구독 해제마다 적용한다.
  - 이유: C1은 설정 항목을 정해 두었고 시간 제한은 없다.
  - 검토한 대안: 설정으로 두기. spec 범위 밖이라 고르지 않았다.
- **다원 MQTT 접속 설정**
  - 프로토콜 3.1(`MqttProtocolVersion.V310`), TLS 1.2, SNI와 인증서 이름은 설정 호스트를 쓴다.
  - 비밀번호는 원문 바이트(`RawPassword`)를 그대로 넘긴다.
  - `SkipCertificateValidation`이 켜져 있으면 다원 MQTT TLS 인증서 검증도 끈다(`WithTlsOptions`의 인증서 검증 처리). REST의 `HttpClient`와 같은 설정을 쓴다(spec C1은 둘을 가리지 않는다). 꺼져 있으면 기본 검증이며, 검증 실패는 C5의 연결 실패 경로(코드 3)를 탄다.
  - keepalive는 장치 값을 그대로 쓴다. 다원 쪽 PINGREQ는 MQTTnet 클라이언트가 스스로 보내며, 장치의 PINGREQ와 1:1로 대응하지는 않는다(위험 절).
- **기록 쓰기**: `SemaphoreSlim` 하나로 직렬화하고, 항목마다 UTC 날짜 파일을 `FileMode.Append`로 열어 한 줄을 쓰고 닫는다(C10). 기록 실패는 Error 로그만 남기고 중계는 계속한다.

## 인터페이스·데이터

설정(YAML, `AppConfiguration.DawonRelay`):

```yaml
DawonRelay:
  Enabled: false                  # 기본 false
  ApiServerHost: dwapi.dawonai.com
  ApiServerPort: 18443
  MqttServerHost: dwmqtt.dawonai.com
  MqttServerPort: 8883
  RecordDirectory: dawon-relay    # ContentRootPath 기준 또는 절대 경로
  SkipCertificateValidation: false
```

`DawonRelay` 섹션이 없으면 꺼짐과 같다. 검증 규칙은 다음과 같다.

- 켜짐이면 두 호스트가 비어 있으면 안 되고, 두 포트가 1~65535여야 한다.
- 기본값은 `ConfigurationValidator`의 `Property` 기본값으로 채운다.

브로커 이벤트별 쓰임(켜짐):

| 이벤트 | 쓰임 |
|---|---|
| `ValidatingConnectionAsync` | 지금의 R9 로그 → 세션 생성·다원 접속·결과에 따라 `ReasonCode` 설정(C5) |
| `InterceptingInboundPacketAsync` | (ClientId·끝점으로 세션 찾음) 클라이언트 PUBLISH를 다원에 발행(C7), UNSUBSCRIBE를 다원에 해제(C6), SUBSCRIBE를 대기 SUBSCRIBE로 넣고 패킷 단위로 다원 구독·결과 저장(C6) |
| `InterceptingSubscriptionAsync` | 같은 ClientId 세션의 대기 SUBSCRIBE에서 그 필터의 미적용 항목을 찾아 결과 반영(C6) |
| `InterceptingPublishAsync` | 지금의 로컬 처리. `ClientId`가 `dawon-relay/`로 시작하는 주입 메시지는 해석되는 토픽이어도 핸들러에 분배하지 않음(해석 못 한 토픽의 R7 로그는 그대로) |
| `InterceptingClientEnqueueAsync` | 주입 메시지를 대상 클라이언트에만 전달(C6) |
| `InterceptingOutboundPacketAsync` | (ClientId·끝점으로 세션 찾음) 주입 메시지의 retain 복원, SUBACK 결과 코드를 원래 필터 순서로 재배열하고 같은 식별자의 대기 SUBSCRIBE를 지운 뒤 모두 없어지면 보관 메시지 주입(C6) |
| `ClientDisconnectedAsync` | ClientId·끝점이 맞는 현재 세션일 때만: `Clean`이면 다원 정상 종료, `NotClean`이면 DISCONNECT 없이 끊음. `Takeover`는 무시(C8) |
| 다원 클라이언트 `DisconnectedAsync` | 세션이 현재 세션(참조 비교)이고 교체 중이 아니면 `DisconnectClientAsync`. 교체 중이면 끊김만 표시(C8) |

C5 처리에서 "접속을 받아들인 뒤"의 기준은 `ValidatingConnectionAsync`가 끝나기 전에 다원 CONNACK을 받는 것이다. 그래서 다원 접속은 이 이벤트 안에서 기다린다.

기록 줄(JSON 한 줄, 공통 필드 `ts`·`channel`·`direction`·`kind`). 종류별 필드는 다음과 같다.

| channel | kind | direction | 필드 |
|---|---|---|---|
| `rest` | `request` | `device-to-dawon` | `method`, `path`, `body`, `bodyEncoding` |
| `rest` | `response` | `dawon-to-device` | `method`, `path`, `status`, `body`(최상위 `mqtt_key`·`verify` 가림), `bodyEncoding` |
| `rest` | `error` | `dawon-to-device` | `method`, `path`, `target`, `error` |
| `mqtt` | `connect` | `device-to-dawon` | `clientId`, `userName`, `hasPassword`, `keepAlive`, `cleanSession`, `will`(`topic`·`payload`·`payloadEncoding`·`qos`·`retain` 또는 `null`) |
| `mqtt` | `connack` | `dawon-to-device` | `clientId`, `returnCode` |
| `mqtt` | `subscribe` | `device-to-dawon` | `clientId`, `filters`(`topic`·`qos` 목록) |
| `mqtt` | `suback` | `dawon-to-device` | `clientId`, `results`(숫자 목록) |
| `mqtt` | `unsubscribe` | `device-to-dawon` | `clientId`, `filters`(토픽 목록) |
| `mqtt` | `publish` | 양방향 | `clientId`, `topic`, `qos`, `retain`, `payload`, `payloadEncoding` |
| `mqtt` | `disconnect` | 양방향 | `clientId`, `clean`(bool) |
| `mqtt` | `error` | `dawon-to-device` | `clientId`, `target`, `error` |

필드 값의 규칙은 다음과 같다.

- `bodyEncoding`·`payloadEncoding`은 `utf8` 또는 `base64`다.
- `ts`는 `DateTimeOffset.UtcNow`의 `O` 형식이다.
- `subscribe`·`suback`은 SUBSCRIBE 패킷마다 한 쌍으로 남고, 필터·값 목록은 장치가 보낸 필터 순서다.

`TopLevelJson`:

- `bool TryFindValue(ReadOnlySpan<byte> json, string name, out Range range, out JsonTokenType type)`: 최상위 객체에서 그 이름의 첫 속성 값 범위를 찾는다(객체·배열 값은 건너뛴 끝까지).
- `byte[] Replace(ReadOnlySpan<byte> json, Range range, ReadOnlySpan<byte> value)`
- `byte[] Mask(ReadOnlySpan<byte> json, params string[] names)`: 값을 `"***"`로 바꾼다. JSON 객체가 아니면 원문을 그대로 돌려준다.

`DeviceRegistrationService.StoreRelayedKeyAsync(mac, userId, modelId, company, mqttKey, ct)`:

- 없으면 새로 만든다(빈 값은 빈 문자열).
- 있으면 키를 바꾸고, 비어 있지 않은 값만 갱신하며, 마지막 요청 시각을 갱신한다.
- 같은 mac의 첫 등록 충돌(`DbUpdateException`)은 한 번 다시 시도해 갱신한다(마지막 저장 키).

## 오류·위험

- **비공개 필드 의존**: `_connectPacket`은 MQTTnet 내부 구현이다.
  - 패키지를 올리면 깨질 수 있다. 접속 거부와 Error 로그로 드러나고, 단위 테스트로 막는다.
  - 다원과 다른 Will로 접속하는 일은 없다. 읽지 못하면 접속하지 않기 때문이다.
- **Dispose의 DISCONNECT 여부**: 확인 전이다. 결정 절의 대체로 처리한다.
- **수신 패킷 이벤트의 발행과 로컬 처리 순서**: 다원 발행(수신 패킷 이벤트)이 로컬 처리(`InterceptingPublishAsync`)보다 앞선다.
  - 다원 발행이 실패해도(다원 접속이 끊김) 패킷 처리는 계속한다.
  - 다원 끊김은 C8에 따라 클라이언트 종료로 이어진다.
- **QoS 확인 응답**: 클라이언트의 PUBACK·PUBREC 응답은 로컬 브로커가 하고, 다원 쪽 확인 응답은 다원 클라이언트가 한다. 두 경로가 1:1로 맞지 않는다.
  - 장치가 받는 확인 시점이 다원 직접 통신과 다를 수 있다. 메시지 내용과 순서는 같다.
  - PINGREQ도 마찬가지다.
  - 이 차이는 기능 기준 spec과 근거 문서 반영 때 기록의 한계로 적는다.
- **SUBACK 재배열**: MQTTnet이 로컬 구독을 QoS 순으로 처리하므로, 나가는 SUBACK 결과 코드를 바꿔 원래 순서로 맞춘다. 로컬 구독 자체의 QoS는 가로채기에서 필터별로 맞췄으므로 결과와 어긋나지 않는다.
- **주입 메시지 retain 복원 짝짓기**: 같은 토픽·페이로드·QoS가 연달아 오면 FIFO로 짝짓는다. 같은 내용이라 순서가 바뀌어도 결과는 같다.
- **동시 등록**: 마지막 저장 키가 남고, 응답은 요청별 다원 본문이다(spec 변경 절).
- **기록 파일 크기**: 자르지 않으므로 파일이 커질 수 있다. 중계는 분석용으로 잠시 켜는 것을 전제로 하며, 정리는 운영자가 한다.
- **비밀 값**: 기록·로그에 `mqtt_key`·`verify`·비밀번호가 남지 않게 한다. 테스트에서 기록 파일과 로그 전체를 검색해 확인한다.
- **다원 서버 인증서**: 검증 실패면 C3·C5의 연결 실패 경로를 탄다. `SkipCertificateValidation`으로 끌 수 있다.

## 검증 전략

- **C1**:
  - `AppConfigurationTests`: 섹션이 없을 때 null, 섹션만 있을 때 기본값, 켜짐인데 호스트가 비었을 때 검증 실패를 확인한다.
  - 꺼짐 회귀: 기존 `DeviceEndpointTests`·`MqttBrokerTests`가 그대로 통과하고, 꺼짐이면 기록 폴더에 파일이 없다.
- **C2·C3·C4**: `DawonRelayRestTests`. 가짜 다원 HTTPS 서버는 응답을 시나리오별로 정하고 받은 요청을 기록한다. 테스트는 기본적으로 `SkipCertificateValidation`을 켜고 한다.
  - 인증서 검증 기본값: 설정을 끈 상태(기본)에서 자체 서명 인증서의 가짜 서버로 등록·connect를 보내면 502와 `error` 항목이 남고 DB가 바뀌지 않는다.
  - 성공 시 원문 그대로 전달(헤더 포함)과 응답 바이트를 확인한다. 응답은 공백·키 순서·이스케이프·다른 필드가 원문과 같고, `verify`만 다시 계산한 값이다.
  - `verify`가 없을 때와 문자열이 아닐 때 본문 그대로인지 확인한다.
  - DB 저장·교체와 다원 `Content-Type` 전달을 확인한다.
  - 실패 조건 각각(비200, `mqtt_key` 없음·빈 값·치환표 밖·문자열 아님, JSON 아님)에서 그대로 전달되고 DB가 바뀌지 않는지 확인한다.
  - 연결 실패(닫힌 포트)와 시간 초과(응답 지연)는 502다.
  - mac 오류는 다원 요청이 0건이고 400이다.
  - connect는 그대로 전달하고, 연결 실패면 502다.
- **C5~C8**: `DawonRelayMqttTests`. 가짜 다원 브로커는 MQTTnet TLS이며, 접속·구독·발행·Will을 관찰한다. 테스트 클라이언트는 MQTTnet `V310`이다. 테스트는 기본적으로 `SkipCertificateValidation`을 켜고 한다.
  - 인증서 검증 기본값: 설정을 끈 상태(기본)에서 자체 서명 인증서의 가짜 브로커로 접속하면 클라이언트가 반환 코드 3으로 거부되고 `error` 항목이 남는다.
  - 주입 메시지 로컬 처리: 다원이 해석되는 토픽(`{prefix}.v1/{deviceKey}/iot-server/json`)으로 보낸 메시지는 핸들러가 호출되지 않는다. 서버 → 장치 토픽으로 보낸 메시지는 R7 로그가 남는다.
  - C5: 자격 증명·keepalive·clean session·Will 전달, 다원 거부 코드 4·5 전달, 연결 실패 시 코드 3을 확인한다.
  - C6: 허용 QoS 전달(다원이 1로 낮춘 경우), 다원 실패 값, 구독 해제 전달을 확인한다. 다원이 발행한 메시지는 그 클라이언트에만 가고, retain 값이 보존되며, 로컬 retained에 남지 않는다.
  - C6 구독 직후 메시지: 가짜 다원 브로커에 retained 메시지를 미리 두고 구독하면, 클라이언트가 SUBACK 뒤에 그 메시지를 retain 1로 받는다. 20회 되풀이해 한 번도 빠지지 않는지 확인한다.
  - 가짜 브로커 사전 검사: 테스트 클라이언트가 가짜 다원 브로커에 직접 A(QoS 0)·B(QoS 1)를 SUBSCRIBE 하나로 보내면, 지정한 결과가 받은 순서([A 결과, B 결과])로 돌아오는지 확인한다.
  - C6 다중 필터: 요청 QoS가 0·1인 필터 둘(원래 순서 A QoS 0, B QoS 1)을 SUBSCRIBE 하나로 보내고, 가짜 다원이 A에 1, B에 0x80을 준다. 다원이 받은 SUBSCRIBE가 하나이고 필터 순서가 A·B이며, 클라이언트 SUBACK이 [1, 0x80] 순서인지 확인한다.
  - C6 잇단 구독: SUBACK을 기다리지 않고 SUBSCRIBE 둘(둘째는 필터 둘)을 보내고, 둘째 SUBSCRIBE의 두 필터에 다원 retained 메시지를 두면, 두 필터 모두 다원에 구독되고 그 메시지들이 빠지지 않는다(20회).
  - C6 구독 해제: 필터 둘을 담은 UNSUBSCRIBE가 다원에 한 번에 가고, 해제 뒤 그 토픽의 다원 메시지가 클라이언트에 오지 않는다.
  - C7: 다원 수신과 로컬 핸들러 호출을 함께 확인한다.
  - C8: 정상 종료 시 다원에 Will이 없고, 소켓을 끊으면 다원이 Will을 발행하며 로컬 Will은 다원에 가지 않는다. 다원 측에서 끊으면 클라이언트가 끊긴다.
  - 접속 단위 구분: 세션 지도와 끝점 비교는 단위 테스트로 확인한다. 같은 ClientId, 다른 끝점의 종료 이벤트가 현재 세션에 적용되지 않아야 한다.
  - takeover: 같은 ClientId로 새로 접속하면 새 접속과 새 다원 접속이 유지되고 옛 로컬 접속만 끊긴다. 새 접속의 다원 접속이 거부되면 옛 접속이 끊긴다(가짜 다원이 옛 접속을 이미 끊은 경우).
- **C9·C10**: `DawonRelayRecorderTests`(형식, 날짜 파일, Base64 표시, 병렬 1000줄 쓰기 후 줄마다 JSON 파싱 성공)와, REST·MQTT 테스트의 기록 줄 검사로 확인한다.
- **C11**: REST·MQTT 테스트가 끝난 뒤, 기록 파일과 로그 폴더 전체에 테스트 키·`verify`·비밀번호 문자열이 없는지 검사한다.
- **C12**: `dotnet test`로 위 테스트와 기존 테스트가 모두 통과하는지 확인한다.
- **C13(수동)**: 사용자와 함께 한다.
  - 중계를 켠 서버에 B540 실장비 1을 연결하고(장치 설정은 device-config, 대상 SSID는 사용자 확인 후), 다원 앱에서 켜기·끄기를 한 번 한다.
  - 기록 파일에 남은 종류(`request`·`response`·`connect`·`connack`·양방향 `publish`)를 작업 문서에 적는다.

## 원칙 점검

- 적용 규칙:
  - `workflow/spec-driven.md`: 세 문서, 기준 spec 반영, 검토
  - `git/github-issue.md`: #20
  - `workflow/repository-content.md`: 기록 파일·실제 키는 저장소에 넣지 않고, 문서 경로는 저장소 기준 상대 경로
  - `dotnet/server-spa-template.md`·`dotnet/server-api-template.md`: YAML 설정 검증, ProblemDetails, MSTest + TestServer
  - `dotnet/kms-config-secrets.md`: 새 설정에 비밀 값이 없어 KMS 대상이 아님. `mqtt_key`는 지금처럼 DB에 저장
  - `dotnet/nuget-package-selection.md`: 새 패키지 없이 기존 중앙 버전 `MQTTnet` 5.2.0.1603을 직접 참조로만 추가
  - `shell/powershell-windows.md`: 검증 명령
  - `workflow/conversation-language.md`
- 충돌: 없음.
  - MQTTnet 비공개 필드 리플렉션은 규칙 위반은 아니지만 위험이라 오류·위험 절에 적었다.
  - 시간 제한 상수는 spec C1의 설정 목록을 늘리지 않으려는 결정이다.
