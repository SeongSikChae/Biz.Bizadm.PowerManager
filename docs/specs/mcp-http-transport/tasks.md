# DeviceConfig.Mcp 전송 방식을 stdio에서 HTTP로 전환 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 패키지·설정

- [x] T001 `ModelContextProtocol.AspNetCore` 2.4.0, `Microsoft.Extensions.Hosting.WindowsServices` 10.0.14 버전 추가. 추가 전 `nuget_check_vulnerabilities`로 두 패키지 확인 (Directory.Packages.props) — C1, C4
- [x] T002 `FrameworkReference Microsoft.AspNetCore.App` 추가, `ModelContextProtocol` → `ModelContextProtocol.AspNetCore`, `Microsoft.Extensions.Hosting.WindowsServices` 추가, `Microsoft.Extensions.Hosting` 참조 제거 (src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.csproj) — C1, C4, C7
- [x] T003 [P] `HttpSection`(`Port` 필수) 추가, `Validate`에서 `Http` 누락·`Port` 범위(1~65535) 검사 (src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/McpConfiguration.cs) — C2

체크포인트: Mcp 프로젝트 `dotnet build` 성공(Program.cs는 아직 stdio라도 빌드됨)

## 단계 2: HTTP 서버·서비스

- [x] T004 `WebApplication.CreateBuilder`(Args·ContentRootPath), `ConfigureKestrel(ListenLocalhost(Http.Port))`, `AddWindowsService()`, `AddMcpServer().WithHttpTransport().WithTools<DeviceConfigTools>()`, `app.MapMcp("/mcp")`. 기존 Serilog·서비스 등록·종료 코드 경로 유지, stdio 전제 주석 정리 (src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Program.cs) — C1, C3, C4

체크포인트: `dotnet build` 경고 0. 콘솔 실행 후 `/mcp`에 접속 가능

## 단계 3: 테스트

- [x] T005 [P] `Http` 누락, `Port` 0·65536 → 예외 테스트 추가. 기존 테스트의 설정에 `Http.Port`가 필요하면 채움 (tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/McpConfigurationTests.cs) — C2
- [x] T006 `StdioIntegrationTests.cs`를 `HttpIntegrationTests.cs`로 이름 변경. 빈 포트를 골라 설정에 `Http.Port`를 넣고 exe를 띄운 뒤 `/mcp` 접속까지 재시도, `HttpClientTransport`로 접속, 테스트 후 프로세스 종료. 기존 tool 단언 유지, `Exe_InvalidConfig_ExitCode2`의 stdout 단언 제거, `Http.Port` 누락 → 종료 코드 2 테스트 추가 (tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/HttpIntegrationTests.cs) — C1, C2, C5
- [x] T007 [P] "stdio 통합 테스트" 주석을 "HTTP 통합 테스트"로 고침 (tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests.csproj) — C5

체크포인트: `dotnet test`(Mcp.Tests) 통과

## 단계 4: 문서·기준 spec

- [x] T008 [P] 사용 문서 작성: YAML 설정 예(`Http.Port`), 콘솔 실행, `sc.exe create`(실행 계정 `obj=`·`password=` 지정, 실행 계정이 Windows 로그인 계정과 다르면 OsKms 금고가 달라져 Wi-Fi 프로필 복호화가 실패한다는 주의, 절대 경로 주의)·`start`·`stop`·`delete`, `claude mcp add --transport http device-config http://127.0.0.1:<Port>/mcp` (docs/mcp/device-config-mcp-사용.md) — C6
- [x] T009 기준 spec 작성: `파일:` 줄, 지금 유효한 요구 R번호(이전 docs/mcp 0001~0005와 이번 C1~C7 반영, 0001·0002의 stdio 결정 대체 사실), 이력(이번 작업 한 줄, 이전 변경은 docs/mcp 문서 참조) (docs/features/device-config-mcp.md) — C1~C7

체크포인트: 두 문서를 읽어 C6 항목과 기준 spec 틀을 확인

## 단계 5: 검증

- [ ] T010 솔루션 `dotnet build`(경고 0)·`dotnet test` 실행, `Program.cs`에 `WithStdioServerTransport` 없음 확인, 콘솔 실행해 `Get-NetTCPConnection`으로 루프백 Listen 확인·Ctrl+C 종료 확인, 서비스 등록·시작·중지 확인(관리자 권한·계정 비밀번호가 필요해 사용자가 실행하거나 승인 뒤 실행) — C1, C3, C4, C7

## 의존 관계

- T001 → T002 → T004: 패키지 버전과 참조가 있어야 Program.cs가 빌드된다.
- T003은 T002와 다른 파일이라 동시에 할 수 있으나 T004·T005·T006보다 먼저 끝나야 한다(`Http.Port` 사용).
- T005·T006은 T003·T004 뒤. T007은 아무 때나.
- T008은 T004의 설정 키·경로가 정해진 뒤면 언제든. T009는 마지막 구현 단계(구현 내용 확정 후).
- T010은 모든 작업 뒤.
