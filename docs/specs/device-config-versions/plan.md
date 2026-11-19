# DeviceConfig ConnectAp에 참고 구현 V1·V2·V4 흐름 추가 계획

## 요약

라이브러리에 흐름 열거형 `DeviceProtocol`(V1~V4)을 두고, `DeviceSettings`에 `Protocol`(기본 V3)과 복호화된 `MqttKey`를 더한다. `DeviceSettings.ToJson`은 V1이면 구형 9개 키, 그 밖에는 지금의 14개 키를 만든다. `DeviceClient.ConnectApAsync`는 핸드셰이크·`apscan`·Wi-Fi 선택 뒤 흐름별로 송수신한다. V4 핸드셰이크는 `START_OK` 앞 프레임을 최대 2개 건너뛴다. 응답을 기다리지 않는 흐름(V1·V2·V4)은 송신을 종료한 뒤 장치가 닫을 때까지 수신을 비우고(검사 없음, 시간 초과는 실패 아님) 연결을 닫는다. MCP와 App은 YAML `Settings.Protocol`(문자열)·`Settings.MqttKey`를 읽고 `MqttKey`를 OsKms로 암호화하며, MCP `settings` 인자에 `protocol`을 더한다.

## 바꿀 파일·구조

라이브러리(`device-config`)
- `src/Biz.Bizadm.PowerManager.DeviceConfig/Protocol/DeviceProtocol.cs`(새 파일): `public enum DeviceProtocol { V1, V2, V3, V4 }`와 `DeviceProtocols.TryParse(string?, out DeviceProtocol)`(이름과 정확히 같을 때만, 대소문자 구분)
- `src/Biz.Bizadm.PowerManager.DeviceConfig/Protocol/DeviceSettings.cs`: `Protocol`(기본 `V3`), `MqttKey`(`string?`, 복호화된 값) 속성 추가. `ToJson`이 V1이면 `server_addr`, `server_port`, `ssl_support`, `ssid`, `pass`, `mqtt_key`, `company`, `model`, `topic` 순서로 만들고, 마스킹은 `pass`·`mqtt_key`를 `***`로
- `src/Biz.Bizadm.PowerManager.DeviceConfig/Protocol/DeviceMessages.cs`: `Delay = "{\"delay\":\"0\"}\n"` 추가
- `src/Biz.Bizadm.PowerManager.DeviceConfig/Transport/DeviceConnection.cs`: 기존 `HandshakeAsync(CancellationToken)`은 두고 오버로드 `HandshakeAsync(int maxSkippedFrames, CancellationToken)`(`START_OK`가 아닌 프레임을 최대 N개 건너뜀) 추가, 응답을 기다리지 않는 송신 `SendAsync`(공개), 정상 종료 `CloseGracefullyAsync`(`Socket.Shutdown(SocketShutdown.Send)` 뒤 장치가 닫을 때까지(수신 0바이트) 수신을 읽어 비움. 받은 프레임은 RX 로그만 남기고 검사하지 않으며, `Device.TimeoutSeconds` 안에 닫히지 않으면 실패 없이 닫음) 추가
- `src/Biz.Bizadm.PowerManager.DeviceConfig/Transport/DeviceClient.cs`: `ConnectApAsync`에서 V4는 `HandshakeAsync(2)`, 흐름별 송수신(V1: 설정 송신 → 종료 / V2: 설정 → 응답 1개(검사 없음) → `Delay` 송신 → 종료 / V3: 지금 그대로 / V4: 설정 송신 → `connectap` 송신 → 종료). V1에서 `MqttKey`가 비면 장치에 아무것도 보내기 전에 `InvalidOperationException`
- `src/Biz.Bizadm.PowerManager.DeviceConfig/DeviceConfigRunner.cs`: `Commands`에 `ConnectAp`가 있고 `Settings.Protocol`이 V1인데 `MqttKey`가 비면 어댑터 선택·장치 AP 접속 전에 `InvalidOperationException`
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/TestSupport/MockDevice.cs`: 생성자에 선택 인자 `helloResponse`(기본 `START_OK` 한 프레임) 추가. 송신 정상 종료 뒤 연결 끝까지 받은 줄을 기록
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/Protocol/DeviceSettingsTests.cs`: V1 키·순서·마스킹, 기본 V3
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/Protocol/DeviceMessagesTests.cs`: `Delay`
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/Transport/DeviceClientTests.cs`: 흐름별 송신 줄 순서, V1·V4는 응답을 주지 않는 모의 장치와 응답을 주는 모의 장치 모두에서 실패 없이 끝남(응답은 읽어 비움), V2는 설정 응답 1개 뒤 `{"delay":"0"}`, V4 핸드셰이크 건너뛰기(0·2개 성공, 3개 실패), V1 `MqttKey` 없음 → 송신 없음, 로그·예외에 `pass`·`mqtt_key` 없음
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/DeviceConfigRunnerTests.cs`: V1 `MqttKey` 없음 → `ConnectOpenAsync` 호출 없이 실패
- `docs/features/device-config.md`: 요구 갱신(R3·R5·R6·R8·R9 바꿈, 흐름 요구 추가. R9는 응답을 기다리지 않는 흐름과 정상 종료 중 수신 비우기의 시간 초과는 실패가 아님을 반영)과 이력

MCP(`device-config-mcp`)
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/McpConfiguration.cs`: `SettingsSection`에 `Protocol`(문자열, 선택, 기본 `"V3"`)·`MqttKey`(문자열, 선택) 추가. `Validate`에서는 `Protocol` 값만 검사한다(V1인데 `MqttKey`가 없어도 기동은 막지 않는다. spec C3대로 ConnectAp 호출 때 `ResolveSettings`가 막아 다른 명령과 `settings.protocol` 덮어쓰기는 쓸 수 있게 둔다). `ToOptions(wifiProfiles, mqttKey)`로 바꿔 `DeviceSettings.Protocol`·`MqttKey`(복호화 값)를 채움
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Security/ConfigSecretProtector.cs`: `Settings.MqttKey`도 평문이면 암호화하고 복호화 값을 돌려줌(반환을 `ProtectedSecrets(WifiProfiles, MqttKey)`로). `HasSecrets`에 `MqttKey` 포함
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Program.cs`: 바뀐 반환값으로 `ToOptions` 호출
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/DeviceConfigService.cs`: `SettingsOverride.Protocol`(문자열) 추가. `ResolveSettings`가 `Protocol`을 인자 → 설정 파일 → `V3` 순으로 정하고 값이 틀리면 `ArgumentException`, `MqttKey`는 설정 파일 값만 쓰며 V1인데 없으면 `ArgumentException`(Wi-Fi 접속 전, R9 시점). 인자가 없을 때도(`o is null`) 설정 파일의 V1·`MqttKey`를 그대로 쓰며, 이때도 V1인데 `MqttKey`가 없으면 `ArgumentException`
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/DeviceConfigTools.cs`: `run_device_commands` 설명에 흐름 선택(`settings.protocol`, 기본 V3, V1은 설정 파일 `MqttKey` 필요) 추가
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/McpConfigurationTests.cs`: `Protocol` 기본·잘못된 값, V1인데 `MqttKey`가 없어도 기동 검증 통과
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/DeviceConfigServiceTests.cs`: `protocol` 덮어쓰기, 잘못된 값, V1 `MqttKey` 없음 → Wi-Fi 접속 없이 오류, `mqttKey` 인자는 모르는 키로 거부
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/HttpIntegrationTests.cs`: `tools/list`의 `settings` 속성 기대 목록에 `protocol` 추가
- `docs/mcp/device-config-mcp-사용.md`: 설정 예에 `Settings.Protocol`·`Settings.MqttKey` 설명
- `docs/features/device-config-mcp.md`: R6·R9 갱신과 이력

App(`device-config-app`)
- `src/Biz.Bizadm.PowerManager.DeviceConfig.App/AppConfiguration.cs`: `SettingsSection`에 `Protocol`·`MqttKey` 추가, `Validate`에서 값 검사·V1 `MqttKey` 필수
- `src/Biz.Bizadm.PowerManager.DeviceConfig.App/Security/ConfigSecretProtector.cs`: MCP와 같은 `MqttKey` 암호화·복호화
- `src/Biz.Bizadm.PowerManager.DeviceConfig.App/Program.cs`: `ProtectSecrets`가 `ProtectedSecrets(WifiProfiles, MqttKey)`(복호화 값)를 돌려주고, `CreateHostApplication`·`CreateOptions`가 이를 받아 `DeviceSettings.Protocol`·`MqttKey`(복호화 값)를 채움. YAML 모델의 `Settings.MqttKey`(기동 후 `DEKENC:` 암호문)는 직접 쓰지 않음
- `docs/features/device-config-app.md`(새 파일): App 기준 spec

## 결정

- YAML은 문자열, 라이브러리는 열거형: YamlDotNet의 열거형 변환은 대소문자를 가리지 않을 수 있어 C1(대소문자 구분)을 직접 검사하려면 문자열로 받아 `DeviceProtocols.TryParse`로 바꾼다 / 대안: YAML 열거형 바인딩 — 대소문자 규칙을 보장하기 어려워 고르지 않음
- `MqttKey`는 `DeviceSettings`에 복호화 값으로 둔다: 흐름 결정과 JSON 생성이 한곳(`DeviceSettings`)에 모인다. `record`라 `ToString`에 값이 나올 수 있으므로 `MqttKey`를 로그·예외에 넣지 않고 `DeviceSettings`를 로그로 찍지 않는다(지금도 찍지 않음) / 대안: `DeviceConfigOptions`에 따로 둠 — 흐름별 필수 검사가 흩어져 고르지 않음
- 응답을 기다리지 않는 흐름은 송신 종료 뒤 수신을 비우고 닫는다(`CloseGracefullyAsync`): 읽지 않은 수신 데이터가 남은 채 닫으면 Windows가 RST를 보내 마지막 송신이 장치에 닿지 않을 수 있다. `Shutdown(Send)`만으로는 그 뒤 남은·도착한 데이터 때문에 RST가 날 수 있으므로, 장치가 연결을 닫을(수신 0바이트) 때까지 읽어 비운다. V2·V4는 장치가 `delay`·설정·`connectap`에 응답할 수 있어 이 경우에 해당한다. 비우는 동안의 응답은 로그만 남기고, 시간 초과는 실패로 보지 않는다(참고 구현도 응답을 읽지 않는다) / 대안: 바로 `Dispose` 또는 `Shutdown(Send)` 뒤 바로 `Dispose` — 위 위험 때문에 고르지 않음. 참고 구현의 `DisconnectAsync(false)`와 같은 방식 — 같은 위험이 남아 고르지 않음
- V1 `MqttKey` 검사를 Runner에도 둔다: MCP·App은 Wi-Fi 접속 전에 검사하지만, 라이브러리를 직접 쓰는 경우에도 PC Wi-Fi를 바꾸기 전에 실패하도록 한다(C3) / 대안: MCP·App만 검사 — 라이브러리 단독 사용 시 Wi-Fi 전환 후 실패하므로 고르지 않음
- V4 건너뛰기 최대 2개: 참고 구현은 프레임 1개를 무조건 버리고 `BUTTON_`이면 1개 더 버린다(최대 2개). `BUTTON_…#`은 수신 처리에서 이미 빠지므로 2개면 충분하다

## 인터페이스·데이터

- `DeviceSettings.Protocol : DeviceProtocol`(기본 `V3`), `DeviceSettings.MqttKey : string?`
- `DeviceSettings.ToJson(string mac, string ssid, string pass, bool masked = false)` 시그니처 그대로. V1 출력 예: `{"server_addr":"…","server_port":"8883","ssl_support":"yes","ssid":"…","pass":"…","mqtt_key":"…","company":"DAWONDNS","model":"B540_W","topic":"dwd"}` + `\n`
- YAML(MCP·App 공통):
  ```yaml
  Settings:
    Protocol: V3        # 선택. V1|V2|V3|V4 (대소문자 구분, 기본 V3)
    MqttKey: plain      # V1일 때 필수. 기동 시 DEKENC:... 로 암호화해 다시 기록
  ```
- MCP `run_device_commands` `settings.protocol`(문자열, 선택). `mqttKey`는 인자에 없으므로 보내면 모르는 키 오류(R9)

## 오류·위험

- V1·V2·V4 대상 장비가 없어 실장비로 확인할 수 없다. 모의 장치로 송수신 순서만 확인한다(spec).
- V2의 설정 응답 형식을 모르므로 내용은 검사하지 않는다. 응답이 없으면 시간 초과로 실패한다.
- `MqttKey`를 암호화해 설정 파일에 다시 쓸 때 `Settings`가 없으면 아무것도 하지 않는다. 기존 설정 파일(`Protocol`·`MqttKey` 없음)은 V3로 지금과 같이 동작한다.
- MCP `ResolveSettings`가 `o is null`일 때 `file!`을 그대로 돌려주는 지금 경로에서도 V1 `MqttKey` 검사를 빼먹지 않게 한다(MCP는 기동 시 이 누락을 막지 않으므로 이 경로가 유일한 검사다).
- App·MCP 모두 복호화한 `MqttKey`는 `Protect`의 반환값으로만 옵션에 넣는다. YAML 모델의 값은 기동 후 암호문이라 장치로 보내면 안 된다.

## 검증 전략

- C1: `McpConfigurationTests`(기본 V3, `v3`·`X` 오류), App은 `Protocol: v3` 설정으로 App exe를 실행해 종료 코드 2를 확인(설정 검증은 KMS 처리 전에 끝난다)
- C2·C7: `DeviceSettingsTests`(V1 키·순서·문자열·마스킹), `DeviceClientTests`(V1 송신 줄: 핸드셰이크 → `apscan` → 설정 JSON, `connectap` 없음, 응답 없는 모의 장치에서 정상 종료, 로그·예외에 `pass`·`mqtt_key` 없음)
- C3: `DeviceClientTests`(V1 `MqttKey` 없음 → 설정 송신 없음), `DeviceConfigRunnerTests`(Wi-Fi 접속 전 실패), `DeviceConfigServiceTests`(V1 `MqttKey` 없음 → `ConnectOpenAsync` 호출 없음. 설정 파일 V1·인자 없음 경로와 인자 `protocol: V1` 경로 둘 다, `mqttKey` 인자 거부), `McpConfigurationTests`(V1인데 `MqttKey`가 없어도 기동 검증 통과), App exe(`Protocol: V1`·`MqttKey` 없음 → 종료 코드 2)
- C4: `DeviceClientTests`(V2: 설정 JSON → 응답 → `{"delay":"0"}`, 응답 내용이 OK가 아니어도 성공, 응답 없으면 `TimeoutException`)
- C5: 기존 `DeviceClientTests`·`DeviceSettingsTests`의 V3 검사가 그대로 통과
- C6: `DeviceClientTests`(V4: hello 응답 앞 프레임 0·1·2개 성공, 3개 실패, 설정 JSON → `connectap` 송신, 응답 없는 모의 장치에서 정상 종료)
- C8: `DeviceConfigServiceTests`(`protocol` 덮어쓰기와 잘못된 값), 기대 목록에 `protocol`을 더한 `HttpIntegrationTests`의 `tools/list` 검사로 `settings` 스키마에 `protocol`이 보이는지 확인
- C9: App `CreateOptions` 매핑은 빌드와 코드로 확인하고, App exe 설정 오류 시나리오(C1·C3)를 실행
- C10: `dotnet build Biz.Bizadm.PowerManager.slnx -warnaserror`, `dotnet test --solution Biz.Bizadm.PowerManager.slnx`
- C11: MCP 게시·배포(`FolderProfile`, exe·pdb만 교체, 기존 파일 백업) 후 사용자가 MCP를 시작하면 B540 실장비 1에 `ConnectAp`(설정 파일 값 그대로, `Protocol` 없음 → V3)를 보내고 서버 로그에 `POST /api/v1/devices/register/create`가 찍히는지 확인

## 원칙 점검

- 적용 규칙:
  - MCP `workflow/spec-driven.md`: 세 문서, 기준 spec, 네 검토
  - MCP `git/github-issue.md`: #13 참조, 종결 조건
  - MCP `dotnet/kms-config-secrets.md`(0001 결정에서 인용): 설정 파일 보안값은 OsKms로 암호화. `MqttKey`도 같은 방식
  - 전역 `language-preference.md`, `portable-python.md`(Python 쓰지 않음)
  - 메모리 `device-target-confirmation`: 상태 변경 명령 전 대상 SSID 확인
  - 저장소 `Directory.Build.props`: `Nullable`·`ImplicitUsings`
- 충돌: 없음
