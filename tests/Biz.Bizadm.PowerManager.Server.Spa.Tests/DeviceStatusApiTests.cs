using Biz.Bizadm.PowerManager.Server.Spa.Devices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

/// <summary>
/// 장비 상태 조회·스트림 API(device-control C4·C5·C15).
/// </summary>
[TestClass]
public sealed class DeviceStatusApiTests
{
    private const string Mac = "3a0000000601";
    private const string StatusPath = "/api/admin/devices/status";
    private const string StreamPath = "/api/admin/devices/status/stream";
    private static readonly string[] ExpectedFields =
        ["mac", "name", "modelId", "online", "relay", "voltage", "current", "powerFactor", "power", "lastReportAt", "controllable"];

    private string tempRoot = null!;
    private AppConfiguration configuration = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.DeviceStatus.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "ClientApp"));
        configuration = TestApp.Configuration(tempRoot);
    }

    [TestCleanup]
    public void TestCleanup() => TestApp.DeleteTempRoot(tempRoot);

    private Task<WebApplication> StartAsync() => TestApp.StartAsync(configuration, Path.Combine(tempRoot, "log"));

    /// <summary>
    /// 열어 둔 상태 스트림. 이벤트를 하나씩 읽는다.
    /// </summary>
    private sealed class StreamReaderClient(HttpContext context, CancellationTokenSource abort) : IDisposable
    {
        private readonly StreamReader reader = new StreamReader(context.Response.Body);

        public HttpContext Context => context;

        public static async Task<StreamReaderClient> OpenAsync(WebApplication app, int localPort = 80)
        {
            CancellationTokenSource abort = new CancellationTokenSource();
            HttpContext context = await app.GetTestServer().SendAsync(c =>
            {
                c.Connection.LocalPort = localPort;
                c.Request.Method = "GET";
                c.Request.Scheme = "http";
                c.Request.Host = new HostString("localhost", localPort);
                c.Request.Path = StreamPath;
                c.Request.Headers.Accept = "text/event-stream";
            }, abort.Token);
            return new StreamReaderClient(context, abort);
        }

        /// <summary>
        /// 다음 이벤트의 이름과 데이터. 스트림이 끝났으면 null.
        /// </summary>
        public async Task<(string Event, JsonElement Data)?> ReadAsync()
        {
            string? name = null;
            string? data = null;
            while (await reader.ReadLineAsync().WaitAsync(TestMqtt.Timeout) is string line)
            {
                if (line.StartsWith("event: ", StringComparison.Ordinal))
                    name = line["event: ".Length..];
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                    data += line["data: ".Length..];
                else if (line.Length == 0 && data is not null)
                {
                    using JsonDocument document = JsonDocument.Parse(data);
                    return (name ?? "message", document.RootElement.Clone());
                }
            }
            return null;
        }

        /// <summary>
        /// 조건에 맞는 <c>device</c> 이벤트가 올 때까지 읽는다.
        /// </summary>
        public async Task<JsonElement> ReadDeviceAsync(Func<JsonElement, bool> condition)
        {
            while (await ReadAsync() is (string name, JsonElement data))
            {
                Assert.AreEqual("device", name);
                CollectionAssert.AreEquivalent(ExpectedFields, data.EnumerateObject().Select(p => p.Name).ToArray());
                if (condition(data))
                    return data;
            }
            Assert.Fail("스트림이 끝났다.");
            return default;
        }

        public void Abort() => abort.Cancel();

        public void Dispose()
        {
            abort.Cancel();
            reader.Dispose();
            abort.Dispose();
        }
    }

    // C4: 등록 장비 전체를 mac 오름차순으로 준다. 보고가 없던 장비는 모름·null이고 MQTT 키는 없다.
    [TestMethod]
    [DataRow(80)]
    [DataRow(443)]
    public async Task Status_AdminPort_ReturnsAllRegisteredDevices(int port)
    {
        await using WebApplication app = await StartAsync();
        TestDevice second = await TestDevices.RegisterAsync(app, "3a0000000602", "M130-W");
        await using TestMqttDevice device = await TestMqttDevice.ConnectAsync(app, configuration, Mac);
        await device.SendAsync("n", """[{"n":"/100/0/31","sv":"true"},{"n":"/100/0/11","sv":"12.34"}]""");

        (int status, string? contentType, string body) = await TestMqtt.SendHttpAsync(app, port, "GET", StatusPath);

        Assert.AreEqual(200, status, body);
        Assert.AreEqual("application/json", contentType);
        Assert.IsFalse(body.Contains("mqtt", StringComparison.OrdinalIgnoreCase), body);
        Assert.IsFalse(body.Contains("key", StringComparison.OrdinalIgnoreCase), body);
        Assert.IsFalse(body.Contains(second.Password, StringComparison.Ordinal), body);
        Assert.IsFalse(body.Contains(device.Device.Password, StringComparison.Ordinal), body);
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement[] items = document.RootElement.EnumerateArray().ToArray();
        CollectionAssert.AreEqual(new[] { Mac, "3a0000000602" }, items.Select(i => i.GetProperty("mac").GetString()).ToArray());
        foreach (JsonElement item in items)
            CollectionAssert.AreEquivalent(ExpectedFields, item.EnumerateObject().Select(p => p.Name).ToArray());

        Assert.AreEqual("B540_W", items[0].GetProperty("modelId").GetString());
        Assert.IsTrue(items[0].GetProperty("online").GetBoolean());
        Assert.AreEqual("on", items[0].GetProperty("relay").GetString());
        Assert.AreEqual(12.34, items[0].GetProperty("power").GetDouble());
        Assert.IsTrue(items[0].GetProperty("controllable").GetBoolean());

        Assert.IsFalse(items[1].GetProperty("online").GetBoolean());
        Assert.AreEqual("unknown", items[1].GetProperty("relay").GetString());
        foreach (string name in new[] { "voltage", "current", "powerFactor", "power", "lastReportAt" })
            Assert.AreEqual(JsonValueKind.Null, items[1].GetProperty(name).ValueKind, name);
        Assert.IsFalse(items[1].GetProperty("controllable").GetBoolean());
        await app.StopAsync();
    }

    // C4: 접속했지만 토픽 정보가 없으면, 또는 토픽 정보가 있지만 오프라인이면 제어 가능하지 않다.
    [TestMethod]
    public async Task Status_Controllable_NeedsOnlineAndTopic()
    {
        await using WebApplication app = await StartAsync();
        TestMqttDevice device = await TestMqttDevice.ConnectAsync(app, configuration, Mac);
        await TestMqtt.WaitUntilAsync(async () => await PropertyAsync("online"), "온라인이 되지 않았다.");
        Assert.IsFalse(await PropertyAsync("controllable"));
        await device.ReportRelayAsync(true);
        Assert.IsTrue(await PropertyAsync("controllable"));
        await device.DisposeAsync();
        await TestMqtt.WaitUntilAsync(async () => !await PropertyAsync("online"), "오프라인이 되지 않았다.");
        Assert.IsFalse(await PropertyAsync("controllable"));
        await app.StopAsync();

        async Task<bool> PropertyAsync(string name)
        {
            (_, _, string body) = await TestMqtt.SendHttpAsync(app, 80, "GET", StatusPath);
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement[0].GetProperty(name).GetBoolean();
        }
    }

    // C5: 먼저 snapshot, 그 뒤 등록·접속·토픽 처음 알게 됨·상태 보고·모델 변경·끊김마다 device 이벤트가 온다.
    [TestMethod]
    public async Task Stream_SendsSnapshotThenChanges()
    {
        await using WebApplication app = await StartAsync();
        await TestDevices.RegisterAsync(app, "3a0000000609");
        using StreamReaderClient stream = await StreamReaderClient.OpenAsync(app);
        Assert.AreEqual(200, stream.Context.Response.StatusCode);
        Assert.AreEqual("text/event-stream", stream.Context.Response.ContentType?.Split(';')[0]);

        (string Event, JsonElement Data)? first = await stream.ReadAsync();
        Assert.AreEqual("snapshot", first!.Value.Event);
        JsonElement[] items = first.Value.Data.EnumerateArray().ToArray();
        Assert.HasCount(1, items);
        CollectionAssert.AreEquivalent(ExpectedFields, items[0].EnumerateObject().Select(p => p.Name).ToArray());

        await TestDevices.RegisterAsync(app, Mac);
        JsonElement registered = await stream.ReadDeviceAsync(d => d.GetProperty("mac").GetString() == Mac);
        Assert.IsFalse(registered.GetProperty("online").GetBoolean());

        TestMqttDevice device = await TestMqttDevice.ConnectAsync(app, configuration, Mac);
        JsonElement connected = await stream.ReadDeviceAsync(d => d.GetProperty("online").GetBoolean());
        Assert.IsFalse(connected.GetProperty("controllable").GetBoolean());

        await device.SendAsync("tisyn", "[]", "timesync/json");
        JsonElement topicKnown = await stream.ReadDeviceAsync(d => d.GetProperty("controllable").GetBoolean());
        Assert.AreEqual("unknown", topicKnown.GetProperty("relay").GetString());

        await device.ReportRelayAsync(true);
        await stream.ReadDeviceAsync(d => d.GetProperty("relay").GetString() == "on");

        await app.Services.GetRequiredService<DeviceRegistrationService>()
            .RegisterAsync(Mac, null, "B540_X", null, CancellationToken.None);
        await stream.ReadDeviceAsync(d => d.GetProperty("modelId").GetString() == "B540_X");

        await device.DisposeAsync();
        await stream.ReadDeviceAsync(d => !d.GetProperty("online").GetBoolean());
        await app.StopAsync();
    }

    // device-name-and-units C13: 장비명이 바뀌면 그 장비의 원소를 보내고, 같은 값으로 저장하면 보내지 않는다.
    [TestMethod]
    public async Task Stream_NameChanged_SendsDeviceOnlyWhenDifferent()
    {
        const string Other = "3a0000000609";
        await using WebApplication app = await StartAsync();
        await TestDevices.RegisterAsync(app, Mac);
        await TestDevices.RegisterAsync(app, Other);
        using StreamReaderClient stream = await StreamReaderClient.OpenAsync(app);
        await stream.ReadAsync();

        await RenameAsync(app, Mac, "거실");
        (string Event, JsonElement Data)? changed = await stream.ReadAsync();
        Assert.AreEqual("device", changed!.Value.Event);
        Assert.AreEqual(Mac, changed.Value.Data.GetProperty("mac").GetString());
        Assert.AreEqual("거실", changed.Value.Data.GetProperty("name").GetString());

        await RenameAsync(app, Mac, " 거실 ");
        await RenameAsync(app, Other, "서재");
        (string Event, JsonElement Data)? next = await stream.ReadAsync();
        Assert.AreEqual(Other, next!.Value.Data.GetProperty("mac").GetString());
        await app.StopAsync();
    }

    private static async Task RenameAsync(WebApplication app, string mac, string name)
    {
        (int status, _, string body) = await TestMqtt.SendHttpAsync(app, 80, "PUT", $"/api/admin/devices/{mac}/name", JsonSerializer.Serialize(new { name }));
        Assert.AreEqual(204, status, body);
    }

    // C5: 클라이언트가 연결을 끊으면 구독이 풀린다.
    [TestMethod]
    public async Task Stream_EndsWhenClientAborts()
    {
        await using WebApplication app = await StartAsync();
        using StreamReaderClient stream = await StreamReaderClient.OpenAsync(app);
        await stream.ReadAsync();
        Assert.AreEqual(1, SubscriptionCount(app));

        stream.Abort();

        await TestMqtt.WaitUntilAsync(() => SubscriptionCount(app) == 0, "끊은 스트림의 구독이 남았다.");
        await app.StopAsync();
    }

    // C5: 서버가 종료하면 스트림이 끝난다(종료가 막히지 않는다).
    [TestMethod]
    public async Task Stream_EndsWhenServerStops()
    {
        await using WebApplication app = await StartAsync();
        using StreamReaderClient stream = await StreamReaderClient.OpenAsync(app);
        await stream.ReadAsync();

        Task stop = app.StopAsync();

        Assert.IsNull(await stream.ReadAsync());
        await stop.WaitAsync(TestMqtt.Timeout);
    }

    // C15: 장치 포트에서는 404 ProblemDetails이고 장비 정보를 내주지 않는다.
    [TestMethod]
    [DataRow(18446, StatusPath)]
    [DataRow(18443, StatusPath)]
    [DataRow(18446, StreamPath)]
    [DataRow(18443, StreamPath)]
    [DataRow(18446, "/api/admin/devices/3a0000000601/settings")]
    [DataRow(18443, "/api/admin/devices/3a0000000601/settings")]
    public async Task DevicePort_Returns404Problem(int port, string path)
    {
        await using WebApplication app = await StartAsync();
        await TestDevices.RegisterAsync(app, Mac);

        (int status, string? contentType, string body) = await TestMqtt.SendHttpAsync(app, port, "GET", path);

        Assert.AreEqual(404, status);
        Assert.AreEqual("application/problem+json", contentType);
        Assert.IsFalse(body.Contains(Mac, StringComparison.Ordinal), body);
        await app.StopAsync();
    }

    private static int SubscriptionCount(WebApplication app) => app.Services.GetRequiredService<DeviceStateStore>().SubscriptionCount;
}
