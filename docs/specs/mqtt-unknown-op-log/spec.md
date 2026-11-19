# MQTT 해석 불가 메시지의 ClientId·원본 페이로드 로그

기능: mqtt-broker(기준 spec 없음, 이번에 만듦)
이슈: GitHub #12

## 무엇을, 왜

실장비(M130-W 장치 3, 장치 키 `DAWONDNS-M130-W-{mac}`)를 내장 MQTT 브로커(`MqttBrokerHostedService`)에 연결하자 근거 문서(당시 커밋되지 않는 로컬 분석 폴더에 있었음, 지금 `docs/reference/b540-mqtt-topic.md`, B540 펌웨어 덤프 기반)와 다른 메시지가 두 가지 관찰됐다.

1. 근거 문서에 없는 `msg.o` 값 `n`. 브로커는 알 수 없는 오퍼레이션으로 보고 다음 경고만 남긴다.

   ```
   Unknown device operation "n": "DAWONDNS-M130-W-{mac}"
   ```

2. 장치 키가 없는 토픽 `/lwt`(Will 메시지로 추정). `DeviceTopic.TryParse`가 실패해 다음 로그만 남는다.

   ```
   Unparsed topic passed through: "/lwt"
   ```

두 로그 모두 메시지 내용과 보낸 연결을 알 수 없어, `n`이 상태 보고(`notify`)인지, `/lwt`의 페이로드가 무엇인지 판단할 수 없다. 또 `/lwt`는 토픽에 장치 키가 없어, 나중에 장치를 식별하려면 MQTT 연결의 ClientId와 장치 키의 대응을 알아야 한다. 이번 작업은 두 로그에 ClientId와 원본 페이로드를 함께 남겨, 실장비 메시지 형태를 로그로 확인하고 ClientId를 이후 대응 확인의 재료로 삼는다. 이번 로그만으로 모든 장치의 ClientId–장치 키 대응을 보장하지는 않는다(알 수 없는 오퍼레이션을 보낸 장치는 C1 로그 한 항목에서 둘을 함께 볼 수 있다).

원본 페이로드를 남기는 이유: `MqttElement`는 `n`·`sv`만 역직렬화하므로, 역직렬화된 요소만 남기면 다른 필드(숫자 값 등)가 빠져 형태 확인에 쓸 수 없다.

이번 범위가 아닌 것(로그로 실장비 형태를 확인한 뒤 따로 한다):

- `n`을 어떤 오퍼레이션으로 처리할지
- 장치 키 없는 `/lwt` 토픽을 오프라인으로 처리하는 것(ClientId로 장치를 찾는 일 포함)

## 완료 조건

- C1. 해석에 성공했지만 `msg.o`가 알려진 오퍼레이션(`rg`·`drg`·`tisyn`·`notify`)이 아닌 메시지를 받으면, Warning 로그 항목 하나에 오퍼레이션, 장치 키, 토픽, ClientId, 원본 페이로드(UTF-8로 디코딩한 문자열, 이스케이프하지 않음)가 함께 남는다.
- C2. `DeviceTopic.TryParse`로 해석하지 못한 토픽(`/lwt` 포함)의 메시지를 받으면, Information 로그 항목 하나에 토픽, ClientId, 원본 페이로드(C1과 같은 형식)가 함께 남는다. 메시지는 지금처럼 그대로 전달한다.
- C3. C1·C2의 원본 페이로드가 4096바이트를 넘으면 앞 4096바이트만 디코딩해 남기고, 잘렸다는 표시와 원래 바이트 수를 같은 로그 항목에 함께 남긴다.
- C4. 알려진 오퍼레이션 분배(`switch`의 `RegisterAsync`·`DeregisterAsync`·`TimeSyncAsync`·`NotifyAsync` 분기)와 LWT 분배(`OfflineAsync`)·해석 실패 경고 코드는 바뀌지 않는다(기준점 대비 diff로 확인). 기존 테스트도 통과한다.
- C5. C1·C2·C3을 확인하는 자동 테스트가 있고 통과한다. C2 테스트는 브로커가 대신 발행한 Will 메시지(`/lwt`)에서 끊긴 클라이언트의 ClientId가 로그에 남는 것을 포함한다.
