# ConnectAp 설정 JSON을 참고 구현 V3에 맞춤

기능: device-config(기준 spec 없음, 이번에 만듦)
이슈: GitHub #11

## 무엇을, 왜

DeviceConfig `ConnectAp`로 B540(`DWD-LS120_501f96`)에 설정을 보내면 장치는 설정 JSON과 `connectap` 모두 `respone: OK`로 응답하지만 공유기에 접속하지 않는다(2026-11-19: DNS 서버 조회·서버 접속 없음, SoftAP가 채널 1에 남음). 같은 네트워크의 M130은 정상 접속하므로 네트워크·서버 문제가 아니며, 이 도구로 장치가 공유기에 접속한 것은 확인된 적이 없다(`docs/cli/0001-device-config-방안.md` 미확인 항목).

참고 구현 [PowerManagerConfig](https://github.com/SeongSikChae/PowerManagerConfig)는 B540 펌웨어 버전마다 흐름을 나눈다(`PowerManagerConfig/IConfigrator.cs` 주석: `ConfigratorV1` "M130, B540(V1.0.26)", `ConfigratorV2` "B540 (v1.0.28)", `ConfigratorV3` "B540 (v1.0.30, v1.0.32)"). 대상 장치 `501f96`의 펌웨어 버전은 확인하지 않았다. 이 작업은 `connectap` 메시지를 쓰는 흐름 중 가장 최신인 `ConfigratorV3`를 기준으로 삼는다. V3는 설정 JSON을 `MqttConfigurationV2`로 보낸다(`PowerManagerConfig/MqttConfiguration.cs`, `IDeviceCommunicator.cs`). 현재 도구의 설정 JSON은 이와 형식이 두 가지 다르다.

1. V3에 없는 `mqtt_key`를 넣는다(실행마다 임의 16자). 참고 구현에서 `mqtt_key`를 장치에 보내는 것은 `ConfigratorV1`(`MqttConfiguration`)뿐이다.
2. `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`으로 직렬화해 `+ & < > '`와 비ASCII를 그대로 보낸다. V3는 `System.Text.Json` 기본 직렬화(`JsonSerializer.Serialize(config)`)라 이 문자들을 `\uXXXX`로 보낸다.

ConnectAp의 설정 JSON 형식을 V3와 같게 만들어 장치의 공유기 접속 여부를 관찰하고, 접속하면 이 두 형식 차이를 미접속 원인으로 확정한다(접속하지 않을 때의 처리는 C6). ConnectAp의 나머지 흐름(핸드셰이크, 같은 연결의 `apscan`과 Wi-Fi 선택, `connectap` 메시지, 응답 확인)과 설정값(주소·포트·`lati`·`long` 등 설정 파일·`settings` 인자에서 오는 값)은 바꾸지 않는다.

이 변경은 `docs/cli/0001-device-config-방안.md`의 `mqtt_key` 결정(실행마다 임의 생성해 설정 JSON에 넣음, 키 목록의 `mqtt_key`)과 `DeviceSettings`의 relaxed 인코더 판단(장치 파서가 `\uXXXX`를 처리한다는 근거가 없음)을 대체한다. `0001`은 지난 작업 기록으로 두고 고치지 않으며, 지금 요구는 기능 `device-config`의 기준 spec(`docs/features/device-config.md`)에 둔다.

## 완료 조건

- C1. ConnectAp가 보내는 설정 JSON의 키와 순서가 `mac`, `api_server_addr`, `api_server_port`, `server_addr`, `server_port`, `ssl_support`, `ssid`, `pass`, `user_id`, `company`, `model`, `lati`, `long`, `topic`이며 `mqtt_key`가 없다. 값은 모두 문자열이다.
- C2. 설정 JSON 직렬화가 `System.Text.Json` 기본 인코더와 같은 결과를 낸다(예: `pass`가 `a+b&c<d>'e`면 `a\u002Bb\u0026c\u003Cd\u003E\u0027e`로 보낸다).
- C3. 로그에 남기는 설정 JSON은 `pass`만 `***`로 가리고, 장치로 보낸 실제 `pass`는 로그·예외 메시지에 남지 않는다.
- C4. ConnectAp의 송신 순서(`[DUT<-PC] START` → `hello tcp SUCCESS_CONNECT` → `{"command":"apscan"}` → 설정 JSON → `{"mac":"<mac>","command":"connectap"}`)와 응답 확인은 바뀌지 않는다.
- C5. 솔루션 빌드가 경고 없이 성공하고, C1~C4에 맞게 고친 테스트를 포함해 전체 테스트(실장비 `Device` 카테고리 제외)가 통과한다.
- C6. 고친 DeviceConfig MCP를 `C:\Bizadm.PowerManager.Config`에 다시 배포해 `DWD-LS120_501f96`에 `ConnectAp`를 보내고, 장치의 공유기 접속 여부(DNS 서버의 `dwapi`/`dwmqtt` 조회 또는 서버 접속)를 관찰해 결과를 기록한다. 접속 성공 여부는 완료 조건이 아니다. 접속하면 두 형식 차이가 원인이었다고 기록한다. 접속하지 않으면 대상 장치의 펌웨어 버전이 확인되지 않아 V3 흐름이 맞는지 알 수 없으므로, 원인 판단을 보류하고 관찰 결과만 기록한다.
