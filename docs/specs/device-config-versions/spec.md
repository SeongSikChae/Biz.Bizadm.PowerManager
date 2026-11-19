# DeviceConfig ConnectAp에 참고 구현 V1·V2·V4 흐름 추가

기능: device-config(기준 spec docs/features/device-config.md) / device-config-mcp(기준 spec docs/features/device-config-mcp.md) / device-config-app(기준 spec 없음, 이번에 만듦)
이슈: GitHub #13

## 무엇을, 왜

#11에서 `ConnectAp` 설정 JSON을 참고 구현 [PowerManagerConfig](https://github.com/SeongSikChae/PowerManagerConfig)의 `ConfigratorV3`에 맞췄고, 실장비 B540(`DWD-LS120_501f96`)이 공유기 접속과 서버 등록 요청까지 도달했다(`docs/features/device-config.md` 이력). 참고 구현은 펌웨어·모델마다 흐름을 나눈다(`PowerManagerConfig/IConfigrator.cs` 주석: `ConfigratorV1` "M130, B540(V1.0.26)", `ConfigratorV2` "B540 (v1.0.28)", `ConfigratorV3` "B540 (v1.0.30, v1.0.32)", `ConfigratorV4` "B550 (v1.0.10)"). 지금 도구는 V3 흐름만 보내므로 다른 펌웨어·모델은 설정할 수 없다. 설정에서 흐름을 골라 V1·V2·V4도 보낼 수 있게 한다.

참고 구현에서 장치와 주고받는 부분만 옮긴다(다원 클라우드 REST 호출 `GetMqttAuth`·`MqttAuthAdd`·`MqttKeyChange`는 옮기지 않는다). 흐름별 장치 송수신(핸드셰이크 뒤):

| 흐름 | 설정 JSON(`IDeviceCommunicator.SendConfigrationAsync`, 기본 직렬화) | 설정 뒤 |
|---|---|---|
| V1 | `MqttConfiguration`: `server_addr`, `server_port`, `ssl_support`, `ssid`, `pass`, `mqtt_key`, `company`, `model`, `topic` | 응답을 읽지 않고 연결 종료 |
| V2 | `MqttConfigurationV2`(V3와 같음) | 응답 하나를 읽고 `{"delay":"0"}`을 보낸 뒤 응답을 읽지 않고 연결 종료 |
| V3 | `MqttConfigurationV2` | 응답 → `{"mac":"<mac>","command":"connectap"}` → 응답 (지금 구현) |
| V4 | `MqttConfigurationV2` | 응답을 읽지 않고 `{"mac":"<mac>","command":"connectap"}`을 보낸 뒤 응답을 읽지 않고 연결 종료 |

V4는 핸드셰이크에서 `hello`를 보낸 뒤 프레임 하나를 무조건 읽어 버리고, 그것이 `[DUT->PC] BUTTON_`으로 시작하면 하나를 더 버린 다음에 `START_OK`를 받는다(`ConfigratorV4`). 지금 핸드셰이크는 `BUTTON_…#` 이벤트만 건너뛰고(`device-config` R3) 그 밖의 프레임이 `START_OK`가 아니면 실패하므로, V4는 `START_OK` 앞의 다른 프레임을 건너뛰도록 바꾼다.

이 도구가 참고 구현에 더한 동작(`apscan`으로 등록 Wi-Fi 대조 후 선택, 로그 마스킹)은 모든 흐름에 똑같이 적용한다. 설정 JSON의 기본 직렬화는 참고 구현과 같다(`IDeviceCommunicator.SendConfigrationAsync`). V1·V2·V4 대상 펌웨어·모델 장비는 지금 없으므로 실장비 확인은 V3만 한다.

## 완료 조건

- C1. 설정에 흐름 값 `Protocol`(`V1`, `V2`, `V3`, `V4` 중 하나, 대소문자 구분, 기본 `V3`)을 둘 수 있다. 그 밖의 값은 설정 오류다. 지금 필수인 Settings 필드(`ApiServerAddr`, `ApiServerPort`, `ServerAddr`, `ServerPort`, `UserId`)는 흐름과 관계없이 필수로 유지한다(V1 설정 JSON에 넣지 않는 값도 포함).
- C2. `Protocol`이 `V1`이면 ConnectAp의 설정 JSON은 키 `server_addr`, `server_port`, `ssl_support`, `ssid`, `pass`, `mqtt_key`, `company`, `model`, `topic`을 이 순서로 담고(값은 모두 문자열), 보낸 뒤 응답을 기다리지 않고 연결을 닫는다. `connectap`은 보내지 않는다.
- C3. `Protocol`이 `V1`이면 `mqtt_key` 값은 설정 `MqttKey`에서 온다. `MqttKey`는 V1일 때 필수이며 없으면 ConnectAp가 PC Wi-Fi를 장치 AP로 바꾸기 전에 실패한다(MCP는 `device-config-mcp` R9의 입력 검사와 같은 시점, App은 기동 시 설정 검증). 기동 시 `WifiProfiles[].Pass`와 같은 방식(OsKms, `DEKENC:`)으로 암호화해 설정 파일에 다시 쓴다. MCP `settings` 인자로는 `MqttKey`를 받지 않는다.
- C4. `Protocol`이 `V2`면 설정 JSON(V3와 같은 키) → 응답 프레임 하나 수신(내용은 검사하지 않음) → `{"delay":"0"}` 송신 후 응답을 기다리지 않고 연결을 닫는다.
- C5. `Protocol`이 `V3`이거나 지정하지 않으면 지금 동작(#11)과 같다.
- C6. `Protocol`이 `V4`면 핸드셰이크에서 `START_OK` 앞에 오는 다른 프레임을 최대 2개까지 건너뛰어 MAC을 얻고(3개째도 `START_OK`가 아니면 실패), 설정 JSON(V3와 같은 키) 송신 → 응답을 기다리지 않고 `{"mac":"<mac>","command":"connectap"}` 송신 → 응답을 기다리지 않고 연결을 닫는다.
- C7. 모든 흐름에서 ConnectAp는 핸드셰이크(V4는 C6)와 `apscan` Wi-Fi 대조를 지금처럼 한 뒤 설정 JSON을 보내고, 설정 JSON은 `System.Text.Json` 기본 인코더로 직렬화한다. 로그의 설정 JSON은 `pass`와 `mqtt_key`를 `***`로 가리며, 실제 값(원문·이스케이프된 형태)은 로그·예외 메시지에 남지 않는다.
- C8. MCP: 설정 파일 `Settings.Protocol`·`Settings.MqttKey`를 읽고, `run_device_commands`의 `settings` 인자에 `protocol`을 더해 필드 단위로 덮어쓴다. tool 설명이 흐름 선택을 알린다.
- C9. App: 설정 파일 `Settings.Protocol`·`Settings.MqttKey`를 읽어 같은 흐름으로 실행한다.
- C10. 솔루션 빌드가 경고 없이 성공하고, 흐름별 송수신을 모의 장치로 확인하는 테스트를 포함해 전체 테스트(실장비 `Device` 카테고리 제외)가 통과한다.
- C11. 고친 MCP를 `C:\Bizadm.PowerManager.Config`에 다시 배포해 `DWD-LS120_501f96`에 `Protocol` 기본값(V3)으로 `ConnectAp`를 보내고, 서버가 `POST /api/v1/devices/register/create`를 받는지 확인한다(#11과 같은 결과면 통과).
