using Biz.Bizadm.PowerManager.Core.Devices;
using Biz.Bizadm.PowerManager.FakeDevice.App.Tests.Fakes;
using System.Text;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Tests;

[TestClass]
public sealed class RunnerRestTests
{
    private const string RegisterPath = "/api/v1/devices/register/create";
    private const string ConnectPath = "/api/v1/device/connect/" + TestHarness.DeviceKey;

    private FakeDawonHttp http = null!;
    private FakeDawonBroker broker = null!;
    private string recordDirectory = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        http = await FakeDawonHttp.StartAsync();
        broker = await FakeDawonBroker.StartAsync();
        recordDirectory = TestHarness.NewTempDirectory();
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        await broker.DisposeAsync();
        await http.DisposeAsync();
        TestHarness.DeleteDirectory(recordDirectory);
    }

    private FakeDeviceOptions Options(string? mqttKey = null) => TestHarness.Options(http, broker, recordDirectory) with { MqttKey = mqttKey };

    private static FakeDawonHttp.Reply Json(int status, string body) => new(status, "application/json", Encoding.UTF8.GetBytes(body));

    private void RespondRegister(Func<FakeDawonHttp.Reply> register)
        => http.Responder = r => r.Path == RegisterPath ? register() : Json(200, "{\"delay\":\"0\"}");

    [TestMethod]
    public async Task Register_SendsSpecRequest_AndConnectsWithKey()
    {
        RespondRegister(() => Json(200, $"{{\"mqtt_key\":\"{TestHarness.TestKey}\"}}"));
        await using RunnerSession session = RunnerSession.Start(Options());

        await TestHarness.WaitUntilAsync(() => !broker.Connections.IsEmpty, "MQTT connection");

        FakeDawonHttp.Received register = http.Requests.First();
        Assert.AreEqual("https", register.Scheme);
        Assert.AreEqual("POST", register.Method);
        Assert.AreEqual(RegisterPath, register.Path);
        Assert.AreEqual("application/json", register.Headers["Content-Type"]);
        Assert.AreEqual("1.0", register.Headers["X-HIT-Version"]);
        Assert.AreEqual(
            "{\"account\":{\"user_id\":\"tester\"},\"device\":{\"mac\":\"a1b2c3d4e5f6\",\"model_id\":\"B540_W\",\"company\":\"DAWONDNS\",\"lati\":\"0\",\"long\":\"0\",\"verify\":\"true\"}}",
            Encoding.UTF8.GetString(register.Body));
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(TestHarness.TestKey), broker.Connections.Single().Password);

        List<JsonElement> records = session.Records();
        JsonElement request = records.Of("rest", "request").First();
        Assert.AreEqual("device-to-dawon", request.GetProperty("direction").GetString());
        Assert.AreEqual(RegisterPath, request.GetProperty("path").GetString());
        JsonElement response = records.Of("rest", "response").First();
        Assert.AreEqual(200, response.GetProperty("status").GetInt32());
        Assert.AreEqual("{\"mqtt_key\":\"***\"}", response.GetProperty("body").GetString());
    }

    [TestMethod]
    [DataRow("match")]
    [DataRow("mismatch")]
    [DataRow("uncomputable")]
    public async Task Register_VerifyJudgement_IsRecordedAndPrinted(string expected)
    {
        string key = expected == "uncomputable" ? "Key_Outside_Alphabet" : TestHarness.TestKey;
        string verify = expected == "match" ? DeviceVerify.Compute("dwmqtt.dawonai.com", TestHarness.Mac, key) : "VerifyValueNotMatching1234";
        RespondRegister(() => Json(200, $"{{\"mqtt_key\":\"{key}\",\"verify\":\"{verify}\"}}"));
        await using RunnerSession session = RunnerSession.Start(Options());

        await TestHarness.WaitUntilAsync(() => !broker.Connections.IsEmpty, "MQTT connection");
        await TestHarness.WaitUntilAsync(() => session.Records().Of("rest", "verify").Any(), "verify record");

        Assert.AreEqual(expected, session.Records().Of("rest", "verify").Single().GetProperty("result").GetString());
        StringAssert.Contains(session.Console, $"verify: {expected}");

        session.Input.Send("quit");
        Assert.IsTrue(await session.ResultAsync());
        string raw = TestHarness.RawRecords(recordDirectory);
        Assert.IsFalse(raw.Contains(key, StringComparison.Ordinal), "key in records");
        Assert.IsFalse(raw.Contains(verify, StringComparison.Ordinal), "verify in records");
        Assert.IsFalse(session.Console.Contains(key, StringComparison.Ordinal), "key in console");
        Assert.IsFalse(session.Console.Contains(verify, StringComparison.Ordinal), "verify in console");
    }

    [TestMethod]
    [DataRow("409")]
    [DataRow("not-json")]
    [DataRow("no-key")]
    [DataRow("empty-key")]
    [DataRow("number-key")]
    [DataRow("array")]
    [DataRow("delay")]
    [DataRow("closed-port")]
    public async Task Register_Failure_EndsWithoutMqtt(string scenario)
    {
        FakeDeviceOptions options = Options() with { RestTimeout = TimeSpan.FromSeconds(1) };
        if (scenario == "closed-port")
            options = options with { ApiServerPort = TestHarness.FreeTcpPort() };
        RespondRegister(() => scenario switch
        {
            "409" => Json(409, "{\"status\":\"REGISTERED\"}"),
            "not-json" => new FakeDawonHttp.Reply(200, "text/plain", Encoding.UTF8.GetBytes("mqtt_key=abc")),
            "no-key" => Json(200, "{\"status\":\"OK\"}"),
            "empty-key" => Json(200, "{\"mqtt_key\":\"\"}"),
            "number-key" => Json(200, "{\"mqtt_key\":12345}"),
            "array" => Json(200, "[{\"mqtt_key\":\"abc\"}]"),
            "delay" => Json(200, $"{{\"mqtt_key\":\"{TestHarness.TestKey}\"}}") with { Delay = TimeSpan.FromSeconds(3) },
            _ => Json(500, "{}"),
        });
        await using RunnerSession session = RunnerSession.Start(options);

        Assert.IsFalse(await session.ResultAsync());

        Assert.IsTrue(broker.Connections.IsEmpty, "no MQTT connection");
        List<JsonElement> records = session.Records();
        Assert.IsFalse(records.Of("rest", "request").Any(r => r.GetProperty("path").GetString()!.StartsWith("/api/v1/device/connect/")), "no device/connect");
        if (scenario is "delay" or "closed-port")
        {
            JsonElement error = records.Of("rest", "error").Single();
            Assert.AreEqual(RegisterPath, error.GetProperty("path").GetString());
            Assert.AreEqual($"https://127.0.0.1:{options.ApiServerPort}", error.GetProperty("target").GetString());
            if (scenario == "delay")
                Assert.AreEqual("timeout", error.GetProperty("error").GetString());
            StringAssert.Contains(session.Console, "register/create: error");
        }
        else
        {
            Assert.AreEqual(1, records.Of("rest", "response").Count());
            StringAssert.Contains(session.Console, "register/create failed: status");
        }
    }

    [TestMethod]
    public async Task ConfiguredKey_SkipsRegister_AndSendsConnect()
    {
        await using RunnerSession session = RunnerSession.Start(Options(TestHarness.TestKey));

        await TestHarness.WaitUntilAsync(() => !broker.Connections.IsEmpty, "MQTT connection");

        FakeDawonHttp.Received only = http.Requests.Single();
        Assert.AreEqual("GET", only.Method);
        Assert.AreEqual(ConnectPath, only.Path);
        StringAssert.Contains(session.Console, "device/connect: status 200");
        Assert.AreEqual(ConnectPath, session.Records().Of("rest", "response").Single().GetProperty("path").GetString());
    }

    [TestMethod]
    [DataRow("404")]
    [DataRow("delay")]
    public async Task Connect_FailureOrTimeout_StillConnectsMqtt(string scenario)
    {
        http.Responder = _ => scenario == "404"
            ? Json(404, "{}")
            : Json(200, "{\"delay\":\"0\"}") with { Delay = TimeSpan.FromSeconds(3) };
        await using RunnerSession session = RunnerSession.Start(Options(TestHarness.TestKey) with { RestTimeout = TimeSpan.FromSeconds(1) });

        await TestHarness.WaitUntilAsync(() => !broker.Connections.IsEmpty, "MQTT connection");

        List<JsonElement> records = session.Records();
        if (scenario == "404")
            Assert.AreEqual(404, records.Of("rest", "response").Single().GetProperty("status").GetInt32());
        else
            Assert.AreEqual("timeout", records.Of("rest", "error").Single().GetProperty("error").GetString());
    }

    [TestMethod]
    public async Task ConfiguredKey_ClosedRestPort_RecordsErrorAndConnectsMqtt()
    {
        FakeDeviceOptions options = Options(TestHarness.TestKey) with { ApiServerPort = TestHarness.FreeTcpPort() };
        await using RunnerSession session = RunnerSession.Start(options);

        await TestHarness.WaitUntilAsync(() => !broker.Connections.IsEmpty, "MQTT connection");

        JsonElement error = session.Records().Of("rest", "error").Single();
        Assert.AreEqual(ConnectPath, error.GetProperty("path").GetString());
        Assert.AreEqual($"https://127.0.0.1:{options.ApiServerPort}", error.GetProperty("target").GetString());
        StringAssert.Contains(session.Console, "device/connect: error");
    }

    [TestMethod]
    public void Options_ToString_HidesSecrets()
    {
        string text = (TestHarness.Options(null, null, recordDirectory) with { Password = TestHarness.TestPassword }).ToString();

        Assert.IsFalse(text.Contains(TestHarness.TestKey, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(TestHarness.TestPassword, StringComparison.Ordinal));
        StringAssert.Contains(text, "MqttKey = ***");
        StringAssert.Contains(text, "Password = ***");
    }
}
