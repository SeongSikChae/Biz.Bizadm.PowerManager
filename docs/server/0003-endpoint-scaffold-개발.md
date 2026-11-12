---
status: done
category: server
feature: endpoint-scaffold
phase: 개발
---

# endpoint-scaffold 개발 계획

선행: [0001 방안](0001-endpoint-scaffold-방안.md), [0002 설계](0002-endpoint-scaffold-설계.md)

`{Solution}` = `Biz.Bizadm.PowerManager`. 스킬 `dotnet-server-api-template`·`dotnet-server-spa-template`·`react-project-create`의 파일 내용을 기본으로 하고, 아래는 **템플릿과 다른 부분과 새 코드**만 적는다.

## 패키지

- `MQTTnet` 5.2.0.1603, `MQTTnet.Server` 5.2.0.1603(2026-11-10 `dotnet package search` 최신).
- `Microsoft.AspNetCore.SpaProxy`, `Microsoft.AspNetCore.OpenApi`, `Microsoft.AspNetCore.TestHost` 10.0.15(2026-11-12 `dotnet package search` 10.0.x 최신 패치).
- 기존 `Microsoft.Extensions.Hosting`·`Logging.Abstractions` 10.0.14는 이 작업 범위가 아니므로 바꾸지 않는다.
- U1 시작 시 `dotnet package search <id> --exact-match`로 버전을 다시 확인하고 취약·Deprecated 여부를 본다. 안전한 후보가 없으면 설치 전에 사용자에게 묻는다(규칙 `dotnet/nuget-package-selection.md`). 설치 후에는 프로젝트를 만들거나 패키지 참조를 추가하는 단위(U2·U3·U4는 프로젝트 생성, U6은 `Server.Spa.Tests`에 `MQTTnet` 추가)의 검증에서 프로젝트별로 한 번 더 확인한다.

## U1. 루트 파일 (기존 파일 병합)

- 대상: 기존 `Biz.Bizadm.PowerManager.slnx`(DeviceConfig 프로젝트 8개 등록), 기존 `Directory.Build.props`, 기존 `Directory.Packages.props`. 템플릿 파일로 덮어쓰지 않고 필요한 항목만 추가한다. 프로젝트 등록은 각 단위에서 `dotnet sln add`로 한다(기존 `/src/`·`/tests/` 폴더).
- 코드 초안: 기존 `Directory.Packages.props`의 `ItemGroup`에 없는 항목만 추가

```xml
<!-- ... existing PackageVersion (KMS, CommandLineParser, Serilog, YamlDotNet, MSTest 등) ... -->
<PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.15" />
<PackageVersion Include="Microsoft.AspNetCore.SpaProxy" Version="10.0.15" />
<PackageVersion Include="Microsoft.AspNetCore.TestHost" Version="10.0.15" />
<PackageVersion Include="MQTTnet" Version="5.2.0.1603" />
<PackageVersion Include="MQTTnet.Server" Version="5.2.0.1603" />
```

- 테스트 프로젝트 공통(U2·U3·U4의 `*.Tests.csproj`): 기존 테스트와 같은 MTP 속성

```xml
<PropertyGroup>
  <IsPackable>false</IsPackable>
  <OutputType>Exe</OutputType>
  <EnableMSTestRunner>true</EnableMSTestRunner>
</PropertyGroup>
```

- 검증: 기존 DeviceConfig 프로젝트가 `.slnx`에 그대로 남아 있음. `Directory.Packages.props`에 추가 항목 5개. (`dotnet package list --vulnerable --include-transitive`·`--deprecated` 0건 확인은 프로젝트 생성·패키지 참조 추가가 있는 U2·U3·U4·U6 검증에서 프로젝트별로 실행)
- 선행: 없음

## U2. Server.Core (+ Server.Core.Tests)

- 대상: `src/Biz.Bizadm.PowerManager.Server.Core/*` 템플릿 그대로. `ServerKestrelExtensions.ConfigureServerKestrel`을 멀티 포트로 바꾸고, 인증서 로드를 새 `TlsCertificateLoader`로 추출한다(설계 §1 예외, §3). 프로젝트 생성 후 솔루션에 등록한다(U3·U4도 각 프로젝트와 Tests를 같은 방식으로 등록):

```powershell
dotnet sln Biz.Bizadm.PowerManager.slnx add src/Biz.Bizadm.PowerManager.Server.Core/Biz.Bizadm.PowerManager.Server.Core.csproj tests/Biz.Bizadm.PowerManager.Server.Core.Tests/Biz.Bizadm.PowerManager.Server.Core.Tests.csproj
```

- 코드 초안:

```csharp
public static class TlsCertificateLoader
{
    /// Kestrel(ConfigureHttps의 체인 로드 포함)과 MQTTS가 같은 경로 계산을 쓴다. 템플릿과 같이 절대 경로.
    public static string ResolvePath(TlsConfiguration tls, string contentRootPath)
        => new FileInfo(Path.Combine(contentRootPath, tls.ServerCertificate!)).FullName;

    /// MQTTS용. 패스워드는 KMS로 복호화된 설정 값을 호출 시에만 읽고 보관·로그하지 않는다. 호출자가 Dispose한다.
    public static X509Certificate2 Load(TlsConfiguration tls, string contentRootPath)
        => X509CertificateLoader.LoadPkcs12FromFile(ResolvePath(tls, contentRootPath), tls.ServerCertificatePassword);
}
```

```csharp
public static IWebHostBuilder ConfigureServerKestrel(this IWebHostBuilder webHost,
    IReadOnlyCollection<ushort> httpPorts, IReadOnlyCollection<ushort> httpsPorts, TlsConfiguration? tls, string contentRootPath)
    => webHost.ConfigureKestrel(options =>
    {
        foreach (ushort port in httpPorts)
            options.ListenAnyIP(port, listen => listen.Protocols = HttpProtocols.Http1);

        if (tls is null || tls.UseTls != true)
            return;

        // ... existing: TLS 분기 안에서 options.ApplicationServices로 logger 생성 ...
        string certificatePath = TlsCertificateLoader.ResolvePath(tls, contentRootPath);
        foreach (ushort port in httpsPorts)
            options.ListenAnyIP(port, listen => ConfigureHttps(listen, tls, certificatePath, logger));
    });

// 템플릿 ListenAnyIP 안의 HTTPS 인라인 람다를 그대로 옮긴 것(내용 무변경: 인증서·체인·cipher)
private static void ConfigureHttps(ListenOptions listen, TlsConfiguration tls, string certificatePath, ILogger logger)
{
    // ... existing lambda body ...
}
```

- 검증: `dotnet build`, `dotnet test --project tests/Biz.Bizadm.PowerManager.Server.Core.Tests` 통과(MTP 모드라 경로는 `--project`로 넘긴다. 템플릿 테스트 표 + `KestrelTlsTests`는 `httpPorts: []`, `httpsPorts`에 TCP·UDP(`[::]` 듀얼 모드) 모두 빈 포트 1개로 호출 — TLS 분기가 HTTP/3(QUIC)을 유지하므로. 추가 케이스: `httpPorts`에 빈 TCP 포트 1개 + TLS 사용으로 띄워 그 포트에 평문 HTTP 요청이 응답하는지 확인(설계 §1 "TLS 시 HTTP 리스너 유지" 예외) + `TlsCertificateLoader.ResolvePath`·`Load` 임시 PFX)
- 선행: U1

## U3. Core 도메인 (+ Core.Tests)

- 대상: `src/Biz.Bizadm.PowerManager.Core/` — `Devices/DeviceRegisterRequest.cs`, `Mqtt/MqttEnvelope.cs`, `Mqtt/MqttOperation.cs`, `Mqtt/DeviceTopic.cs`, `Mqtt/DeviceTopicKind.cs`, `Mqtt/IDeviceMessageHandler.cs`, `Mqtt/IDeviceCommandPublisher.cs`. ASP.NET Core·MQTTnet 참조 없음(System.Text.Json만).
- 코드 초안:

```csharp
// 장치 본문(b540-rest-api.md §2)은 snake_case이고 값이 모두 문자열("verify":"true" 포함).
// 이름을 고정하지 않으면 ASP.NET Core 기본 camelCase로 매핑돼 값이 조용히 null이 된다.
public sealed record DeviceRegisterRequest(
    [property: JsonPropertyName("account")] DeviceAccount? Account,
    [property: JsonPropertyName("device")] DeviceInfo? Device);
public sealed record DeviceAccount([property: JsonPropertyName("user_id")] string? UserId);
public sealed record DeviceInfo(
    [property: JsonPropertyName("mac")] string? Mac,
    [property: JsonPropertyName("model_id")] string? ModelId,
    [property: JsonPropertyName("company")] string? Company,
    [property: JsonPropertyName("lati")] string? Latitude,
    [property: JsonPropertyName("long")] string? Longitude,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("verify")] string? Verify);
```

```csharp
public enum DeviceTopicKind { Uplink, Lwt }
```

```csharp
public sealed record MqttEnvelope(
    [property: JsonPropertyName("sid")] string? Sid, // 껍데기에서는 로그용, 없어도 해석 실패로 보지 않음
    [property: JsonPropertyName("msg")] MqttMessage Msg);
public sealed record MqttMessage(
    [property: JsonPropertyName("o")] string Operation,
    [property: JsonPropertyName("e")] IReadOnlyList<MqttElement> Elements);
public sealed record MqttElement(
    [property: JsonPropertyName("n")] string Path,
    [property: JsonPropertyName("sv")] string? StringValue);

public static class MqttOperation
{
    public const string Register = "rg", Deregister = "drg", TimeSync = "tisyn", Notify = "notify",
        Write = "w", Change = "chg", Reboot = "reb";
}
```

```csharp
/// 장치 → 서버: {prefix}.v1/{deviceKey}/iot-server/{suffix} (suffix: json, timesync 등 — 근거 문서 §2 접미)
/// LWT: 같은 접두 + "/lwt" (추정. 근거 문서는 "/lwt" 접미만 관찰, 결합 형태 미확인 → 빈 토큰 무시)
/// 서버 → 장치: {prefix}.v1/iot-server/{deviceKey}
public sealed record DeviceTopic(string Prefix, string DeviceKey, DeviceTopicKind Kind)
{
    public static bool TryParse(string topic, [NotNullWhen(true)] out DeviceTopic? result)
    {
        string[] parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        result = null;
        // 접두 뒤 접미 토큰이 최소 1개 있어야 한다(3토큰 `x.v1/key/iot-server`는 false)
        // parts[0].Length > 3: 접두가 빈 `.v1/...` 토픽은 거부
        if (parts.Length < 4 || parts[0].Length <= 3 || !parts[0].EndsWith(".v1", StringComparison.Ordinal) || parts[2] != "iot-server")
            return false;
        DeviceTopicKind kind = parts[^1] == "lwt" ? DeviceTopicKind.Lwt : DeviceTopicKind.Uplink;
        string prefix = parts[0][..^3];
        result = new DeviceTopic(prefix, parts[1], kind);
        return true;
    }

    public static string ToDevice(string prefix, string deviceKey) => $"{prefix}.v1/iot-server/{deviceKey}";
}
```

```csharp
public interface IDeviceMessageHandler
{
    Task RegisterAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct);
    Task DeregisterAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct);
    Task TimeSyncAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct);
    Task NotifyAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct);
    Task OfflineAsync(DeviceTopic topic, CancellationToken ct);
}

public interface IDeviceCommandPublisher
{
    Task WriteAsync(string prefix, string deviceKey, IReadOnlyList<MqttElement> elements, CancellationToken ct);
    Task ChangeAsync(string prefix, string deviceKey, IReadOnlyList<MqttElement> elements, CancellationToken ct);
    Task RebootAsync(string prefix, string deviceKey, CancellationToken ct);
}
```

- 검증: `Core.Tests` — `DeviceTopic.TryParse`(`.../iot-server/json`·`.../iot-server//json`·`.../iot-server/timesync` → Uplink, `.../iot-server/lwt`·`.../iot-server//lwt` → Lwt, 서버→장치 토픽·3토큰 `x.v1/key/iot-server`·빈 접두 `.v1/key/iot-server/json` → false), `ToDevice`, 문서의 `rg` 등록 JSON 역직렬화(요소 7개, `/3/0/3` = `1.01.34`), `DeviceRegisterRequest` 본문 형식 1·2 역직렬화(`Device.ModelId == "B540_W"`, `Account.UserId`·`Longitude`·`Verify == "true"` 채워짐, 형식 2는 `Status` 포함)
- 선행: U1

## U4. Server.Spa 기반 (+ Server.Spa.Tests)

- 대상: `src/Biz.Bizadm.PowerManager.Server.Spa/` — SPA 템플릿 csproj·launchSettings·`Program.cs`·`AppConfiguration.cs`, 새 `HttpsPortRedirectionMiddleware.cs`. `ProjectReference`에 `Core` 추가, `PackageReference`에 `MQTTnet.Server` 추가. `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests` csproj에 `MSTest`, `Microsoft.AspNetCore.TestHost` PackageReference(TestServer 테스트용).
- 코드 초안 — `AppConfiguration`(템플릿 `Port` 제거):

```csharp
[Property(PropertyType.USHORT, DefaultValue = "80")]    public ushort? HttpPort { get; set; }
[Property(PropertyType.USHORT, DefaultValue = "443")]   public ushort? HttpsPort { get; set; }
[Property(PropertyType.USHORT, DefaultValue = "18446")] public ushort? DeviceHttpPort { get; set; }
[Property(PropertyType.USHORT, DefaultValue = "18443")] public ushort? DeviceHttpsPort { get; set; }
[Property(PropertyType.USHORT, DefaultValue = "1803")]  public ushort? MqttPort { get; set; }
[Property(PropertyType.USHORT, DefaultValue = "8883")]  public ushort? MqttsPort { get; set; }
```

- `Program.CreateWebApplication` 차이:

```csharp
builder.WebHost.ConfigureServerKestrel(
    [c.HttpPort!.Value, c.DeviceHttpPort!.Value], [c.HttpsPort!.Value, c.DeviceHttpsPort!.Value], c.Tls, c.ContentRootPath);
// ...
app.UseExceptionHandler();
app.UseStatusCodePages();
if (useTls)
{
    app.UseHsts();
    app.UseMiddleware<HttpsPortRedirectionMiddleware>(new Dictionary<int, int>
    {
        [c.HttpPort!.Value] = c.HttpsPort!.Value,
        [c.DeviceHttpPort!.Value] = c.DeviceHttpsPort!.Value,
    });
}
```

```csharp
public sealed class HttpsPortRedirectionMiddleware(RequestDelegate next, IReadOnlyDictionary<int, int> portMap)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.IsHttps || !portMap.TryGetValue(context.Connection.LocalPort, out int httpsPort))
            return next(context);

        // FOTA SDK는 기본 HTTP이고 HTTPS 지원 미확인 → /fota 세그먼트 경로(/fota, /fota/...)는 HTTP로 처리(방안 결정)
        if (context.Request.Path.StartsWithSegments("/fota", StringComparison.OrdinalIgnoreCase))
            return next(context);

        HttpRequest r = context.Request;
        if (!r.Host.HasValue)
            return next(context); // Host 없는 HTTP/1.0 요청은 Location을 만들 수 없어 통과

        // POST 본문 보존을 위해 307
        context.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
        context.Response.Headers.Location =
            UriHelper.BuildAbsolute("https", new HostString(r.Host.Host, httpsPort), r.PathBase, r.Path, r.QueryString);
        return Task.CompletedTask;
    }
}
```

- 검증: `Server.Spa.Tests` — SPA 템플릿 표(정적 파일, HTML fallback, 비HTML 404, `/health`) + `HttpsPortRedirectionMiddleware` 단위 테스트(`DefaultHttpContext`에 `LocalPort=18446`, POST → 307, `Location`=`https://host:18443/...`; HTTPS 요청·미등록 포트·Host 없음·`LocalPort=80`의 `GET /fota/b540/version`·`/fota`는 통과, `/fotax`는 307)
- 테스트 헬퍼: 템플릿 `PipelineTests`처럼 `new AppConfiguration { ... }`로 만들 때 포트 6개를 모두 명시한다. `MqttPort`는 빈 TCP 포트로 지정한다(hosted service가 TestServer에서도 실제 포트를 연다).
- `AppConfigurationTests`: SPA 구성에는 `Server.Tests`가 없으므로 API 템플릿의 `AppConfigurationTests`를 `Server.Spa.Tests`로 옮긴다. 포트 키를 바꾼 만큼 케이스를 추가한다: 포트 키가 없는 YAML을 `ConfigurationValidator.Validate`한 뒤 `HttpPort`~`MqttsPort`가 80/443/18446/18443/1803/8883인지 확인한다. `USHORT` `DefaultValue`가 채워지지 않으면 구현을 멈추고 보고한다(포트 키 필수화 등 사용자와 결정).
- 선행: U2, U3

## U5. 장치 REST·FOTA 컨트롤러

- 대상: `Server.Spa/Devices/DevicesController.cs`, `Server.Spa/Fota/FotaController.cs`
- 코드 초안:

```csharp
[ApiController]
public sealed class DevicesController(ILogger<DevicesController> logger) : ControllerBase
{
    [HttpPost("/api/v1/devices/register/create")]
    public IActionResult Register([FromBody] DeviceRegisterRequest request)
    {
        logger.LogInformation("Device register requested: mac={Mac}, model={Model}, company={Company}",
            request.Device?.Mac, request.Device?.ModelId, request.Device?.Company);
        return Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Not implemented");
    }

    [HttpGet("/api/v1/device/connect/{deviceKey}")]
    public IActionResult Connect(string deviceKey)
    {
        logger.LogInformation("Device connect requested: {DeviceKey}", deviceKey);
        return Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Not implemented");
    }
}

[ApiController]
public sealed class FotaController(ILogger<FotaController> logger) : ControllerBase
{
    [HttpGet("/fota/{**path}"), HttpHead("/fota/{**path}")]
    public IActionResult Get(string? path)
    {
        path ??= string.Empty;
        // 라우팅·Redirect 제외와 같이 대소문자 무시
        bool isVersion = path.EndsWith("/version", StringComparison.OrdinalIgnoreCase)
            || path.Equals("version", StringComparison.OrdinalIgnoreCase);
        logger.LogInformation("FOTA {Kind} requested: {Path}", isVersion ? "version" : "image", path);
        return Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Not implemented");
    }
}
```

- 장치 요청은 `Content-Type: application/json`, 헤더 `X-HIT-Version`을 보낸다. 껍데기에서는 헤더를 검사하지 않는다.
- `DeviceRegisterRequest`(U3)의 속성은 모두 nullable로 둔다. `[ApiController]`가 non-nullable 참조 속성을 암묵적 `[Required]`로 보고 로그 없이 400을 돌려주는 것을 막는다. 로그는 `request.Device?.Mac`처럼 null 안전하게 남긴다. 빈 본문·깨진 JSON의 자동 400은 설계 §6 예외로 유지한다.
- FOTA는 `[HttpGet]`·`[HttpHead]`를 둔다. GET만 두면 운영 모드에서 확장자 없는 경로의 HEAD가 SPA fallback으로 가 404가 된다(설계 §5).
- 검증: TestServer로 `POST register/create`(문서 본문 형식 1) → 501 `application/problem+json`, 필드 일부 누락 본문 → 501, `GET device/connect/DAWONDNS-B540_W-aabbccddeeff` → 501, `GET /fota/b540/version`·`/fota/b540/user2.bin`·`/fota/` → 501, `HEAD /fota/b540/version`·`HEAD /fota/b540/user2.bin` → 501(Production 환경 TestServer, fallback 있는 상태)
- 선행: U4

## U6. 내장 MQTT 브로커

- 대상: `Server.Spa/Mqtt/MqttBrokerHostedService.cs`, `LoggingDeviceMessageHandler.cs`, `MqttDeviceCommandPublisher.cs`, `Program` 서비스 등록. `Server.Spa.Tests` csproj에 `MQTTnet`(테스트 클라이언트) PackageReference를 추가한다(`MSTest`·`TestHost`는 U4에서 추가됨).
- 코드 초안:

```csharp
bool useTls = c.Tls?.UseTls == true;
MqttServerOptionsBuilder ob = new MqttServerFactory().CreateServerOptionsBuilder()
    .WithDefaultEndpoint().WithDefaultEndpointPort(c.MqttPort!.Value);
if (useTls)
{
    certificate = TlsCertificateLoader.Load(c.Tls!, c.ContentRootPath); // 필드 보관, StopAsync에서 Dispose
    ob = ob.WithEncryptedEndpoint().WithEncryptedEndpointPort(c.MqttsPort!.Value)
        .WithEncryptionCertificate(certificate)          // 체인·cipher 미적용
        .WithEncryptionSslProtocol(SslProtocols.Tls12);  // 장치 mbedTLS TLSv1.0~1.2
}

server = new MqttServerFactory().CreateMqttServer(ob.Build());
server.InterceptingPublishAsync += OnPublishAsync;
await server.StartAsync();

// StopAsync: Ctrl+C 정상 종료(U8). DisposeAsync: StopAsync 없이 호스트가 해제될 때(테스트) 리스너 정리.
// 클래스는 IHostedService, IAsyncDisposable을 구현한다.
public async Task StopAsync(CancellationToken ct) => await DisposeAsync();

public async ValueTask DisposeAsync()
{
    if (server is not null)
    {
        await server.StopAsync(); // v5 MqttServerStopOptions 인자 필요 여부는 구현 시 확인
        server.Dispose();
        server = null;
    }
    certificate?.Dispose();
    certificate = null;
}
```

```csharp
private async Task OnPublishAsync(InterceptingPublishEventArgs e)
{
    if (!DeviceTopic.TryParse(e.ApplicationMessage.Topic, out DeviceTopic? topic))
    {
        // 서버→장치 토픽 등은 그대로 전달. 실장비 토픽 형태 확인용(템플릿 최소 레벨 Information이라 Debug 불가)
        logger.LogInformation("Unparsed topic passed through: {Topic}", e.ApplicationMessage.Topic);
        return;
    }

    try
    {
        if (topic.Kind == DeviceTopicKind.Lwt)
        {
            await handler.OfflineAsync(topic, e.CancellationToken);
            return;
        }

        // v5 Payload는 ReadOnlySequence<byte> → Utf8JsonReader(ReadOnlySequence) 로 역직렬화.
        // STJ는 nullable 주석을 강제하지 않으므로 Msg·Elements·Operation null도 "해석 실패"로 경고 로그 후 null 반환(Sid는 nullable이라 검사하지 않음).
        MqttEnvelope? envelope = TryDeserialize(e.ApplicationMessage.Payload);
        if (envelope is null)
            return;

        Task task = envelope.Msg.Operation switch
        {
            MqttOperation.Register => handler.RegisterAsync(topic, envelope, e.CancellationToken),
            MqttOperation.Deregister => handler.DeregisterAsync(topic, envelope, e.CancellationToken),
            MqttOperation.TimeSync => handler.TimeSyncAsync(topic, envelope, e.CancellationToken),
            MqttOperation.Notify => handler.NotifyAsync(topic, envelope, e.CancellationToken),
            _ => LogUnknown(topic, envelope),
        };
        await task;
    }
    catch (OperationCanceledException) when (e.CancellationToken.IsCancellationRequested)
    {
        // 브로커 종료 중 취소는 정상 흐름이라 Error로 남기지 않는다
    }
    catch (Exception ex)
    {
        // 핸들러 예외가 브로커 연결 처리로 번지지 않게 로그만 남긴다
        logger.LogError(ex, "Device message handling failed: {Topic}", e.ApplicationMessage.Topic);
    }
}
```

- 설정 전달: `MqttBrokerHostedService`는 `CreateWebApplication`에서 `services.AddSingleton(appConfiguration)`으로 등록한 `AppConfiguration`(KMS 복호화 후)을 생성자로 받는다.
- 등록: `services.AddSingleton<IDeviceMessageHandler, LoggingDeviceMessageHandler>()`, `AddSingleton<IDeviceCommandPublisher, MqttDeviceCommandPublisher>()`(모든 메서드 `throw new NotImplementedException()`), `AddHostedService<MqttBrokerHostedService>()`. 브로커 접속 인증 없음. 이 등록은 `CreateWebApplication`에서 `configureBuilder?.Invoke(builder)` **앞**에 둔다. 테스트는 `configureBuilder`에서 `services.Replace(ServiceDescriptor.Singleton<IDeviceMessageHandler>(testHandler))`로 바꿔, 등록 순서와 관계없이 교체되게 한다.
- 프로토콜: MQTTnet 서버가 MQTT 3.1(`MQIsdp`) CONNECT를 수용하는지 테스트에서 확인한다(`MqttProtocolVersion.V310` 클라이언트). 수용하지 않으면 구현을 멈추고 사용자에게 보고한다(브로커 선택 재검토).
- API 확인: MQTTnet 5.2의 `WithEncryptionCertificate` 오버로드(`X509Certificate2` 또는 `X509CertificateProvider`)와 `MqttServer.StopAsync`의 `MqttServerStopOptions` 인자 필요 여부, TLS 엔드포인트의 `CipherSuitesPolicy` 지원 여부(적용은 범위 밖, 설계 §3 위험 대응 수단 확인용)는 구현 시 패키지 API로 확인하고 맞는 형태를 쓴다.
- MQTTS 검증(Server.Spa.Tests): `Tls.UseTls = true`와 임시 자체 서명 PFX(평문 패스워드, TestServer 경로라 KMS 미동작)로 구성하고, `MqttPort`·`MqttsPort`에 각각 빈 TCP 포트를 지정해 앱을 띄운다. MQTTnet 클라이언트(V310)가 `MqttsPort`에 TLS 1.2로 접속(테스트에서 서버 인증서 검증은 지문 비교로 허용)해 `rg`를 발행 → `RegisterAsync` 1회 수신. 같은 클라이언트를 TLS 1.3만 허용하도록 접속하면 실패해야 한다(TLS 1.2 고정 확인, OS가 1.3을 지원하지 않으면 이 케이스는 생략하고 기록).
- 검증: Server.Spa.Tests — 빈 TCP 포트를 `MqttPort`로 지정해 앱 기동, MQTTnet 클라이언트(V310)로 접속 후 `dwd.v1/DAWONDNS-B540_W-aabbccddeeff/iot-server/json`에 `rg` 발행 → 테스트용 `IDeviceMessageHandler`가 `RegisterAsync` 1회 수신. `msg` 없는 JSON 발행 → 핸들러 호출 없음(`AddServerSerilog`가 테스트 `ILoggerProvider`로 로그를 넘기지 않으므로, 경고 로그는 `await app.StopAsync()`와 해제로 File sink를 닫은 뒤 임시 로그 디렉터리의 파일에서 확인. 실행 중에 읽어야 하면 `FileShare.ReadWrite`로 연다). LWT: V310 클라이언트가 Will 토픽 `dwd.v1/DAWONDNS-B540_W-aabbccddeeff/iot-server/lwt`(payload `offline`)로 접속한 뒤 DISCONNECT 패킷 없이 연결을 끊는다 → `OfflineAsync` 1회 수신. MQTTnet 클라이언트의 `DisconnectAsync`는 DISCONNECT를 보내 Will을 폐기하므로 쓰지 않는다. 방법은 원시 `TcpClient`로 MQTT 3.1 CONNECT(Will 포함)를 보낸 뒤 `Close()`한다(대안: 짧은 keepalive 후 응답 중단). Will 메시지가 `InterceptingPublishAsync`를 거치지 않으면 구현을 멈추고 `ClientDisconnectedAsync` 등 대체 경로를 사용자와 정한다. 다른 앱 테스트(U4·U5)는 빈 포트를 써서 1803 충돌을 피한다. 앱을 띄운 테스트는 끝에서 `await app.StopAsync()`를 호출하고 `await using`으로 해제한다.
- 선행: U3, U4

## U7. Client (React)

- 대상: `src/Biz.Bizadm.PowerManager.Client/` — 스킬 `react-project-create` + SPA 스킬 4단계 그대로. 화면은 `App.tsx` 제목 한 줄.
- 코드 초안 — `vite.config.ts` 프록시만 이 프로젝트에 맞춘다:

```ts
// 개발 모드는 Tls 없이 실행(설계 §3). TLS를 켜면 SERVER_URL=https://localhost:443 + secure:false
const serverUrl = process.env.SERVER_URL ?? "http://localhost:80";

export default defineConfig({
  // ... existing plugins·resolve·test ...
  server: {
    // ... existing port: 5173, strictPort: true ...
    proxy: {
      // React가 호출하는 경로만(장치·FOTA는 Vite를 거치지 않음)
      "/api": { target: serverUrl, changeOrigin: true, secure: false },
    },
  },
});
```

- 검증: `npx tsc -b`, `npm run lint`, `npm test`, `npm run build` 모두 종료 코드 0, `npm ls react react-dom vite typescript tailwindcss vitest --depth=0` 단일 버전, `npm audit`·`npm audit --omit=dev` 결과 기록, `npm run dev`로 Vite(5173) 접속 시 `App.tsx` 제목 렌더링·브라우저 콘솔 오류 없음(스킬 `react-project-create` 9단계, shadcn 컴포넌트는 추가하지 않으므로 Button 확인은 해당 없음)
- 선행: 없음(U4의 `SpaRoot`와 경로만 맞춤)

## U8. 통합 검증

검증 전용 단위다. 제품 코드는 바꾸지 않으며, 코드 초안은 핵심 검증 스크립트 조각이다.

- 코드 초안 — TLS Redirect·`/fota` 제외 확인과 Ctrl+C 정상 종료 확인(PowerShell, 임시 포트 변수 사용):

```powershell
# Redirect: /fota는 501, 그 외는 대응 HTTPS 포트로 307
curl.exe -s -o NUL -w "%{http_code}" "http://localhost:$DeviceHttpPort/fota/b540/version"        # 501
curl.exe -s -o NUL -w "%{http_code} %{redirect_url}" -X POST "http://localhost:$DeviceHttpPort/api/v1/devices/register/create"  # 307 https://localhost:$DeviceHttpsPort/...

# Ctrl+C(스킬 dotnet-server-api-template 7단계 방법): 대상 exe는 별도 콘솔에서 실행하고,
# 별도 PowerShell 프로세스가 그 콘솔에 붙어 Ctrl+C를 보낸다(검증 셸은 중단되지 않음).
# 임시 경로는 공백 없는 경로를 쓰고, 인자는 각각 인용해 넘긴다.
$p = Start-Process $exe -ArgumentList '--config', "`"$yaml`"", '--log', "`"$logDir`"" -PassThru   # 새 콘솔 창
$null = $p.Handle   # 종료 전에 핸들을 잡아 ExitCode가 $null이 되지 않게 함
# ... 기동 대기(로그·포트 리슨 확인) ...
powershell -NoProfile -File $sendCtrlC -ProcessId $p.Id
#   $sendCtrlC 내부: FreeConsole() → AttachConsole(pid) → SetConsoleCtrlHandler(null, $true) → GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0)
$p.WaitForExit(); $p.ExitCode   # 0
```

- `dotnet build Biz.Bizadm.PowerManager.slnx --no-incremental` 경고 0, `dotnet test --solution Biz.Bizadm.PowerManager.slnx` 통과(기존 DeviceConfig 테스트 포함, MTP 모드). U2~U6 단위 검증도 `dotnet test --project tests/<프로젝트>` 형식을 쓴다
- publish(SPA 스킬 7단계 표)
  - `dotnet publish src/Biz.Bizadm.PowerManager.Server.Spa -c Release -o <임시>` → `<임시>/ClientApp/index.html`
  - 같은 `<임시>`에 잔여 파일을 넣고 다시 publish → 잔여 파일 없음
  - `-p:SkipClientAppBuild=true` → npm 미실행, 기존 `ClientApp` 그대로
- 빌드 exe 실행(인자 없음 → 1, `OsKms` 없음 → 2)은 자동 실행.
- 아래는 KMS가 OS 자격 증명 저장소에 기록하고 80/443 바인딩이 다른 프로세스(IIS 등)·권한과 충돌할 수 있어 **사용자 승인 후** 임시 `CredentialService`·임시 포트 YAML(포트 6개 모두 임시 값, MQTT 1803 포함)로 실행하고 정리한다. 정리: `cmdkey /delete:"Biz.Bizadm.KMS:<임시 CredentialService>"` 후 `cmdkey /list`로 해당 항목이 남지 않았는지 확인, 임시 YAML·DEK·로그 디렉터리 삭제(스킬 `dotnet-server-api-template` 7단계).
  - 정상 YAML(TLS 없음) 기동: `HttpPort`·`DeviceHttpPort` 각각 `Now listening on` 로그, MQTT 포트 리슨, HTTP/2·3 관련 경고 없음
  - 배포본 exe(Production): `/` 200 `index.html`, `/some/route`(HTML) 200, `/assets/*.js` 200, `/api/none`(JSON) 404 `application/problem+json`, `/health` 200 `Healthy`, 로그 파일 `Biz.Bizadm.PowerManager.Server.Spa<yyyyMMdd>.log` 생성
  - `OsKms.CredentialService`가 URI가 아님 → 종료 코드 2
  - 같은 포트로 두 번째 실행 → 종료 코드 3
  - 정상 기동한 배포본 exe에 `GenerateConsoleCtrlEvent`로 Ctrl+C → 종료 코드 0, 로그 `Application is shutting down`, HTTP·MQTT 포트 리슨 없음(강제 종료는 정상 종료 검증으로 보지 않는다)
  - `UseTls: true` + 평문 `ServerCertificatePassword` → YAML에 `DEKENC:` 값으로 기록, 재기동 시 YAML 무변경
  - `UseTls: true`(임시 PFX), curl로 확인:
    - `HttpPort`·`DeviceHttpPort`의 `GET /fota/b540/version` → 307 없이 501
    - `HttpsPort`·`DeviceHttpsPort`의 같은 요청 → 501(임시 자체 서명 인증서라 `curl.exe -k`)
    - `HttpPort`·`DeviceHttpPort`의 그 외 경로(`GET /`, `POST /api/v1/devices/register/create`) → 대응 HTTPS 포트로 307(`HttpPort`→`HttpsPort`, `DeviceHttpPort`→`DeviceHttpsPort`)
    - MQTT·MQTTS 포트 리슨, MQTTS 포트 TLS 1.2 핸드셰이크 성공(`openssl s_client -connect localhost:<MqttsPort> -tls1_2` 또는 `SslStream`)
  - 개발 모드 `dotnet run --project src/Biz.Bizadm.PowerManager.Server.Spa --launch-profile http -- --config <임시 YAML> --log <임시>`(Tls 없음): `GET /` 302 → `http://localhost:5173/`, `/health`·`/api/none`·`/openapi/v1.json`(200) 리다이렉트 없음, Ctrl+C 후 dotnet·npm·node 종료. 임시 포트 YAML이면 Vite 프록시까지 확인할 때 `SERVER_URL=http://localhost:<임시 HttpPort>`를 지정한다.
- 선행: U1–U7

## 비고

- 커밋·PR, GitHub 이슈 등록은 구현 시작 시 규칙 `git/github-issue.md`에 따라 진행한다.
