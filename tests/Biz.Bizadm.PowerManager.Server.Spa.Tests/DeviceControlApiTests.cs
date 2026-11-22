using Biz.Bizadm.PowerManager.Core.Mqtt;
using Biz.Bizadm.PowerManager.Server.Spa.Mqtt;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

/// <summary>
/// 장비 제어·설정 API(device-control C6~C15). 테스트 MQTT 클라이언트가 받은 제어 메시지로 확인한다.
/// </summary>
[TestClass]
public sealed class DeviceControlApiTests
{
    private const string Mac = "3a0000000701";
    private const string Base = "/api/admin/devices/" + Mac;
    private const string ReportBody = """{"minInterval":60,"maxInterval":600,"changeThreshold":5}""";
    private const string StandbyBody = """{"enabled":true,"thresholdWatts":5.0,"delay":60}""";
    private const string ScheduleBody = """{"enabled":true,"days":[false,true,true,true,true,true,false],"time":"03:00:00","seconds":10}""";

    private string tempRoot = null!;
    private AppConfiguration configuration = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.DeviceControl.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "ClientApp"));
        configuration = TestApp.Configuration(tempRoot);
    }

    [TestCleanup]
    public void TestCleanup() => TestApp.DeleteTempRoot(tempRoot);

    private string LogDirectory => Path.Combine(tempRoot, "log");

    private Task<WebApplication> StartAsync() => TestApp.StartAsync(configuration, LogDirectory);

    /// <summary>
    /// 접속해 구독하고 릴레이 상태를 보고한(제어 가능하고 릴레이 읽기가 나가지 않는) 장비.
    /// </summary>
    private async Task<TestMqttDevice> ControllableDeviceAsync(WebApplication app, string modelId = "B540_W")
    {
        TestMqttDevice device = await TestMqttDevice.ConnectAsync(app, configuration, Mac, modelId);
        await device.ReportRelayAsync(true);
        return device;
    }

    private static Task<(int Status, string? ContentType, string Body)> CallAsync(WebApplication app, string method, string path, string? body = null,
        int port = 80)
        => TestMqtt.SendHttpAsync(app, port, method, path, body);

    private static async Task AssertProblemAsync(WebApplication app, int expected, string method, string path, string? body = null)
    {
        (int status, string? contentType, string text) = await CallAsync(app, method, path, body);
        Assert.AreEqual(expected, status, $"{method} {path} {body}: {text}");
        Assert.AreEqual("application/problem+json", contentType);
    }

    private static string MessagePart(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        CollectionAssert.AreEqual(new[] { "sid", "msg" }, root.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual(JsonValueKind.String, root.GetProperty("sid").ValueKind);
        return root.GetProperty("msg").GetRawText();
    }

    // C7~C12: API마다 오퍼레이션, 요소 순서와 값이 맞고 retain이 아니다.
    [TestMethod]
    [DataRow("POST", "/relay", """{"on":true}""", """{"o":"e","e":[{"n":"/100/0/31","sv":"true"}]}""")]
    [DataRow("POST", "/relay", """{"on":false}""", """{"o":"e","e":[{"n":"/100/0/31","sv":"false"}]}""")]
    [DataRow("POST", "/power-cycle", """{"seconds":10}""", """{"o":"e","e":[{"n":"/100/0/31","sv":"false"},{"n":"/100/0/68","sv":"10"}]}""")]
    [DataRow("POST", "/reboot", null, """{"o":"reb","e":[]}""")]
    [DataRow("PUT", "/report-condition", ReportBody, """{"o":"w","e":[{"n":"/99/0/2","sv":"60"},{"n":"/99/0/3","sv":"600"},{"n":"/99/0/4","sv":"5"}]}""")]
    [DataRow("PUT", "/standby-cutoff", StandbyBody, """{"o":"w","e":[{"n":"/100/0/40","sv":"true"},{"n":"/100/0/41","sv":"5.00"},{"n":"/100/0/42","sv":"60"}]}""")]
    [DataRow("PUT", "/standby-cutoff", """{"enabled":false,"thresholdWatts":0,"delay":0}""", """{"o":"w","e":[{"n":"/100/0/40","sv":"false"},{"n":"/100/0/41","sv":"0.00"},{"n":"/100/0/42","sv":"0"}]}""")]
    [DataRow("PUT", "/schedule", ScheduleBody, """{"o":"w","e":[{"n":"/100/0/58","sv":"1,U,T,0,0111110,03:00:00,F,10"}]}""")]
    [DataRow("PUT", "/schedule", """{"enabled":false,"days":[true,false,false,false,false,false,false],"time":"23:59:58","seconds":1}""", """{"o":"w","e":[{"n":"/100/0/58","sv":"1,U,F,0,0200000,23:59:58,F,1"}]}""")]
    [DataRow("PUT", "/schedule", """{"enabled":true,"days":[false,true,false,false,false,false,false],"time":"03:00:00","seconds":10}""", """{"o":"w","e":[{"n":"/100/0/58","sv":"1,U,T,0,0100000,03:00:00,F,10"}]}""")]
    [DataRow("PUT", "/schedule", """{"enabled":true,"days":[true,true,false,false,false,false,false],"time":"03:00:00","seconds":10}""", """{"o":"w","e":[{"n":"/100/0/58","sv":"1,U,T,0,0300000,03:00:00,F,10"}]}""")]
    [DataRow("PUT", "/schedule", """{"enabled":true,"days":[true,true,true,true,true,true,true],"time":"03:00:00","seconds":10}""", """{"o":"w","e":[{"n":"/100/0/58","sv":"1,U,T,0,0311111,03:00:00,F,10"}]}""")]
    [DataRow("PUT", "/schedule", """{"enabled":true,"days":[false,false,true,true,true,true,true],"time":"03:00:00","seconds":10}""", """{"o":"w","e":[{"n":"/100/0/58","sv":"1,U,T,0,0011111,03:00:00,F,10"}]}""")]
    [DataRow("DELETE", "/schedule", null, """{"o":"w","e":[{"n":"/100/0/58","sv":"1,D,F,0,1111111,00:00:00,F,0"}]}""")]
    public async Task Command_SendsExpectedMessage(string method, string path, string? body, string expected)
    {
        await using WebApplication app = await StartAsync();
        await using TestMqttDevice device = await ControllableDeviceAsync(app);

        (int status, _, string text) = await CallAsync(app, method, Base + path, body);

        Assert.AreEqual(204, status, text);
        Assert.AreEqual(string.Empty, text);
        await device.FlushAsync();
        Assert.HasCount(1, device.Messages);
        Assert.AreEqual(device.DownlinkTopic, device.Messages[0].Topic);
        Assert.IsFalse(device.Messages[0].Retain);
        Assert.AreEqual(expected, MessagePart(device.Payloads[0]));
        await app.StopAsync();
    }

    // sid는 서버 실행마다 100부터 1씩 늘린 10진 문자열이다(plan 결정).
    [TestMethod]
    public async Task Command_SidStartsAt100AndIncreases()
    {
        await using WebApplication app = await StartAsync();
        await using TestMqttDevice device = await ControllableDeviceAsync(app);
        await CallAsync(app, "POST", Base + "/relay", """{"on":true}""");
        await CallAsync(app, "POST", Base + "/relay", """{"on":false}""");
        await device.FlushAsync();
        string[] sids = device.Payloads.Select(p => JsonDocument.Parse(p).RootElement.GetProperty("sid").GetString()!).ToArray();
        CollectionAssert.AreEqual(new[] { "100", "101" }, sids);
        await app.StopAsync();
    }

    // C8·C10·C11·C12: 범위의 양 끝은 받는다.
    [TestMethod]
    [DataRow("POST", "/power-cycle", """{"seconds":1}""")]
    [DataRow("POST", "/power-cycle", """{"seconds":999}""")]
    [DataRow("PUT", "/report-condition", """{"minInterval":1,"maxInterval":11,"changeThreshold":1}""")]
    [DataRow("PUT", "/report-condition", """{"minInterval":3600,"maxInterval":3600,"changeThreshold":100}""")]
    [DataRow("PUT", "/report-condition", """{"minInterval":3600,"maxInterval":14400,"changeThreshold":100}""")]
    [DataRow("PUT", "/standby-cutoff", """{"enabled":true,"thresholdWatts":9999.99,"delay":9999}""")]
    [DataRow("PUT", "/standby-cutoff", """{"enabled":true,"thresholdWatts":0.010,"delay":0}""")]
    [DataRow("PUT", "/schedule", """{"enabled":true,"days":[true,true,true,true,true,true,true],"time":"00:00:00","seconds":59}""")]
    [DataRow("PUT", "/schedule", """{"enabled":true,"days":[false,false,false,false,false,false,true],"time":"23:59:50","seconds":9}""")]
    public async Task Command_RangeEdges_AreAccepted(string method, string path, string body)
    {
        await using WebApplication app = await StartAsync();
        await using TestMqttDevice device = await ControllableDeviceAsync(app);

        (int status, _, string text) = await CallAsync(app, method, Base + path, body);

        Assert.AreEqual(204, status, text);
        await app.StopAsync();
    }

    // C6·C8·C10·C11·C12: 범위를 벗어나거나 빠진 값은 400이고 아무것도 보내지 않는다.
    [TestMethod]
    public async Task Command_OutOfRange_Returns400WithoutMessage()
    {
        (string Method, string Path, string Body)[] cases =
        [
            ("POST", "/relay", "{}"),
            ("POST", "/relay", """{"on":"yes"}"""),
            ("POST", "/power-cycle", "{}"),
            ("POST", "/power-cycle", """{"seconds":0}"""),
            ("POST", "/power-cycle", """{"seconds":1000}"""),
            ("POST", "/power-cycle", """{"seconds":1.5}"""),
            ("PUT", "/report-condition", "{}"),
            ("PUT", "/report-condition", """{"minInterval":0,"maxInterval":600,"changeThreshold":5}"""),
            ("PUT", "/report-condition", """{"minInterval":3601,"maxInterval":14400,"changeThreshold":5}"""),
            ("PUT", "/report-condition", """{"minInterval":1,"maxInterval":10,"changeThreshold":5}"""),
            ("PUT", "/report-condition", """{"minInterval":1,"maxInterval":14401,"changeThreshold":5}"""),
            ("PUT", "/report-condition", """{"minInterval":1,"maxInterval":600,"changeThreshold":0}"""),
            ("PUT", "/report-condition", """{"minInterval":1,"maxInterval":600,"changeThreshold":101}"""),
            ("PUT", "/report-condition", """{"minInterval":601,"maxInterval":600,"changeThreshold":5}"""),
            ("PUT", "/standby-cutoff", """{"thresholdWatts":5,"delay":1}"""),
            ("PUT", "/standby-cutoff", """{"enabled":true,"thresholdWatts":-0.01,"delay":1}"""),
            ("PUT", "/standby-cutoff", """{"enabled":true,"thresholdWatts":10000,"delay":1}"""),
            ("PUT", "/standby-cutoff", """{"enabled":true,"thresholdWatts":5.001,"delay":1}"""),
            ("PUT", "/standby-cutoff", """{"enabled":true,"thresholdWatts":5,"delay":-1}"""),
            ("PUT", "/standby-cutoff", """{"enabled":true,"thresholdWatts":5,"delay":10000}"""),
            ("PUT", "/schedule", """{"enabled":true,"days":[true,true,true,true,true,true],"time":"03:00:00","seconds":10}"""),
            ("PUT", "/schedule", """{"enabled":true,"days":[false,false,false,false,false,false,false],"time":"03:00:00","seconds":10}"""),
            ("PUT", "/schedule", """{"enabled":true,"days":[true,true,true,true,true,true,true],"time":"3:00:00","seconds":10}"""),
            ("PUT", "/schedule", """{"enabled":true,"days":[true,true,true,true,true,true,true],"time":"24:00:00","seconds":10}"""),
            ("PUT", "/schedule", """{"enabled":true,"days":[true,true,true,true,true,true,true],"time":"03:00:00","seconds":0}"""),
            ("PUT", "/schedule", """{"enabled":true,"days":[true,true,true,true,true,true,true],"time":"03:00:00","seconds":60}"""),
            ("PUT", "/schedule", """{"enabled":true,"days":[true,true,true,true,true,true,true],"time":"03:00:50","seconds":10}"""),
            ("PUT", "/schedule", """{"days":[true,true,true,true,true,true,true],"time":"03:00:00","seconds":10}"""),
        ];
        await using WebApplication app = await StartAsync();
        await using TestMqttDevice device = await ControllableDeviceAsync(app);

        foreach ((string method, string path, string body) in cases)
            await AssertProblemAsync(app, 400, method, Base + path, body);

        await device.FlushAsync();
        Assert.IsEmpty(device.Payloads);
        (_, _, string settings) = await CallAsync(app, "GET", Base + "/settings");
        Assert.AreEqual("""{"reportCondition":null,"standbyCutoff":null,"schedule":null}""", settings);
        await app.StopAsync();
    }

    // C9: 모델이 B540으로 시작하지 않으면 재부팅은 400이고 보내지 않는다. 다른 제어는 받는다.
    [TestMethod]
    public async Task Reboot_OtherModel_Returns400WithoutMessage()
    {
        await using WebApplication app = await StartAsync();
        await using TestMqttDevice device = await ControllableDeviceAsync(app, "M130-W");

        await AssertProblemAsync(app, 400, "POST", Base + "/reboot");
        await device.FlushAsync();
        Assert.IsEmpty(device.Payloads);

        (int status, _, string text) = await CallAsync(app, "POST", Base + "/relay", """{"on":true}""");
        Assert.AreEqual(204, status, text);
        await app.StopAsync();
    }

    // C6: mac 형식 400, 미등록 404. 등록됐지만 제어 가능하지 않으면 409이되, 입력이 틀리면 400이 먼저다.
    [TestMethod]
    public async Task Command_StatusCodes_FollowOrder()
    {
        (string Method, string Path, string? Body)[] commands =
        [
            ("POST", "/relay", """{"on":true}"""),
            ("POST", "/power-cycle", """{"seconds":10}"""),
            ("POST", "/reboot", null),
            ("PUT", "/report-condition", ReportBody),
            ("PUT", "/standby-cutoff", StandbyBody),
            ("PUT", "/schedule", ScheduleBody),
            ("DELETE", "/schedule", null),
        ];
        await using WebApplication app = await StartAsync();
        await TestDevices.RegisterAsync(app, Mac);

        foreach ((string method, string path, string? body) in commands)
        {
            await AssertProblemAsync(app, 400, method, "/api/admin/devices/not-a-mac" + path, body);
            await AssertProblemAsync(app, 400, method, "/api/admin/devices/3A0000000701" + path, body);
            await AssertProblemAsync(app, 404, method, "/api/admin/devices/3a00000007ff" + path, body);
            await AssertProblemAsync(app, 409, method, Base + path, body);
        }
        await AssertProblemAsync(app, 404, "POST", "/api/admin/devices/3a00000007ff/power-cycle", """{"seconds":0}""");
        await AssertProblemAsync(app, 400, "POST", Base + "/power-cycle", """{"seconds":0}""");
        await app.StopAsync();
    }

    // C6: 제어 가능하지만 구독하지 않은 장비는 큐에 넣지 못해 409이고 설정도 기억하지 않는다. 다른 클라이언트가 구독해도 성공이 아니다.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Command_NotEnqueuedToDevice_Returns409(bool otherSubscriber)
    {
        await using WebApplication app = await StartAsync();
        await using TestMqttDevice device = await TestMqttDevice.ConnectAsync(app, configuration, Mac, subscribe: false);
        await device.ReportRelayAsync(true);
        await using TestMqttDevice other = await TestMqttDevice.ConnectAsync(app, configuration, "3a0000000702", subscribe: false);
        if (otherSubscriber)
            await other.SubscribeToAsync(device.DownlinkTopic + "/#");

        await AssertProblemAsync(app, 409, "PUT", Base + "/report-condition", ReportBody);

        (_, _, string settings) = await CallAsync(app, "GET", Base + "/settings");
        Assert.AreEqual("""{"reportCondition":null,"standbyCutoff":null,"schedule":null}""", settings);
        await WaitForLogAsync("[Warning]", "Device command not enqueued", $"mac=\"{Mac}\"", "operation=\"w\"", "/99/0/2=60");
        await app.StopAsync();
    }

    // C13: 성공한 마지막 값을 돌려주고, 예약 삭제가 성공하면 예약은 null이다. 형식 오류 400, 미등록 404.
    [TestMethod]
    public async Task Settings_ReturnsLastSentValues()
    {
        await using WebApplication app = await StartAsync();
        await using TestMqttDevice device = await ControllableDeviceAsync(app);
        await AssertProblemAsync(app, 400, "GET", "/api/admin/devices/xyz/settings");
        await AssertProblemAsync(app, 404, "GET", "/api/admin/devices/3a00000007ff/settings");

        await CallAsync(app, "PUT", Base + "/report-condition", ReportBody);
        await CallAsync(app, "PUT", Base + "/report-condition", """{"minInterval":30,"maxInterval":300,"changeThreshold":7}""");
        await CallAsync(app, "PUT", Base + "/standby-cutoff", StandbyBody);
        await CallAsync(app, "PUT", Base + "/schedule", ScheduleBody);
        (int status, _, string body) = await CallAsync(app, "GET", Base + "/settings");
        Assert.AreEqual(200, status);
        using (JsonDocument document = JsonDocument.Parse(body))
        {
            JsonElement root = document.RootElement;
            Assert.AreEqual("""{"minInterval":30,"maxInterval":300,"changeThreshold":7}""", root.GetProperty("reportCondition").GetRawText());
            JsonElement standby = root.GetProperty("standbyCutoff");
            Assert.IsTrue(standby.GetProperty("enabled").GetBoolean());
            Assert.AreEqual(5m, standby.GetProperty("thresholdWatts").GetDecimal());
            Assert.AreEqual(60, standby.GetProperty("delay").GetInt32());
            Assert.AreEqual(ScheduleBody, root.GetProperty("schedule").GetRawText());
        }

        (status, _, _) = await CallAsync(app, "DELETE", Base + "/schedule");
        Assert.AreEqual(204, status);
        (_, _, body) = await CallAsync(app, "GET", Base + "/settings");
        using (JsonDocument document = JsonDocument.Parse(body))
        {
            Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("schedule").ValueKind);
            Assert.AreEqual(JsonValueKind.Object, document.RootElement.GetProperty("reportCondition").ValueKind);
        }
        await app.StopAsync();
    }

    // C13·C6: 실패한 예약 삭제는 기억한 예약을 지우지 않는다.
    [TestMethod]
    public async Task Settings_FailedDelete_KeepsSchedule()
    {
        await using WebApplication app = await StartAsync();
        TestMqttDevice device = await ControllableDeviceAsync(app);
        await CallAsync(app, "PUT", Base + "/schedule", ScheduleBody);
        await device.DisposeAsync();
        await TestMqtt.WaitUntilAsync(async () => (await CallAsync(app, "DELETE", Base + "/schedule")).Status == 409, "오프라인 장비의 삭제가 409가 아니다.");

        (_, _, string body) = await CallAsync(app, "GET", Base + "/settings");
        using JsonDocument document = JsonDocument.Parse(body);
        Assert.AreEqual(JsonValueKind.Object, document.RootElement.GetProperty("schedule").ValueKind);
        await app.StopAsync();
    }

    // C14: 보낸 제어 메시지마다 Information 항목 하나에 mac·토픽·sid·오퍼레이션·요소가 남는다.
    [TestMethod]
    public async Task Command_LogsOneInformationEntry()
    {
        await using WebApplication app = await StartAsync();
        await using TestMqttDevice device = await ControllableDeviceAsync(app);

        await CallAsync(app, "POST", Base + "/power-cycle", """{"seconds":10}""");

        await WaitForLogAsync("[Information]", "Device command sent", $"mac=\"{Mac}\"", $"topic=\"{device.DownlinkTopic}\"", "sid=\"100\"",
            "operation=\"e\"", "elements=\"/100/0/31=false, /100/0/68=10\"");
        Assert.AreEqual(1, TestMqtt.ReadLogs(LogDirectory).Split('\n').Count(l => l.Contains("Device command sent", StringComparison.Ordinal)));
        await app.StopAsync();
    }

    // C14: 브로커가 없으면 Error 로그를 남기고 큐에 넣지 못한 것으로 돌려준다.
    [TestMethod]
    public async Task Publish_WithoutBroker_LogsErrorAndIsNotEnqueued()
    {
        await using WebApplication app = await StartAsync();
        MqttServerAccessor accessor = app.Services.GetRequiredService<MqttServerAccessor>();
        MQTTnet.Server.MqttServer? server = accessor.Server;
        accessor.Server = null;
        try
        {
            DevicePublishResult result = await app.Services.GetRequiredService<IDeviceCommandPublisher>().PublishAsync(
                new DeviceCommandTarget(Mac, "dwd", "DAWONDNS-B540_W-" + Mac), MqttOperation.Execute,
                [new MqttElement("/100/0/31", "true")], CancellationToken.None);
            Assert.IsFalse(result.Enqueued);
        }
        finally
        {
            accessor.Server = server;
        }
        await WaitForLogAsync("[Error]", "Device command failed", $"mac=\"{Mac}\"", "elements=\"/100/0/31=true\"");
        await app.StopAsync();
    }

    // C15: 장치 포트에서는 404 ProblemDetails이고 제어 메시지를 보내지 않는다.
    [TestMethod]
    [DataRow(18446)]
    [DataRow(18443)]
    public async Task DevicePort_Returns404WithoutMessage(int port)
    {
        await using WebApplication app = await StartAsync();
        await using TestMqttDevice device = await ControllableDeviceAsync(app);
        (string Method, string Path, string? Body)[] commands =
        [
            ("POST", "/relay", """{"on":true}"""),
            ("POST", "/power-cycle", """{"seconds":10}"""),
            ("POST", "/reboot", null),
            ("PUT", "/report-condition", ReportBody),
            ("PUT", "/standby-cutoff", StandbyBody),
            ("PUT", "/schedule", ScheduleBody),
            ("DELETE", "/schedule", null),
            ("GET", "/settings", null),
        ];

        foreach ((string method, string path, string? body) in commands)
        {
            (int status, string? contentType, string text) = await CallAsync(app, method, Base + path, body, port);
            Assert.AreEqual(404, status, $"{method} {path}");
            Assert.AreEqual("application/problem+json", contentType);
            Assert.IsFalse(text.Contains(Mac, StringComparison.Ordinal), text);
        }
        await device.FlushAsync();
        Assert.IsEmpty(device.Payloads);
        await app.StopAsync();
    }

    private Task WaitForLogAsync(params string[] parts)
        => TestMqtt.WaitUntilAsync(() => TestMqtt.ReadLogs(LogDirectory).Split('\n').Any(l => parts.All(p => l.Contains(p, StringComparison.Ordinal))),
            "로그가 없다: " + string.Join(" … ", parts));
}
