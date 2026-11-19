using Biz.Bizadm.PowerManager.Server.Spa.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

/// <summary>
/// 관리용 장비 목록 API와 포트 제한. TestServer는 실제 포트를 열지 않으므로 요청의 <c>Connection.LocalPort</c>를 직접 정한다
/// (<see cref="TestApp.Configuration"/>: 관리 80·443, 장치 18446·18443, TLS 없음).
/// </summary>
[TestClass]
public sealed class AdminDeviceEndpointTests
{
    private const string ListPath = "/api/admin/devices";
    private static readonly string[] ExpectedFields = ["mac", "userId", "modelId", "company", "registeredAt", "lastRequestedAt"];

    private static string tempRoot = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.Server.Spa.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "ClientApp"));
        File.WriteAllText(Path.Combine(tempRoot, "ClientApp", "index.html"), "<html>index</html>");
    }

    [ClassCleanup]
    public static void ClassCleanup() => TestApp.DeleteTempRoot(tempRoot);

    private static Task<WebApplication> StartAsync()
    {
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        configuration.DatabasePath = $"devices-{Guid.NewGuid():N}.db";
        return TestApp.StartAsync(configuration, Path.Combine(tempRoot, "log"));
    }

    private static async Task<(int Status, string? ContentType, string Body)> SendAsync(
        WebApplication app, int localPort, string method, string path, string? jsonBody = null)
    {
        HttpContext context = await app.GetTestServer().SendAsync(c =>
        {
            c.Connection.LocalPort = localPort;
            c.Request.Method = method;
            c.Request.Scheme = "http";
            c.Request.Host = new HostString("localhost", localPort);
            c.Request.Path = path;
            c.Request.Headers.Accept = "application/json";
            if (jsonBody is not null)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(jsonBody);
                c.Request.ContentType = "application/json";
                c.Request.ContentLength = bytes.Length;
                c.Request.Body = new MemoryStream(bytes);
            }
        });
        using StreamReader reader = new StreamReader(context.Response.Body);
        string body = await reader.ReadToEndAsync();
        string? contentType = context.Response.ContentType?.Split(';')[0].Trim();
        return (context.Response.StatusCode, contentType, body);
    }

    private static async Task AddDevicesAsync(WebApplication app, params DeviceRegistration[] devices)
    {
        IDbContextFactory<PowerManagerDbContext> factory = app.Services.GetRequiredService<IDbContextFactory<PowerManagerDbContext>>();
        await using PowerManagerDbContext db = await factory.CreateDbContextAsync();
        db.Devices.AddRange(devices);
        await db.SaveChangesAsync();
    }

    private static DeviceRegistration Device(string mac, DateTimeOffset lastRequestedAt, string userId = "user-01", string modelId = "M130-W") => new()
    {
        Mac = mac,
        UserId = userId,
        ModelId = modelId,
        Company = "DAWONDNS",
        MqttKey = "SecretKey" + mac[..7],
        RegisteredAt = new DateTimeOffset(2026, 11, 12, 0, 14, 0, TimeSpan.Zero),
        LastRequestedAt = lastRequestedAt,
    };

    // C1·C2: 관리 포트는 필드 여섯 개짜리 배열을 마지막 요청 최근 순(같으면 mac 오름차순)으로 준다. MQTT 키는 없다.
    [TestMethod]
    [DataRow(80)]
    [DataRow(443)]
    public async Task List_AdminPort_ReturnsSixFieldsSortedByLastRequested(int port)
    {
        await using WebApplication app = await StartAsync();
        DateTimeOffset t1 = new DateTimeOffset(2026, 11, 19, 1, 0, 0, TimeSpan.Zero);
        DateTimeOffset t2 = t1.AddMinutes(5);
        DateTimeOffset t3 = t1.AddMinutes(10);
        await AddDevicesAsync(app,
            Device("2ef4322ee918", t2),
            Device("2ef4322ee8dd", t3),
            Device("2ef4322ee886", t1, userId: "", modelId: ""),
            Device("0a0000000001", t3));

        (int status, string? contentType, string body) = await SendAsync(app, port, "GET", ListPath);

        Assert.AreEqual(StatusCodes.Status200OK, status);
        Assert.AreEqual("application/json", contentType);
        Assert.IsFalse(body.Contains("mqtt", StringComparison.OrdinalIgnoreCase), body);
        Assert.IsFalse(body.Contains("SecretKey", StringComparison.Ordinal), body);

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement[] items = document.RootElement.EnumerateArray().ToArray();
        CollectionAssert.AreEqual(new[] { "0a0000000001", "2ef4322ee8dd", "2ef4322ee918", "2ef4322ee886" },
            items.Select(i => i.GetProperty("mac").GetString()).ToArray());
        foreach (JsonElement item in items)
            CollectionAssert.AreEquivalent(ExpectedFields, item.EnumerateObject().Select(p => p.Name).ToArray());

        JsonElement first = items[0];
        Assert.AreEqual("user-01", first.GetProperty("userId").GetString());
        Assert.AreEqual("M130-W", first.GetProperty("modelId").GetString());
        Assert.AreEqual("DAWONDNS", first.GetProperty("company").GetString());
        Assert.AreEqual(string.Empty, items[3].GetProperty("userId").GetString());

        Regex iso = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?[+-]\d{2}:\d{2}$");
        string registeredAt = first.GetProperty("registeredAt").GetString()!;
        string lastRequestedAt = first.GetProperty("lastRequestedAt").GetString()!;
        Assert.IsTrue(iso.IsMatch(registeredAt), registeredAt);
        Assert.IsTrue(iso.IsMatch(lastRequestedAt), lastRequestedAt);
        Assert.AreEqual(t3, DateTimeOffset.Parse(lastRequestedAt));
        await app.StopAsync();
    }

    // C2
    [TestMethod]
    public async Task List_NoDevices_ReturnsEmptyArray()
    {
        await using WebApplication app = await StartAsync();

        (int status, string? contentType, string body) = await SendAsync(app, 80, "GET", ListPath);

        Assert.AreEqual(StatusCodes.Status200OK, status);
        Assert.AreEqual("application/json", contentType);
        Assert.AreEqual("[]", body);
        await app.StopAsync();
    }

    // C3: 장치 포트에서 처리되는 목록 요청은 404 ProblemDetails이고 장비 정보를 내주지 않는다.
    [TestMethod]
    [DataRow(18446)]
    [DataRow(18443)]
    public async Task List_DevicePort_Returns404ProblemWithoutDevices(int port)
    {
        await using WebApplication app = await StartAsync();
        await AddDevicesAsync(app, Device("2ef4322ee918", DateTimeOffset.UtcNow));

        (int status, string? contentType, string body) = await SendAsync(app, port, "GET", ListPath);

        Assert.AreEqual(StatusCodes.Status404NotFound, status);
        Assert.AreEqual("application/problem+json", contentType);
        Assert.IsFalse(body.Contains("2ef4322ee918", StringComparison.Ordinal), body);
        await app.StopAsync();
    }

    // C3: 기존 장치 endpoint(register·connect)는 관리·장치 포트 모두에서 그대로 처리된다.
    [TestMethod]
    [DataRow(80)]
    [DataRow(443)]
    [DataRow(18446)]
    [DataRow(18443)]
    public async Task DeviceEndpoints_AnyPort_StillWork(int port)
    {
        await using WebApplication app = await StartAsync();

        (int registerStatus, string? registerType, string registerBody) = await SendAsync(app, port, "POST",
            "/api/v1/devices/register/create", """{"device":{"mac":"aabbccddeeff","model_id":"M130-W"}}""");
        Assert.AreEqual(StatusCodes.Status200OK, registerStatus, registerBody);
        Assert.AreEqual("application/json", registerType);

        (int connectStatus, _, string connectBody) = await SendAsync(app, port, "GET", "/api/v1/device/connect/DAWONDNS-M130-W-aabbccddeeff");
        Assert.AreEqual(StatusCodes.Status200OK, connectStatus, connectBody);
        await app.StopAsync();
    }
}
