---
status: done
category: mcp
feature: device-config-mcp-nullable-output
phase: 개발
---

# device-config-mcp-nullable-output 개발

선행: [0002 설계](0002-device-config-mcp-설계.md) 5절 tool 표 · [0003 개발](0003-device-config-mcp-개발.md) · GitHub: SeongSikChae/Biz.Bizadm.PowerManager#8

단축(바로 구현): 버그 수정이라 방안·설계는 생략한다. tool 이름·인자·annotations와 반환 필드 이름은 바꾸지 않는다.

## 문제

Wi-Fi 미연결 어댑터가 있으면 `list_wifi_adapters`가 실패한다.

```
Structured content does not match the tool's output schema: data/adapters/0 must have required property 'currentSsid'
```

- `UseStructuredContent = true`라 SDK(ModelContextProtocol 2.4.0)가 반환 record로 출력 스키마를 만들며, 기본값 없는 생성자 매개변수 `string? CurrentSsid`를 `required`로 넣는다(추정, U1 테스트로 확인).
- SDK 직렬화는 null 속성을 생략하므로(추정) 값이 null이면 필드가 빠져 검증에 걸린다.
- 어댑터 목록은 연결할 어댑터를 고르려고 조회하므로 미연결 어댑터(`currentSsid` 없음)는 정상 케이스다.
- `RunResult.Error`도 같은 구조다. `run_device_commands` 성공 시 `error`가 null이라 같은 오류가 날 수 있다.

## 결정

- 두 필드를 **선택 필드**로 만든다. 값이 있으면 그대로 반환하고, null이면 생략돼도 스키마를 통과한다.
- 방법: positional record 매개변수에 기본값 `= null`을 준다. SDK 전역 직렬화 옵션은 바꾸지 않는다.
- 검토 시 SDK 2.4.0 실측: 기본값을 주면 `required`에서 빠지고 속성에 `"default": null`이 추가된다(무해). null이면 필드가 생략되고 스키마를 통과한다. 성공 시 `error`는 수정 전후 모두 생략된다(0002 예시의 `"error": null`과 다르나 동작 변화 없음, 이번 범위 아님).
- 다른 출력 record(`WifiNetwork`, `CommandResult`, `WifiProfilesResult`)에는 nullable 필드가 없다.

---

## U1. 출력 스키마에서 currentSsid·error를 선택 필드로

**대상**
- `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/DeviceConfigService.cs` `AdapterInfo`, `RunResult`
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests/StdioIntegrationTests.cs` (테스트 추가)

**코드 초안**

```csharp
// DeviceConfigService.cs
public sealed record AdapterInfo(string Id, string Name, string? CurrentSsid = null);
// ... existing ...
public sealed record RunResult(bool Success, IReadOnlyList<CommandResult> Results, string? Error = null);
```

호출부(`new AdapterInfo(a.Id, a.Name, ...)`, `new RunResult(true, results, null)`)는 위치 인자 그대로라 바꾸지 않는다.

```csharp
// StdioIntegrationTests.cs — 실제 Wi-Fi를 건드리지 않고 tools/list의 출력 스키마만 본다
private static string[] Required(JsonElement schema) =>
    schema.TryGetProperty("required", out JsonElement required)
        ? required.EnumerateArray().Select(e => e.GetString()!).ToArray()
        : [];

[TestMethod]
public async Task Stdio_OutputSchema_NullableFieldsAreOptional()
{
    await using McpClient client = await StartClientAsync();
    IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: TestContext.CancellationToken);

    JsonElement adapters = tools.Single(t => t.Name == "list_wifi_adapters").ProtocolTool.OutputSchema!.Value;
    JsonElement adapterItem = adapters.GetProperty("properties").GetProperty("adapters").GetProperty("items");
    Assert.IsTrue(adapterItem.GetProperty("properties").TryGetProperty("currentSsid", out _)); // 필드 이름 유지
    CollectionAssert.DoesNotContain(Required(adapterItem), "currentSsid");
    CollectionAssert.IsSubsetOf(new[] { "id", "name" }, Required(adapterItem));

    JsonElement run = tools.Single(t => t.Name == "run_device_commands").ProtocolTool.OutputSchema!.Value;
    Assert.IsTrue(run.GetProperty("properties").TryGetProperty("error", out _));
    CollectionAssert.DoesNotContain(Required(run), "error");
    CollectionAssert.IsSubsetOf(new[] { "success", "results" }, Required(run));
}
```

기존 `Required(Tool)`(입력 스키마용)은 그대로 두고 위 오버로드만 추가한다. 재현은 실제 Wi-Fi 응답 대신 이 스키마 required 검증으로 확인한다.

**검증**
1. 수정 전: 새 테스트가 `currentSsid`/`error` required로 실패하는지 확인(재현).
2. 수정 후: 새 테스트 통과, 기존 테스트(`ListAdapters_ReturnsCurrentSsid`, `Stdio_*`, `Run_*`) 회귀 없음.
3. 공통 명령:

```powershell
dotnet build Biz.Bizadm.PowerManager.slnx -warnaserror
dotnet test --solution Biz.Bizadm.PowerManager.slnx
```

4. 실제 서버: 재빌드한 MCP 서버로 미연결 어댑터 환경에서 `list_wifi_adapters`가 오류 없이 반환되는지 확인(MCP 클라이언트 재시작 필요).

**선행 단위**: 없음
