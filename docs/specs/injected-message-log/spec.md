# 서버 주입 메시지 해석 불가 토픽 로그 제외

기능: mqtt-broker(기준 spec docs/features/mqtt-broker.md)
이슈: GitHub #22

## 무엇을, 왜

서버는 `tisyn` 시간 동기 요청마다 그 장치의 서버 → 장치 토픽(`{prefix}.v1/iot-server/{장치 키}`)으로 응답을 브로커에 주입한다(mqtt-broker R5, 보낸 ClientId `power-manager`). 주입한 메시지도 브로커 발행 가로채기(`MqttBrokerHostedService.OnPublishAsync`)를 지나고, 서버 → 장치 토픽은 장치 → 서버 토픽 형식이 아니라 `DeviceTopic.TryParse`가 실패한다. 그래서 응답마다 R7의 Information 로그 `Unparsed topic passed through: "dwd.v1/iot-server/...", clientId="power-manager", ...`가 하나씩 남는다. 장치는 동기될 때까지 `tisyn`을 짧은 간격으로 다시 보내므로 이 로그가 많이 쌓인다(2026-11-20 실장비 M130-W 3대에서 관찰).

R7 로그는 실장비가 보내는 토픽 형태를 확인하려는 것이다. 서버가 주입한 메시지의 토픽·페이로드는 서버가 정한 것이고, 응답 내용은 이미 R5 응답 로그(`Time sync response sent`)에, 전달은 R12·R13 로그에 남는다. 그래서 서버가 브로커에 직접 주입한 메시지는 R7 로그를 남기지 않게 한다.

구분 기준은 보낸 ClientId 문자열이 아니라 서버가 주입했는지다. MQTT 클라이언트가 ClientId `power-manager`로 접속해 보낸 메시지와, 접속이 끊겨 브로커가 클라이언트 대신 보내는 Will(장치 키 없는 `/lwt` 포함)은 서버가 주입한 것이 아니므로 지금처럼 R7 로그를 남긴다. 주입 메시지의 전달(구독자 큐에 넣음·구독자 없음, R12·R13)과 그 로그, 클라이언트가 보낸 메시지의 처리는 바꾸지 않는다.

## 완료 조건

- C1. 서버가 브로커에 주입한 메시지(지금은 `tisyn` 응답)는 `DeviceTopic.TryParse`로 해석하지 못하는 토픽이어도 R7 로그(`Unparsed topic passed through`)를 남기지 않는다. 그 메시지는 지금처럼 구독 클라이언트에 전달되고, R12(큐에 넣음, 보낸 ClientId `power-manager`)·R13(구독자 없음) 로그와 R5 응답 로그도 지금처럼 남는다.
- C2. MQTT 클라이언트가 PUBLISH한 메시지와, 접속이 끊겨 브로커가 그 클라이언트 대신 보내는 Will은 ClientId와 상관없이(ClientId가 `power-manager`인 클라이언트 포함) 해석하지 못한 토픽이면 지금처럼 R7 로그를 남긴다(장치 키 없는 `/lwt` Will 포함). 해석한 토픽의 분배(R3~R6)도 바뀌지 않는다.
- C3. 기준 spec `docs/features/mqtt-broker.md`의 R7에 서버가 주입한 메시지는 로그 대상에서 뺀다는 것을 반영하고, 머리말에 한 문장을 더하고, `파일:` 줄을 이번에 더하거나 바꾼 파일에 맞추고, 이력에 이번 작업 한 줄을 더한다.
- C4. `dotnet build`가 경고 0으로 끝나고 `dotnet test` 전체에서 실패가 없다. C1·C2는 자동 테스트로 확인한다.
