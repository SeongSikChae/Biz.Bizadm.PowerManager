# R03 MCP 설정 서버

## 목적

사용자가 에이전트(MCP 클라이언트)를 통해 B540 장치 설정(장치 AP 접속 → 명령 실행 → PC Wi-Fi 복구)을 하려 한다. 서버는 장치 설정 라이브러리(`R01`)를 MCP tool로 내주고, PC 한 대의 Wi-Fi를 여러 클라이언트가 함께 쓰도록 실행을 하나씩 줄 세운다.

## 요구사항

| ID | 요구사항 | 상태 | 일감 |
| --- | --- | --- | --- |
| `R03-R1` | 서버는 MCP Streamable HTTP로 요청을 받는다(endpoint `/mcp`, SDK 기본 stateless). stdio 전송은 없다. 서버 프로세스 하나를 여러 클라이언트가 함께 쓰며, PC Wi-Fi를 쓰는 tool은 프로세스 안의 세마포어로 하나씩 실행한다. | 반영 | |
| `R03-R2` | 서버는 포트를 YAML `Http.Port`(필수, 기본값 없음, 1~65535)로 정한다. 없거나 범위 밖이면 설정 오류(종료 코드 2)로 끝난다. | 반영 | |
| `R03-R3` | 서버는 루프백(`127.0.0.1`, `::1`)에만 바인딩한다. 인증은 없다. | 반영 | |
| `R03-R4` | 같은 exe는 콘솔(Ctrl+C로 종료)과 Windows 서비스(SCM 시작·중지)로 실행된다. 서비스 실행 계정은 OsKms 자격 증명을 가진 Windows 로그인 계정이다. | 반영 | |
| `R03-R5` | 서버는 실행 인수 `--config <yaml> --log <dir>`(절대 경로 가정)을 받고, 종료 코드를 정상 0, 인수 오류 1, 설정 오류 2, 실행 오류 3으로 돌려준다. | 반영 | |
| `R03-R6` | 서버는 YAML 설정에서 `ContentRootPath`·`OsKms`·`Http`를 필수로, `Device`를 기본값으로, `WifiProfiles`·`Settings`를 선택으로 읽는다. 기동하면 `WifiProfiles.Pass`와 `Settings.MqttKey`를 OsKms로 암호화해 설정 파일에 다시 쓰고, 복호화한 값만 실행에 쓴다. `Settings.Protocol`(`V1`·`V2`·`V3`·`V4`, 대소문자 구분, 기본 `V3`)이 그 밖의 값이면 설정 오류로 끝난다. V1인데 `MqttKey`가 없어도 기동은 막지 않는다(`R03-R9`에서 막는다). | 반영 | |
| `R03-R7` | 서버는 tool 4개 `list_wifi_adapters`·`scan_device_aps`·`list_wifi_profiles`(readOnly), `run_device_commands`(destructive, openWorld)를 제공하며 모두 structuredContent·outputSchema를 채운다. 조회가 실패하면 원인 메시지가 있는 isError로 돌려준다. | 반영 | |
| `R03-R8` | `run_device_commands`는 명령·Wi-Fi·인자 오류이면 `success: false`와 완료된 결과, `error`를 돌려주고, 시작과 명령 완료마다 진행 알림을 보고한다. 명령 이름이 `DeviceCommand` 이름과 정확히 일치하지 않으면 오류로 돌려준다. | 반영 | |
| `R03-R9` | `ConnectAp`가 있으면 `run_device_commands`는 Wi-Fi 접속 전에 `WifiProfiles`·`wifiSsid`·Settings(설정 파일과 `settings` 인자를 필드 단위로 합친 값)를 검사한다. `settings`의 모르는 키는 오류로 돌려준다. `settings.protocol`(문자열)은 흐름을 덮어쓰며 그 밖의 값은 오류다. `MqttKey`는 인자로 받지 않고(모르는 키) 설정 파일 값만 쓰며, 흐름이 V1인데 없으면 오류로 돌려준다. | 반영 | |
| `R03-R10` | 서버는 null일 수 있는 출력 필드(`currentSsid`, `error`)를 outputSchema의 required에 넣지 않는다. | 반영 | |
| `R03-R11` | 콘솔 실행, `sc.exe` 서비스 등록(실행 계정·절대 경로 주의), `claude mcp add --transport http` 등록 방법이 `docs/mcp/device-config-mcp-사용.md`에 있다. | 반영 | |

## 수용 기준

| ID | 대상 | 기준 | 최신 검증 |
| --- | --- | --- | --- |
| `R03-A1` | `R03-R1` | 실행 중인 서버의 `/mcp`에 HTTP MCP 클라이언트 둘이 붙어 tool 목록을 받고, 둘이 동시에 `run_device_commands`를 부르면 두 번째는 첫 번째가 끝난 뒤에 PC Wi-Fi를 쓴다. | `V03_04` |
| `R03-A2` | `R03-R2` | `Http.Port`가 없거나 0·65536이면 종료 코드 2로 끝나고, 1~65535이면 그 포트로 연다. | `V03_04` |
| `R03-A3` | `R03-R3` | 서버가 루프백 주소에서만 수신하고, 인증 헤더 없이 tool을 부를 수 있다. | `V03_04` |
| `R03-A4` | `R03-R4` | 콘솔에서 실행해 Ctrl+C로 끝나고, 같은 exe를 서비스로 등록해 SCM으로 시작·중지할 수 있다. | |
| `R03-A5` | `R03-R5` | 인수가 빠지면 1, 설정이 틀리면 2로 끝나고, 정상 종료하면 0이다. | |
| `R03-A6` | `R03-R6` | `Http`가 없으면 종료 코드 2, `Settings.Protocol`이 모르는 값이면 종료 코드 2로 끝나고, V1이면서 `MqttKey`가 없어도 기동한다. 평문 `Pass`·`MqttKey`는 기동 뒤 설정 파일에서 암호문으로 바뀐다. | `V01_03` |
| `R03-A7` | `R03-R7` | tool 목록에 네 tool이 요구의 annotation으로 있고, 각 호출 결과에 outputSchema에 맞는 structuredContent가 있으며, 조회가 실패하면 원인 메시지와 함께 isError가 참이다. | `V03_04` |
| `R03-A8` | `R03-R8` | 없는 명령 이름(대소문자만 다른 이름 포함)이나 틀린 인자로 부르면 `success: false`, 그때까지 끝난 결과, `error`가 돌아오고, 정상 호출에서는 시작과 명령마다 진행 알림이 온다. | `V03_01` |
| `R03-A9` | `R03-R9` | `ConnectAp`에서 `settings`에 모르는 키나 `mqttKey`를 넣거나, `settings.protocol`이 모르는 값이거나, V1인데 설정 파일에 `MqttKey`가 없으면 PC Wi-Fi를 바꾸지 않고 오류가 돌아온다. `settings`로 준 필드는 설정 파일 값을 필드 단위로 덮어쓴다. | `V01_03` |
| `R03-A10` | `R03-R10` | 각 tool의 outputSchema `required`에 `currentSsid`·`error`가 없고, 두 값이 null인 결과가 schema 검증을 통과한다. | |
| `R03-A11` | `R03-R11` | `docs/mcp/device-config-mcp-사용.md`에 콘솔 실행, `sc.exe` 등록(실행 계정·절대 경로 주의 포함), `claude mcp add --transport http` 등록이 있다. | `V03_04` |

## 지금 동작

- tool 이름과 입력·출력은 `C03-I2`~`C03-I5`에 있다. 처음 설계(일감 #7~#9)는 stdio 전송과 단일 인스턴스를 전제했으나 `R03-R1`~`R03-R4`가 대신한다.

## 파일

- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.csproj`
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Program.cs`
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/ExitCode.cs`
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/McpConfiguration.cs`
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Security/ConfigSecretProtector.cs`
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/DeviceConfigTools.cs`
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/DeviceConfigService.cs`
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/ParameterDeviceConfigSelector.cs`
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests.csproj`
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/McpConfigurationTests.cs`
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/ParameterDeviceConfigSelectorTests.cs`
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/DeviceConfigServiceTests.cs`
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/HttpIntegrationTests.cs`
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/TestSupport/SyncProgress.cs`
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/TestSupport/GatedHostWifi.cs`
- `docs/mcp/device-config-mcp-사용.md`

## 의존

- `R01`의 실행 흐름, 명령 이름(`DeviceCommand`), `DeviceSettings`(`R01-R1`, `R01-R4`, `R01-R10`)

## 범위 밖

- 인증, 루프백 밖 바인딩

## 기존 문서

- 없음

## 번호 대장

| 번호 | 제목 | 출처 | 상태 |
| --- | --- | --- | --- |
| `D03_01` | settings 인자로 필드 단위 덮어쓰기 | `docs/mcp/0005`(지움. `15dc357`에 있음)의 사용자 합의, `T03_03` | 유효 |
| `T03_01` | MCP 설정 서버 최초 구현 | `R03-R5`~`R03-R9`(#7) | 끝남 |
| `T03_02` | null 출력 필드를 선택으로 | `R03-R10`(#8) | 끝남 |
| `T03_03` | settings 인자로 설정 덮어쓰기 | `R03-R9`(#9) | 끝남 |
| `T03_04` | HTTP 전송으로 전환 | `R03-R1`~`R03-R4`, `R03-R11`(#10) | 끝남 |
