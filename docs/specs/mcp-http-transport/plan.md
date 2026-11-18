# DeviceConfig.Mcp 전송 방식을 stdio에서 HTTP로 전환 계획

## 요약

Mcp 실행 프로그램의 Host를 `Host.CreateApplicationBuilder`(stdio)에서 `WebApplication`(Kestrel)으로 바꾸고, MCP 서버 전송을 `WithStdioServerTransport()`에서 `WithHttpTransport()` + `MapMcp("/mcp")`로 바꾼다. Kestrel은 YAML의 `Http.Port`(필수)로 루프백에만 바인딩한다. `AddWindowsService()`를 더해 같은 exe를 Windows 서비스로도 실행한다. tool 코드(`DeviceConfigTools`, `DeviceConfigService`)는 바꾸지 않는다. stdio 통합 테스트는 exe를 띄우고 HTTP 클라이언트로 접속하는 테스트로 옮긴다.

## 바꿀 파일·구조

- `Directory.Packages.props`: `ModelContextProtocol.AspNetCore` 2.4.0, `Microsoft.Extensions.Hosting.WindowsServices` 10.0.14 버전 추가
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.csproj`: `FrameworkReference Microsoft.AspNetCore.App` 추가, `ModelContextProtocol` → `ModelContextProtocol.AspNetCore`, `Microsoft.Extensions.Hosting.WindowsServices` 추가, `Microsoft.Extensions.Hosting` PackageReference 제거(공유 프레임워크가 제공하므로 남기면 NU1510 경고. `Directory.Packages.props`의 버전 항목은 다른 프로젝트가 쓰므로 유지)
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/McpConfiguration.cs`: `Http` 섹션(`Port` 필수) 추가와 검사
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Program.cs`: WebApplication·Kestrel 루프백 바인딩·HTTP 전송·`MapMcp`·`AddWindowsService`, stdio 전제 주석 정리
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/StdioIntegrationTests.cs` → `HttpIntegrationTests.cs`(이름 변경): exe를 HTTP 서버로 띄워 `HttpClientTransport`로 접속, stdout 단언 제거, `Http.Port` 누락 시 종료 코드 2 테스트 추가
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/McpConfigurationTests.cs`: `Http.Port` 누락·범위 밖 검사 테스트 추가
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests.csproj`: "stdio 통합" 주석만 "HTTP 통합"으로 고침
- `docs/mcp/device-config-mcp-사용.md`(새 파일): 설정 예, 콘솔 실행, `sc.exe` 서비스 등록(실행 계정 지정), `claude mcp add --transport http` 등록
- `docs/features/device-config-mcp.md`(새 파일): 기준 spec

## 결정

- Host는 `WebApplication.CreateBuilder` + `FrameworkReference Microsoft.AspNetCore.App`: MCP HTTP 전송(`MapMcp`)이 ASP.NET Core 라우팅을 쓴다. / 검토한 대안: SDK를 `Microsoft.NET.Sdk.Web`으로 바꾸기 — appsettings·launchSettings 등 기본 동작이 더 바뀌고, OS별 Runtime 참조 조건이 있는 csproj를 크게 손대야 해서 고르지 않음.
- 바인딩은 `ConfigureKestrel(o => o.ListenLocalhost(port))`: `127.0.0.1`과 `::1`에만 바인딩한다(C3). 코드로 정한 Listen이 `ASPNETCORE_URLS` 등 다른 URL 설정보다 우선한다. / 대안: `UseUrls("http://127.0.0.1:port")` — IPv6 루프백이 빠지고 환경 변수와 겹칠 수 있어 고르지 않음.
- endpoint 경로는 `/mcp`: MCP 클라이언트 등록 URL은 `http://127.0.0.1:<Port>/mcp`.
- HTTP 전송 옵션은 SDK 기본값(2.4.0에서 `Stateless = true`, 세션 없음)을 그대로 쓴다: tool 4개는 서버→클라이언트 요청(sampling·elicitation·roots)을 쓰지 않고, 진행 알림은 그 요청의 응답 스트림으로 전달된다. 세션이 없으므로 서비스가 재시작돼도 클라이언트 세션이 끊기지 않는다. / 검토한 대안: `Stateless = false`(stateful) — `2026-07-28` 이후 개정 클라이언트가 `-32022`로 거절된 뒤 프로토콜을 낮춰 다시 접속해야 하고, 얻는 기능이 없어 고르지 않음.
- 서비스 실행은 `builder.Services.AddWindowsService()`: 서비스로 시작되면 SCM 수명을 쓰고, 콘솔에서는 아무 일도 하지 않아 하나의 exe로 둘 다 된다(C4). 서비스 이름·계정은 `sc.exe` 등록 때 사용자가 정한다.
- 포트 설정은 `Http.Port`(필수, 기본값 없음, 1~65535): spec C2. 없거나 범위 밖이면 기존 설정 오류 경로(종료 코드 2).
- 통합 테스트는 지금처럼 Mcp 출력 폴더의 exe를 자식 프로세스로 띄운다: 테스트가 빈 TCP 포트를 하나 골라 설정에 넣고, `/mcp`에 접속될 때까지 짧게 재시도한 뒤 `HttpClientTransport`로 접속한다. 테스트가 끝나면 프로세스를 종료한다. / 대안: `WebApplicationFactory`·TestServer로 메모리 안에서 띄우기 — `Program`이 YAML·KMS·로깅을 직접 구성하는 구조라 손이 많이 가서 고르지 않음.

## 인터페이스·데이터

YAML 설정에 추가하는 섹션:

```yaml
Http:
  Port: 18460   # 필수, 1~65535
```

MCP endpoint: `http://127.0.0.1:<Port>/mcp` (또는 `http://localhost:<Port>/mcp`)

명령줄 인수(`--config`, `--log`)와 종료 코드(0/1/2/3)는 바꾸지 않는다.

## 오류·위험

- 포트를 이미 쓰고 있으면 Kestrel 기동이 실패한다. 기존 `host.StartAsync` 예외 경로로 로그를 남기고 종료 코드 3으로 끝난다.
- 테스트에서 빈 포트를 고른 뒤 서버가 바인딩하기 전에 다른 프로세스가 그 포트를 잡을 수 있다. 드문 경우라 재시도는 두지 않는다.
- 서비스 실행 계정이 Windows 로그인 계정이 아니면 OsKms 자격 증명 금고가 달라져 Wi-Fi 프로필 복호화가 실패할 수 있다. 사용 문서에 적는다.
- 서비스로 실행할 때 상대 경로는 작업 폴더가 `System32`가 되므로 `--config`·`--log`·`ContentRootPath`는 절대 경로를 써야 한다(기존 가정과 같음). 사용 문서에 적는다.

## 검증 전략

- C1: `HttpIntegrationTests`가 exe를 띄워 `tools/list`(tool 4개)·`tools/call`(`list_wifi_profiles`)을 확인한다. `Program.cs`에 `WithStdioServerTransport`가 없음을 grep으로 확인한다.
- C2: `McpConfigurationTests`(누락·범위 밖 → 예외), `HttpIntegrationTests`의 `Http.Port` 누락 설정 → 종료 코드 2.
- C3: 서버를 콘솔로 띄운 뒤 `Get-NetTCPConnection -State Listen -LocalPort <Port>`의 LocalAddress가 `127.0.0.1`과 `::1`뿐인지 확인한다.
- C4: 콘솔로 실행한 뒤 Ctrl+C를 눌러 정상 종료(종료 코드 0)되는지 직접 확인한다. 서비스는 `sc.exe create`·`start`·`query`·`stop`·`delete`로 확인한다(관리자 권한과 실행 계정 비밀번호가 필요하므로 사용자가 실행하거나 사용자 승인 뒤 실행).
- C5: 옮긴 `HttpIntegrationTests`의 기존 단언(schema·annotations·오류 처리)이 모두 통과한다.
- C6: 사용 문서에 세 방법(콘솔 실행, `sc.exe` 등록·계정 지정, `claude mcp add --transport http`)이 있는지 읽어 확인한다.
- C7: `dotnet build` 경고 0, `dotnet test` 통과.

## 원칙 점검

- 적용 규칙: 전역 언어·스펙 주도(`workflow/spec-driven.md`)·GitHub 이슈(`git/github-issue.md`), `dotnet/nuget-package-selection.md`(추가 패키지는 TFM 호환·최신 패치·비취약 버전 확인), `dotnet/console-app-template.md`(--config/--log, YAML 검증, Serilog Console+일별 File, 종료 코드 0/1/2/3), `dotnet/rid-specific-runtime.md`(OS별 Runtime 참조 구조 유지), `git/commit-message.md`
- 충돌: 없음. Host가 WebApplication으로 바뀌어도 --config/--log, YAML 검증, Serilog 구성, 종료 코드 규칙은 그대로 둔다(`Microsoft.Extensions.Hosting` 직접 참조만 공유 프레임워크로 대체). 추가 패키지 두 개는 구현 시 `nuget_check_vulnerabilities`로 확인하고, 문제가 있으면 설치 전에 묻는다.
