# MQTTS TLS 버전·암호 목록 적용

기능: mqtt-broker(기준 spec docs/features/mqtt-broker.md)
이슈: GitHub #35

## 무엇을, 왜

설정 `Tls.IncludeCipherSuites`는 HTTPS(Kestrel) 엔드포인트에만 적용되고, 내장 MQTT 브로커의 MQTTS 엔드포인트는 서버 인증서와 TLS 1.2 고정만 설정한다(`src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs` `BuildServerOptions`). 그래서 리눅스 서버에서 HTTPS는 목록 밖 암호를 거절하지만 MQTTS는 OS 기본 암호를 모두 받고(목록에 없는 `ECDHE-RSA-AES128-SHA` 수락), TLS 1.3은 거절한다(2026-11-23 리눅스 서버에서 `openssl s_client`로 확인).

MQTTS도 같은 설정의 암호 목록을 따르게 하고, TLS 버전은 HTTPS와 같게 1.2·1.3을 받는다(D001). MQTTnet 5.2의 TLS 엔드포인트 옵션(`MqttServerTlsTcpEndpointOptions`)에 `SslProtocol`과 `CipherSuitesPolicy` 속성이 있다(리플렉션으로 확인).

장치(B540 `1.01.36`, M130-W)의 ClientHello는 TLS 1.2에 `TLS_RSA_WITH_AES_256_CBC_SHA256`·`TLS_RSA_WITH_AES_256_CBC_SHA`·`TLS_RSA_WITH_AES_128_CBC_SHA256`·`TLS_RSA_WITH_AES_128_CBC_SHA`(그 밖에 RSA_PSK 4종, 재협상 SCSV)만 제시한다(2026-11-23 리눅스 서버 tcpdump 캡처 9개, 무작위값을 빼면 모두 같음). 목록을 MQTTS에 적용하면 목록에 장치 암호가 있어야 장치가 붙는다.

## 완료 조건
- C1. `Tls.UseTls`가 `true`이면 MQTTS 엔드포인트는 TLS 1.2와 TLS 1.3 핸드셰이크를 받고, TLS 1.0·1.1 핸드셰이크는 거절한다. C1의 TLS 1.3 수락은 암호 목록이 적용될 때(C2) 목록에 TLS 1.3 암호가 있는 경우를 말한다(D006).
- C2. Windows가 아닌 OS에서 `Tls.IncludeCipherSuites`에 유효한 이름이 하나 이상 있으면 MQTTS 엔드포인트는 그 목록의 암호로만 핸드셰이크를 받는다. 목록에 있는 TLS 1.2 암호로는 연결되고, 목록에 없는 암호만 제시하면 거절한다.
- C3. `Tls.IncludeCipherSuites`가 없거나 비었거나 공백뿐이면 MQTTS는 암호를 제한하지 않고 경고도 남기지 않는다(OS 기본). Windows가 아닌 OS에서 잘못된 이름은 HTTPS와 같은 경고를 남기고 빼며, 남은 이름으로 C2를 따르고, 남은 이름이 없으면 제한하지 않는다. 목록에 TLS 1.3 암호가 없을 때의 HTTP/3 경고는 MQTTS 때문에 다시 남기지 않는다(D005).
- C4. Windows에서 `Tls.IncludeCipherSuites`에 공백이 아닌 글자가 있으면 MQTTS도 HTTPS와 같은 문구로 경고하고, 이름을 해석하지 않은 채 목록을 무시하고 엔드포인트를 연다.
- C5. MQTTS의 다른 동작은 그대로다. 서버 인증서, 패킷 분할 끔(mqtt-broker R14), 접속 인증(R1의 접속 판정·R9), 비TLS 엔드포인트는 바뀌지 않고, HTTPS의 코드 동작(Kestrel의 TLS 버전·암호 처리)도 바뀌지 않는다. mqtt-broker R1의 TLS 1.2 고정은 C1로 바뀐다.
- C6. 리눅스 서버에 배포한 뒤(D004), 그 서버의 설정 `IncludeCipherSuites: TLS_AES_256_GCM_SHA384,TLS_CHACHA20_POLY1305_SHA256,TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,TLS_AES_128_GCM_SHA256,TLS_AES_128_CCM_SHA256,TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,TLS_RSA_WITH_AES_256_CBC_SHA256,TLS_RSA_WITH_AES_128_CBC_SHA256`로 MQTTS가 장치 암호 `AES256-SHA256`·`AES128-SHA256`과 TLS 1.3을 받고 목록 밖 `ECDHE-RSA-AES128-SHA`·`AES128-SHA`를 거절하며, B540 실장비가 다시 접속해 비밀번호 판정 `Match`로 구독까지 간다. 이 설정은 2026-11-23 11:01에 이미 서버에 적용돼 있어 배포 때 설정은 바꾸지 않는다(D004).

## 범위 밖
- 서버 인증서 체인(`Tls.CertificateChain`)을 MQTTS에 적용하는 것(D003). 리눅스 서버의 MQTTS는 지금 OS 신뢰 저장소에 든 중간·루트 인증서로 HTTPS와 같은 체인을 보낸다. 그 저장소에 인증서가 없는 서버에서는 MQTTS가 체인 없이 서버 인증서만 보낼 수 있다(미확인).
- MQTTS만 따로 쓰는 암호 목록 설정. HTTPS와 같은 `Tls.IncludeCipherSuites`를 쓴다.
- HTTPS(Kestrel)의 TLS 버전·암호 처리 변경.
