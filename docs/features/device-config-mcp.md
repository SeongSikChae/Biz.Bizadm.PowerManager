# device-config-mcp

파일: src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.csproj, src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Program.cs, src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/ExitCode.cs, src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/McpConfiguration.cs, src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Security/ConfigSecretProtector.cs, src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/DeviceConfigTools.cs, src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/DeviceConfigService.cs, src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/ParameterDeviceConfigSelector.cs, tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests.csproj, tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/McpConfigurationTests.cs, tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/ParameterDeviceConfigSelectorTests.cs, tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/DeviceConfigServiceTests.cs, tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/HttpIntegrationTests.cs, tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/TestSupport/SyncProgress.cs, tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/TestSupport/GatedHostWifi.cs, docs/mcp/device-config-mcp-사용.md

B540 장치 설정(장치 AP 접속 → 명령 실행 → PC Wi-Fi 복구)을 MCP tool로 제공하는 서버. 상세 설계는 `docs/mcp/0001`~`0005`에 있으며, 전송 방식(stdio)과 단일 인스턴스 전제에 관한 0001·0002의 결정은 R1~R4가 대체한다(2026-11-18).

## 요구(완료 조건)

- R1. MCP Streamable HTTP로 요청을 받는다(endpoint `/mcp`, SDK 기본 stateless). stdio 전송은 없다. 서버 프로세스 하나를 여러 클라이언트가 함께 쓰며, PC Wi-Fi를 쓰는 tool은 프로세스 안의 세마포어로 하나씩 실행된다.
- R2. 포트는 YAML `Http.Port`(필수, 기본값 없음, 1~65535)로 정한다. 없거나 범위 밖이면 설정 오류(종료 코드 2).
- R3. 루프백(`127.0.0.1`, `::1`)에만 바인딩한다. 인증은 없다.
- R4. 같은 exe를 콘솔(Ctrl+C로 종료)과 Windows 서비스(SCM 시작·중지)로 실행한다. 서비스 실행 계정은 OsKms 자격 증명을 가진 Windows 로그인 계정이다.
- R5. 실행 인수는 `--config <yaml> --log <dir>`(절대 경로 가정)이며 종료 코드는 정상 0, 인수 오류 1, 설정 오류 2, 실행 오류 3이다.
- R6. YAML 설정은 `ContentRootPath`·`OsKms`·`Http` 필수, `Device` 기본값, `WifiProfiles`·`Settings` 선택이다. `WifiProfiles.Pass`는 기동 시 OsKms로 암호화해 설정 파일에 다시 쓴다.
- R7. tool 4개 `list_wifi_adapters`·`scan_device_aps`·`list_wifi_profiles`(readOnly), `run_device_commands`(destructive, openWorld)를 제공하며 모두 structuredContent·outputSchema를 채운다. 조회 실패는 원인 메시지가 있는 isError로 돌려준다.
- R8. `run_device_commands`는 명령·Wi-Fi·인자 오류를 `success: false`와 완료된 결과, `error`로 돌려주고, 시작과 명령 완료마다 진행 알림을 보고한다. 명령 이름은 `DeviceCommand` 이름과 정확히 일치해야 한다.
- R9. `ConnectAp`는 Wi-Fi 접속 전에 `WifiProfiles`·`wifiSsid`·Settings(설정 파일과 `settings` 인자를 필드 단위로 합친 값)를 검사한다. `settings`의 모르는 키는 오류로 돌려준다.
- R10. null일 수 있는 출력 필드(`currentSsid`, `error`)는 outputSchema의 required가 아니다.
- R11. 콘솔 실행, `sc.exe` 서비스 등록(실행 계정·절대 경로 주의), `claude mcp add --transport http` 등록 방법이 `docs/mcp/device-config-mcp-사용.md`에 있다.

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 | 작업 문서 |
|---|---|---|---|---|
| 2026-11-18 | mcp-http-transport | R1~R11 처음 작성. stdio 전송·stdout MCP 전용·command/args 등록·stdin 종료 정상 종료·단일 인스턴스 전제(0001·0002) 뺌, R1~R4·R11 추가, R2 포트 필수 | docs/specs/mcp-http-transport/ | 2026-11-18-mcp-http-transport.md |

표에 없는 이전 변경: 기록 없음(docs/mcp/0001~0005 참조. #7 최초 구현, #8 nullable 출력, #9 settings 인자)
