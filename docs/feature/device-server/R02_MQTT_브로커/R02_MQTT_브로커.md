# R02 MQTT 브로커

## 목적

장치가 다원 브로커 대신 PowerManager 서버에 MQTT로 접속해 상태를 보고하고 제어 메시지를 받으려 한다. `Server.Spa`에 내장한 브로커(MQTTnet)는 등록된 장치만 등록 키로 받아들이고, 장치 → 서버 메시지를 토픽·SenML 유사 JSON으로 해석해 `IDeviceMessageHandler`로 분배하며, 시간 동기 요청에 응답하고, 서버의 제어 메시지를 장비에게 보낸다. 장비까지 메시지가 가는지 가릴 수 있도록 접속·구독·전달을 로그로 남긴다.

## 요구사항

| ID | 요구사항 | 상태 | 일감 |
| --- | --- | --- | --- |
| `R02-R1` | 브로커는 `MqttPort`에 비TLS 엔드포인트를 열고, `Tls.UseTls`가 `true`이면 `MqttsPort`에 `Tls` 서버 인증서로 TLS 1.2·1.3을 받고 TLS 1.0·1.1을 거절하는 엔드포인트를 연다(암호 목록은 `R02-R18`). 브로커는 MQTT 3.1(`MQIsdp`) 클라이언트를 받으며, 접속은 `R02-R9` 판정이 `Match`일 때만 받는다(인증을 끄는 설정은 없다). | 반영 | #35 |
| `R02-R2` | `DeviceTopic.TryParse`는 `{prefix}.v1/{deviceKey}/iot-server/{접미}`(빈 토큰 무시)를 해석하고 끝 토큰이 `lwt`이면 LWT로 본다. 토큰이 4개 미만이거나 접두가 비었거나 `.v1`로 끝나지 않거나 셋째 토큰이 `iot-server`가 아니면 실패한다. 서버 → 장치 토픽은 `{prefix}.v1/iot-server/{deviceKey}`다. | 반영 | |
| `R02-R3` | 해석한 장치 → 서버 메시지가 JSON 오류이거나 `msg.o`·`msg.e`가 없으면 브로커는 Warning 로그를 남기고 분배하지 않는다. | 반영 | |
| `R02-R4` | 브로커는 `msg.o`가 `rg`·`drg`·`tisyn`·`notify`이면 각각 `RegisterAsync`·`DeregisterAsync`·`TimeSyncAsync`·`NotifyAsync`로, `n`(M130-W가 쓰는 `notify`의 짧은 형태)이면 `NotifyAsync`로(원래 값 `n`을 그대로 넘김), LWT 토픽은 페이로드와 관계없이 `OfflineAsync`로 분배한다. 핸들러가 예외를 던지면 Error 로그로 남기고 브로커로 번지지 않게 하며, 브로커 종료 중 취소는 로그 없이 넘긴다. | 반영 | |
| `R02-R5` | 기본 핸들러(`LoggingDeviceMessageHandler`)는 받은 내용을 Information 로그로 남긴다. `tisyn`을 받으면 그 요청 토픽의 접두·장치 키로 만든 서버 → 장치 토픽(`{prefix}.v1/iot-server/{장치 키}`, `R02-R2`)에 retain 없이 `{"sid":<요청 sid, 없으면 "0">,"msg":{"o":"w","e":[{"n":"/99/0/1","sv":"<값>"}]}}`(값은 응답을 만들 때 서버 시각의 UTC Unix epoch 초를 소수점 아래 버린 10진수 문자열)을 보내고, Information 로그 항목 하나에 장치 키, 토픽, `sid`, 보낸 값을 남긴다. 그 밖의 서버 → 장치 메시지는 `R02-R17`의 명령 발행으로 보낸다. | 반영 | |
| `R02-R6` | 해석에 성공했지만 `msg.o`가 `R02-R4`의 다섯 값이 아니면 브로커는 Warning 로그 항목 하나에 오퍼레이션, 장치 키, 토픽, ClientId, 원본 페이로드(UTF-8 디코딩, 이스케이프하지 않음)를 남긴다. | 반영 | |
| `R02-R7` | `DeviceTopic.TryParse`로 해석하지 못한 토픽(장치 키 없는 `/lwt` Will 포함)의 메시지이면 브로커는 그대로 전달하고, Information 로그 항목 하나에 토픽, ClientId, 원본 페이로드(`R02-R6`과 같은 형식)를 남긴다. 서버가 브로커에 주입한 메시지(`tisyn` 응답 등)는 남기지 않는다. MQTT 클라이언트가 PUBLISH한 메시지와 브로커가 클라이언트 대신 보내는 Will은 ClientId와 상관없이(ClientId가 `power-manager`여도) 남긴다. | 반영 | |
| `R02-R8` | `R02-R6`·`R02-R7`의 원본 페이로드가 4096바이트를 넘으면 브로커는 앞 4096바이트만 디코딩해 남기고, 잘림 여부와 원래 바이트 수를 같은 로그 항목에 남긴다. | 반영 | |
| `R02-R9` | 클라이언트가 접속하면(거부한 접속 포함) 브로커는 Information 로그 항목 하나에 ClientId, 사용자명, 비밀번호 유무, 비밀번호 판정(`Match`·`Mismatch`·`Unknown`)을 남긴다. ClientId가 `DAWONDNS-{12자리 16진수}`이고 그 mac(대소문자 무관)이 등록돼 있으면(`R01`) 비밀번호가 등록 `mqtt_key`와 같을 때 `Match`, 다르거나 없으면 `Mismatch`, 그 밖에는 `Unknown`이다. 판정을 확인하는 중 예외가 나면 Error 로그를 남기고 판정은 `Unknown`이다. 비밀번호 값은 어떤 로그에도 남기지 않는다. `Match`이면 접속을 받고, `Mismatch`·`Unknown`(예외 때 빼고)이면 거부해 MQTT 3.1·3.1.1 클라이언트는 CONNACK 반환 코드 4(잘못된 사용자명 또는 비밀번호)를 받으며, 예외 때는 거부해 반환 코드 3(서버 사용 불가)을 받는다(`R02-R1`). | 반영 | |
| `R02-R10` | 클라이언트가 SUBSCRIBE를 보내면 브로커는 그 안의 토픽 필터마다 Information 로그 항목 하나에 ClientId, 토픽 필터, 요청 QoS를 남긴다. 구독을 거부하거나 바꾸지 않으며, SUBACK 결과와 그 뒤 메시지 전달은 이 로그가 없을 때와 같다. | 반영 | |
| `R02-R11` | 클라이언트가 UNSUBSCRIBE를 보내면 브로커는 그 안의 토픽 필터마다 Information 로그 항목 하나에 ClientId와 토픽 필터를 남긴다. 구독 해제를 거부하지 않는다. | 반영 | |
| `R02-R12` | 서버 → 장치 토픽 메시지를 구독 클라이언트의 큐에 넣으면 브로커는 받는 클라이언트마다 Information 로그 항목 하나에 보낸 ClientId(서버가 주입한 메시지는 `power-manager`), 받는 ClientId, 토픽을 남긴다. 서버 → 장치 토픽은 빈 토큰을 빼고 토큰이 3개 이상이며, 첫 토큰이 `.v1`로 끝나고 그 앞 접두가 비지 않으며, 둘째 토큰이 `iot-server`인 토픽이다(`R02-R2`의 서버 → 장치 토픽과 그 뒤에 접미가 붙은 형태). 그 밖의 토픽(장치 → 서버 토픽, `/lwt` 등)에는 남기지 않는다. | 반영 | |
| `R02-R13` | 서버 → 장치 토픽(`R02-R12`) 메시지에 맞는 구독이 없어 아무 클라이언트에게도 가지 않으면 브로커는 Warning 로그 항목 하나에 보낸 ClientId와 토픽을 남긴다. 서버 → 장치 토픽에만 남긴다. | 반영 | |
| `R02-R14` | 브로커는 비TLS·TLS 엔드포인트 모두 패킷 분할을 끈 설정(`AllowPacketFragmentation = false`)으로 열어, 클라이언트에 보내는 MQTT 패킷마다 한 번에 쓴다. TLS 엔드포인트에서 MQTT 패킷 전체가 16384바이트 이하인 PUBLISH는 TLS 레코드 하나로 나간다. | 반영 | |
| `R02-R15` | 토픽을 해석한 장치 → 서버 메시지를 받으면 브로커는 분배하기 전에 보낸 ClientId와 함께 장비 상태 저장소에 넘긴다(상태 반영 규칙은 `admin/R04-R1`·`admin/R04-R3`·`admin/R04-R4`). LWT와 본문을 해석하지 못한 메시지(`R02-R3`)는 토픽과 ClientId만 넘긴다. 이 처리에서 예외가 나면 Error 로그로 남기고 분배를 막지 않으며, 기존 분배(`R02-R4`)와 로그는 그대로다. | 반영 | #28 |
| `R02-R16` | 클라이언트가 접속하거나 끊기면 브로커는 장비 상태 저장소에 알려 온라인 여부를 바꾼다(`admin/R04-R2`). 같은 ClientId의 새 접속으로 옛 연결이 끊긴 것(`Takeover`)이나 지금 접속의 연결이 아닌 끊김은 오프라인으로 보지 않는다. | 반영 | #28 |
| `R02-R17` | 명령 발행(`IDeviceCommandPublisher.PublishAsync`)을 부르면 브로커는 대상의 접두·장치 키로 만든 서버 → 장치 토픽(`R02-R2`)에 retain 없이 QoS 0으로 `{"sid":…,"msg":{"o":…,"e":[…]}}`을 주입한다. `sid`는 서버 실행마다 100부터 1씩 늘린 10진 문자열이다. ClientId `DAWONDNS-{mac}`의 전송 큐에 넣었다는 브로커 이벤트를 최대 3초 기다려, 받으면 넣은 것으로, 아니면(구독자 없음 포함) 넣지 못한 것으로 판정한다. Information 로그 항목 하나에 mac, 토픽, `sid`, 오퍼레이션, 요소를 남기고, 넣지 못하면 Warning, 브로커가 없거나 예외가 나면 Error 로그로 같은 내용을 남긴다. `R02-R12`·`R02-R13` 로그는 그대로 남는다. | 반영 | #28 |
| `R02-R18` | 브로커는 MQTTS 엔드포인트에 HTTPS와 같은 규칙으로 `Tls.IncludeCipherSuites`를 적용한다. 없거나 비었거나 공백뿐이면 암호를 제한하지 않고 경고도 없다(OS 기본). Windows가 아닌 OS에서는 유효한 이름의 암호로만 핸드셰이크를 받고(목록에 없는 암호만 제시하면 거절), 잘못된 이름은 HTTPS와 같은 경고를 남기고 빼며, 남은 이름이 없으면 제한하지 않는다. 목록에 TLS 1.3 암호가 없을 때의 HTTP/3 경고는 MQTTS 때문에 다시 남기지 않는다. Windows에서는 공백이 아닌 글자가 있으면 HTTPS와 같은 문구로 경고하고, 이름을 해석하지 않은 채 목록을 무시한다. `R02-R1`의 TLS 1.3 수락은 목록이 적용될 때 목록에 TLS 1.3 암호가 있는 경우를 말한다. 인증서 체인(`Tls.CertificateChain`)은 MQTTS에 적용하지 않는다. | 반영 | #35 |

## 수용 기준

| ID | 대상 | 기준 | 최신 검증 |
| --- | --- | --- | --- |
| `R02-A1` | `R02-R1` | MQTT 3.1 클라이언트가 등록 키로 비TLS 포트와 MQTTS 포트에 접속하면 둘 다 받는다. MQTTS에 TLS 1.2·1.3으로는 핸드셰이크가 되고 TLS 1.0·1.1은 거절된다. `Tls.UseTls`가 `false`이면 MQTTS 포트가 열리지 않는다. | `V02_10` |
| `R02-A2` | `R02-R2` | `dwd.v1/DAWONDNS-B540_W-{mac}/iot-server/notify/json`은 접두 `dwd`·장치 키·접미로, 끝이 `lwt`인 토픽은 LWT로 해석되고, 토큰 3개, 빈 접두, `.v1` 없는 접두, 셋째 토큰이 `iot-server`가 아닌 토픽은 실패한다. 서버 → 장치 토픽은 `dwd.v1/iot-server/{장치 키}`로 만들어진다. | |
| `R02-A3` | `R02-R3` | JSON이 아니거나 `msg.o`·`msg.e`가 빠진 메시지를 보내면 Warning 로그가 남고 핸들러가 불리지 않는다. | |
| `R02-A4` | `R02-R4` | 다섯 오퍼레이션과 LWT 토픽마다 대응하는 핸들러 메서드가 불리고 `n`은 `NotifyAsync`에 `n` 그대로 넘어간다. 핸들러가 예외를 던지면 Error 로그가 남고 브로커와 다른 클라이언트는 계속 동작한다. | `V02_03` |
| `R02-A5` | `R02-R5` | `sid` `"7"`인 `tisyn`을 보내면 같은 장치의 서버 → 장치 토픽에 retain 없이 `sid` `"7"`, `o` `w`, `/99/0/1`에 응답 시각 epoch 초 문자열이 담긴 메시지가 오고, `sid` 없는 요청에는 `"0"`이 오며, 응답마다 장치 키·토픽·`sid`·값이 든 Information 로그가 하나 남는다. | `V02_05` |
| `R02-A6` | `R02-R6` | 모르는 오퍼레이션(예: `xyz`)을 보내면 Warning 로그 하나에 오퍼레이션·장치 키·토픽·ClientId·이스케이프하지 않은 원본 페이로드가 남는다. | `V02_03` |
| `R02-A7` | `R02-R7` | 장치 키 없는 `/lwt` Will과 해석되지 않는 토픽의 PUBLISH는 구독자에게 전달되고 각각 Information 로그 하나가 남으며, ClientId `power-manager`인 클라이언트의 PUBLISH도 남는다. 서버의 `tisyn` 응답 주입은 이 로그를 남기지 않는다. | `V02_08` |
| `R02-A8` | `R02-R8` | 4097바이트 이상인 페이로드의 로그에는 앞 4096바이트만 디코딩되어 있고 잘림 표시와 원래 바이트 수가 함께 있다. 4096바이트 이하면 잘림 표시가 없다. | `V02_02` |
| `R02-A9` | `R02-R9` | 등록된 mac의 ClientId가 등록 키로 접속하면 `Match`로 받고, 다른 비밀번호·비밀번호 없음은 `Mismatch`로, 등록되지 않은 mac이나 다른 형식의 ClientId는 `Unknown`으로 CONNACK 4를 받으며, 판정 중 예외는 CONNACK 3을 받는다. 모든 경우에 로그 하나가 남고 비밀번호 값은 로그에 없다. | `V02_09` |
| `R02-A10` | `R02-R10` | 토픽 필터 둘을 한 SUBSCRIBE로 보내면 필터마다 ClientId·필터·QoS 로그가 남고, SUBACK과 그 뒤 메시지 수신은 로그 없이 동작할 때와 같다. | `V02_06` |
| `R02-A11` | `R02-R11` | 토픽 필터 둘을 한 UNSUBSCRIBE로 보내면 필터마다 ClientId·필터 로그가 남고 구독이 해제된다. | `V02_06` |
| `R02-A12` | `R02-R12` | 서버 → 장치 토픽(접미가 붙은 형태 포함)을 구독한 클라이언트 둘에게 메시지가 가면 받는 클라이언트마다 로그 하나가 남고, 서버 주입이면 보낸 ClientId가 `power-manager`다. 장치 → 서버 토픽과 `/lwt`에는 이 로그가 없다. | `V02_06` |
| `R02-A13` | `R02-R13` | 구독자가 없는 서버 → 장치 토픽에 보내면 Warning 로그 하나에 보낸 ClientId와 토픽이 남고, 구독자 없는 장치 → 서버 토픽에는 남지 않는다. | `V02_06` |
| `R02-A14` | `R02-R14` | 두 엔드포인트의 옵션에서 패킷 분할이 꺼져 있고, TLS 포트로 16384바이트 이하의 PUBLISH를 받을 때 그 패킷이 TLS 애플리케이션 데이터 레코드 하나에 담겨 온다. | `V02_07` |
| `R02-A15` | `R02-R15` | 장치 → 서버 메시지를 보내면 분배 전에 장비 상태 저장소가 토픽·ClientId·메시지를 받고, LWT와 본문 해석 실패 메시지는 토픽·ClientId만 받는다. 저장소가 예외를 던져도 Error 로그가 남고 핸들러 분배와 기존 로그는 그대로다. | `admin/V04_02` |
| `R02-A16` | `R02-R16` | 장비가 접속하면 온라인, 끊으면 오프라인이 되고, 같은 ClientId로 다시 접속해 옛 연결이 끊긴 경우에는 온라인으로 남는다. | `admin/V04_02` |
| `R02-A17` | `R02-R17` | 구독 중인 장비에 명령을 발행하면 서버 → 장치 토픽에 retain 없이 QoS 0, `sid` `"100"`부터 1씩 늘어난 메시지가 오고 넣은 것으로 판정되며 Information 로그가 남는다. 구독자가 없으면 3초 안에 넣지 못한 것으로 판정되고 Warning 로그가, 브로커가 없으면 Error 로그가 남는다. | `admin/V04_02` |
| `R02-A18` | `R02-R18` | Linux에서 목록에 든 암호로는 MQTTS 핸드셰이크가 되고 목록 밖 암호만 제시하면 거절되며, 잘못된 이름은 HTTPS와 같은 경고와 함께 빠지고, 목록이 비거나 유효한 이름이 없으면 제한이 없다. Windows에서는 목록이 있으면 HTTPS와 같은 경고가 남고 제한이 없다. MQTTS에는 `Tls.CertificateChain`이 쓰이지 않는다. | `V02_10` |

## 지금 동작

- 토픽·메시지 형태의 근거는 B540 펌웨어 덤프 분석(`docs/reference/b540-mqtt-topic.md`)이다. `tisyn` 응답 형식은 같은 문서의 「시간 동기」 절에 있다.
- 실장비 M130-W는 근거와 다른 `msg.o` `n`(토픽 접미 `notify/json`의 상태 보고)과 장치 키 없는 `/lwt`를 쓴다. 관찰 내용은 `docs/reference/mqtt-device-control.md` 5절에 있다.
- 패킷 분할을 끄는 것(`R02-R14`)은 장비 MQTT 라이브러리가 여러 수신에 걸친 패킷을 합치지 않는 것으로 보여서다. 분할된 PUBLISH에 장비가 반응하지 않았다.
- B540 실장비의 MQTT 비밀번호는 등록 `mqtt_key`와 같다. 그래서 인증을 강제한다(`R02-R9`).

## 파일

- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs`
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/LoggingDeviceMessageHandler.cs`
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttDeviceCommandPublisher.cs`
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/DownlinkDeliveryTracker.cs`
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttServerAccessor.cs`
- `src/Biz.Bizadm.PowerManager.Core/Mqtt/DeviceTopic.cs`
- `src/Biz.Bizadm.PowerManager.Core/Mqtt/DeviceTopicKind.cs`
- `src/Biz.Bizadm.PowerManager.Core/Mqtt/MqttEnvelope.cs`
- `src/Biz.Bizadm.PowerManager.Core/Mqtt/MqttOperation.cs`
- `src/Biz.Bizadm.PowerManager.Core/Mqtt/IDeviceMessageHandler.cs`
- `src/Biz.Bizadm.PowerManager.Core/Mqtt/IDeviceCommandPublisher.cs`
- `src/Biz.Bizadm.PowerManager.Core/Mqtt/DeviceCommandTarget.cs`
- `src/Biz.Bizadm.PowerManager.Core/Mqtt/DevicePublishResult.cs`
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs`
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TimeSyncResponseTests.cs`
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttSubscribeLogTests.cs`
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttPacketFragmentationTests.cs`
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttTlsPolicyTests.cs`
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TestDevices.cs`
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TestMqtt.cs`
- `tests/Biz.Bizadm.PowerManager.Core.Tests/DeviceTopicTests.cs`
- `tests/Biz.Bizadm.PowerManager.Core.Tests/SerializationTests.cs`

## 의존

- `R01-R5`(등록 저장소의 mac과 `mqtt_key`)
- `admin/R04-R1`~`admin/R04-R4`(장비 상태 저장소의 반영 규칙)

## 범위 밖

- 인증을 끄는 설정

## 기존 문서

- 없음

## 번호 대장

| 번호 | 제목 | 출처 | 상태 |
| --- | --- | --- | --- |
| `D02_01` | 응답 `sid`는 요청의 `sid`를 그대로 쓴다(없으면 `"0"`) | 옛 `tisyn-response` D001, `T02_05` | 유효 |
| `D02_02` | 기준 spec R5는 `tisyn` 응답만 적고, 명령 발행(`w`·`chg`·`reb`)은 구현하지 않은 채 둔다 | 옛 `tisyn-response` D002, `T02_05` | 유효 |
| `D02_03` | 기준 spec R5에는 발행 실패 처리를 적지 않고, tasks T007에서 그 항목을 뺀다 | 옛 `tisyn-response` D003, `T02_05` | 유효 |
| `D02_04` | C7 간격에 구간 시작부터 첫 `tisyn`까지를 넣고, `tisyn`이 없는 구간을 정의한다 | 옛 `tisyn-response` D004, `T02_05` | 유효 |
| `D02_05` | 구독 로그에 서버 → 장치 메시지 전달 로그를 더한다 | 옛 `mqtt-subscribe-log` D001, `T02_06` | 유효 |
| `D02_06` | 전달 로그에서 버림을 빼고 큐에 넣은 것만 남긴다 | 옛 `mqtt-subscribe-log` D002, `T02_06` | 유효 |
| `D02_07` | B540 분석 결과의 출처를 바로 적고 근거 문서 반영은 #21에 맡긴다 | 옛 `mqtt-subscribe-log` D003, `T02_06` | 바뀜 → `D02_08` |
| `D02_08` | 재분석 결과의 근거 문서 반영을 후속 작업으로 둔다 | 옛 `mqtt-subscribe-log` D004, `T02_06` | 유효 |
| `D02_09` | C1 테스트는 필터를 QoS가 높은 순서로 보내 필터마다 승인 결과를 단언한다 | 옛 `mqtt-subscribe-log` D005, `T02_06` | 유효 |
| `D02_10` | 비TLS 엔드포인트의 분할 끔은 서버 옵션 값으로 확인한다 | 옛 `mqtt-no-fragmentation` D001, `T02_07` | 유효 |
| `D02_11` | C5는 캡처 중 tisyn을 보낸 실장비 모두의 응답을 확인한다 | 옛 `mqtt-no-fragmentation` D002, `T02_07` | 유효 |
| `D02_12` | C1의 판정 문구를 원시 TLS 레코드 개수로 바꾼다 | 옛 `mqtt-no-fragmentation` D003, `T02_07` | 유효 |
| `D02_13` | C1 테스트는 R5와 R12(또는 R13) 로그가 모두 보인 뒤 R7 없음을 본다 | 옛 `injected-message-log` D001, `T02_08` | 유효 |
| `D02_14` | 판정이 `Unknown`인 접속은 거부한다 | 옛 `mqtt-connect-auth` D001, `T02_09` | 유효 |
| `D02_15` | 접속 확인 중 예외가 나면 거부한다 | 옛 `mqtt-connect-auth` D002, `T02_09` | 유효 |
| `D02_16` | 인증 강제를 켜고 끄는 설정을 두지 않는다 | 옛 `mqtt-connect-auth` D003, `T02_09` | 유효 |
| `D02_17` | 예외 때 판정은 `Unknown`으로 두고 C3에서 예외를 뺀다 | 옛 `mqtt-connect-auth` D004, `T02_09` | 유효 |
| `D02_18` | C3·C4의 MQTT 3.1.1 확인은 tasks.md에만 더한다 | 옛 `mqtt-connect-auth` D005, `T02_09` | 유효 |
| `D02_19` | catch 주석 수정은 tasks.md T004에만 더한다 | 옛 `mqtt-connect-auth` D006, `T02_09` | 유효 |
| `D02_20` | MQTTS는 TLS 1.2와 1.3을 받는다 | 옛 `mqtts-tls-policy` D001, `T02_10` | 유효 |
| `D02_21` | 서버 인증서 체인도 MQTTS에 적용한다 | 옛 `mqtts-tls-policy` D002, `T02_10` | 바뀜 → `D02_22` |
| `D02_22` | 인증서 체인은 이번 범위에서 뺀다 | 옛 `mqtts-tls-policy` D003, `T02_10` | 유효 |
| `D02_23` | 리눅스 서버에 배포해 확인한다 | 옛 `mqtts-tls-policy` D004, `T02_10` | 유효 |
| `D02_24` | 목록에 TLS 1.3 암호가 없을 때 MQTTS는 경고를 남기지 않는다 | 옛 `mqtts-tls-policy` D005, `T02_10` | 유효 |
| `D02_25` | C1의 버전별 목록 조건은 판정 조건이 아닌 범위 설명으로 둔다 | 옛 `mqtts-tls-policy` D006, `T02_10` | 유효 |
| `D02_26` | 암호 목록 해석·경고를 OS 판단과 분리해 리눅스 경로를 작업 PC에서 테스트한다 | 옛 `mqtts-tls-policy` D007, `T02_10` | 유효 |
| `D02_27` | MQTTnet DLL 근거 문장을 고치고 정책 전달을 메타데이터 근거로 적는다 | 옛 `mqtts-tls-policy` D008, `T02_10` | 유효 |
| `D02_28` | 정책 생성 앞에 OS 확인을 다시 두어 CA1416을 피한다 | 옛 `mqtts-tls-policy` D009, `T02_10` | 유효 |
| `D02_29` | TLS 1.0 거절도 TLS 1.1과 같은 방법으로 확인한다 | 옛 `mqtts-tls-policy` D010, `T02_10` | 유효 |
| `D02_30` | TLS 1.0·1.1 openssl 확인 이력의 출처를 밝힌다 | 옛 `mqtts-tls-policy` D011, `T02_10` | 유효 |
| `D02_31` | 작업 PC의 TLS 1.0·1.1 핸드셰이크 테스트는 두지 않는다 | 옛 `mqtts-tls-policy` D012, `T02_10` | 유효 |
| `T02_01` | 서버 골격과 내장 브로커 | `R02-R1`~`R02-R5`(#1) | 끝남 |
| `T02_02` | 해석 불가 메시지 원본 로그 | `R02-R6`~`R02-R8`(#12) | 끝남 |
| `T02_03` | M130-W 상태 보고 분배 | `R02-R4`, `R02-R6`(#14) | 끝남 |
| `T02_04` | tisyn 응답 분석 근거 문서 | 없음(근거 문서)(#18) | 끝남 |
| `T02_05` | tisyn 시간 동기 응답 | `R02-R5`(#21) | 끝남 |
| `T02_06` | 구독과 전달 기록 로그 | `R02-R10`~`R02-R13`(#23) | 끝남 |
| `T02_07` | 패킷 분할 끄기 | `R02-R14`(#25) | 끝남 |
| `T02_08` | 주입 메시지 로그 제외 | `R02-R7`(#22) | 끝남 |
| `T02_09` | 접속 인증 강제 | `R02-R1`, `R02-R9`(#26) | 끝남 |
| `T02_10` | MQTTS TLS 버전과 암호 목록 | `R02-R1`, `R02-R18`(#35) | 끝남 |
