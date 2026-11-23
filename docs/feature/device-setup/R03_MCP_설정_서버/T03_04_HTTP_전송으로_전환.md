---
출처: R03-R1, R03-R2, R03-R3, R03-R4, R03-R11
상태: 끝남
---

# T03_04 HTTP 전송으로 전환

## 읽을 것

- 요구사항: `R03-R1`~`R03-R4`, `R03-R11`
- 수용 기준: `R03-A1`~`R03-A4`, `R03-A7`, `R03-A11`
- 결정: 없음
- 계약: `C03-I1`
- 옛 작업 기록: `docs/specs/mcp-http-transport/`(지움. `15dc357`에 있음), 저장소 밖 작업 문서 `2026-11-18-mcp-http-transport.md`(기준점 `dd98be7`). 일감 #10, 커밋 `1517b24`

## 고칠 파일

- `Directory.Packages.props`: `ModelContextProtocol.AspNetCore` 2.4.0, `Microsoft.Extensions.Hosting.WindowsServices` 10.0.14
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.csproj`: `FrameworkReference Microsoft.AspNetCore.App`, 패키지 교체, `Microsoft.Extensions.Hosting` 참조 제거(NU1510)
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/McpConfiguration.cs`: `Http.Port` 필수·범위 검사
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Program.cs`: `WebApplication`, `ListenLocalhost(Http.Port)`, `AddWindowsService`, `WithHttpTransport`, `MapMcp("/mcp")`
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/StdioIntegrationTests.cs` → `HttpIntegrationTests.cs`, `McpConfigurationTests.cs`, 테스트 csproj 주석
- `docs/mcp/device-config-mcp-사용.md`(새 파일), `docs/features/device-config-mcp.md`(지움. `15dc357`에 있음)(새 파일)

## 할 일

### 단계 1: 패키지·설정
- 패키지 추가 전 `nuget_check_vulnerabilities`로 확인하고 `Http` 섹션을 더한다(`R03-R2`).

확인: Mcp 프로젝트 `dotnet build` 성공.

### 단계 2: HTTP 서버·서비스
- stdio 전송을 HTTP(Streamable, stateless)로 바꾸고 루프백에만 바인딩하며 Windows 서비스로도 돈다(`R03-R1`, `R03-R3`, `R03-R4`).

확인: `dotnet build` 경고 0, 콘솔 실행 뒤 `/mcp` 접속.

### 단계 3: 테스트
- `Http` 누락·`Port` 0·65536 예외, exe를 빈 포트로 띄워 `HttpClientTransport`로 접속하는 통합 테스트, `Http.Port` 누락 종료 코드 2.

확인: `dotnet test`(Mcp.Tests) 통과.

### 단계 4: 문서
- 사용 문서(설정 예, 콘솔 실행, `sc.exe` 등록과 실행 계정·절대 경로 주의, `claude mcp add --transport http`)와 옛 기준 spec(`R03-R11`).

확인: 두 문서를 읽어 세 방법과 기준 spec 틀을 확인.

### 단계 5: 검증
- 솔루션 빌드·테스트, `WithStdioServerTransport` 없음, 루프백 Listen, Ctrl+C 종료, 서비스 등록·시작·중지.

확인: 서비스 실행은 확인하지 못한 채 사용자 확인을 받아 #10을 종결했다(이슈 댓글에 기록).

## 위험

- 포트를 이미 쓰고 있으면 Kestrel 기동이 실패하고 종료 코드 3으로 끝난다.
- 테스트가 고른 빈 포트를 다른 프로세스가 먼저 잡을 수 있다(드물어 재시도 없음).
- 서비스 실행 계정이 Windows 로그인 계정이 아니면 OsKms 금고가 달라 Wi-Fi 프로필 복호화가 실패한다. 서비스에서 상대 경로는 `System32` 기준이 된다.

## 검증 방법

| 수용 기준 | 종류 | 대상 | 전제 | 순서 | 기대 결과 |
| --- | --- | --- | --- | --- | --- |
| `R03-A1` | 단위 테스트 | `HttpIntegrationTests` | 빈 포트 | `dotnet test --solution Biz.Bizadm.PowerManager.slnx`; `Program.cs`에서 `WithStdioServerTransport` 검색 | `tools/list` 4개·`tools/call` 성공, 검색 0건 |
| `R03-A2` | 단위 테스트 | `McpConfigurationTests`, `HttpIntegrationTests` | 없음 | 위 명령 | 누락·0·65536 예외, 누락 설정 exe 종료 코드 2 |
| `R03-A3` | 실행 | MCP exe 콘솔 실행 | 설정의 `Http.Port` | 실행 뒤 `Get-NetTCPConnection -State Listen -LocalPort <Port>` | LocalAddress가 `127.0.0.1`·`::1`뿐 |
| `R03-A4` | 실행 | MCP exe | 서비스: 관리자 권한, 실행 계정 비밀번호 | 콘솔 실행 뒤 Ctrl+C; `sc.exe create`·`start`·`query`·`stop`·`delete` | 종료 코드 0; 서비스 시작·중지(이 작업에서는 미확인) |
| `R03-A7` | 단위 테스트 | `HttpIntegrationTests`(schema·annotations·오류 처리 단언) | 없음 | 위 명령 | 기존 단언 모두 통과 |
| `R03-A11` | 문서 확인 | `docs/mcp/device-config-mcp-사용.md` | 없음 | 읽음 | 세 방법과 주의 사항이 있음 |

## 원칙 점검

- 적용 규칙: 그때의 MCP `workflow/spec-driven.md`, MCP `git/github-issue.md`, NuGet 취약성 확인, 전역 `language-preference.md`
- 충돌: 없음
