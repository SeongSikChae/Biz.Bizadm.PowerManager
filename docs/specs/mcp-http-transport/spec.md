# DeviceConfig.Mcp 전송 방식을 stdio에서 HTTP로 전환

기능: device-config-mcp(기준 spec 없음, 이번에 만듦)
이슈: GitHub #10

## 무엇을, 왜

`Biz.Bizadm.PowerManager.DeviceConfig.Mcp`는 stdio MCP 서버라서 MCP 클라이언트(Claude Code 등)가 세션마다 서버 프로세스를 하나씩 띄운다. 세션이 많아지면 프로세스도 그만큼 늘어난다.

전송 방식을 MCP Streamable HTTP로 바꿔, 서버 프로세스 하나를 여러 클라이언트가 URL로 접속해 함께 쓰게 한다. stdio 전송은 남기지 않는다.

- 같은 PC에서만 접속한다(루프백 바인딩). 인증은 두지 않는다.
- Windows 서비스로 실행할 수 있게 한다. 실행 계정은 사용자가 설치할 때 Windows 로그인 계정으로 지정한다. 콘솔 실행도 계속 지원한다.
- tool 4개의 이름·입력·출력·동작은 바꾸지 않는다.
- 이 변경은 `docs/mcp/0001-device-config-mcp-방안.md`와 `0002-device-config-mcp-설계.md`의 stdio 결정을 대체한다. 두 문서는 기록이므로 고치지 않고, 대체한 사실은 기준 spec에 적는다.

## 완료 조건

- C1. 서버가 MCP Streamable HTTP로 요청을 받는다. 클라이언트가 endpoint URL로 접속하면 `tools/list`로 tool 4개를 받고, `tools/call`이 동작한다. stdio 전송 코드는 남지 않는다.
- C2. 포트는 YAML 설정의 필수 값이다. 기본값은 없고, 값이 없으면 설정 오류로 기동하지 않는다.
- C3. 서버는 루프백 주소에만 바인딩한다.
- C4. 같은 exe를 Windows 서비스로 등록해 시작·중지할 수 있고, 콘솔에서 실행하면 Ctrl+C로 종료된다.
- C5. 기존 stdio 통합 테스트를 HTTP 전송으로 옮겨 통과한다(tool 4개의 schema·annotations·오류 처리가 그대로).
- C6. HTTP 실행, `sc.exe` 서비스 등록, `claude mcp add --transport http` 등록 방법이 문서에 있다.
- C7. `dotnet build` 경고 0, `dotnet test` 통과.
