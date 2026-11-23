using Biz.Bizadm.PowerManager.Core.Mqtt;
using Biz.Bizadm.PowerManager.Server.Spa.Devices;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet.Server;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

/// <summary>
/// 장비 상태(device-control C1·C2·C3·C30과 장비의 메시지 판정). 테스트 MQTT 클라이언트로 보내고 상태 조회 API와 받은 서버 → 장치 메시지로 확인한다.
/// </summary>
[TestClass]
public sealed class DeviceStateTests
{
    private const string Mac = "3a0000000501";
    private const string RelayReadJson = """{"o":"r","e":[{"n":"/100/0/30","sv":""}]}""";

    private string tempRoot = null!;
    private AppConfiguration configuration = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.DeviceState.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "ClientApp"));
        configuration = TestApp.Configuration(tempRoot);
    }

    [TestCleanup]
    public void TestCleanup() => TestApp.DeleteTempRoot(tempRoot);

    private string LogDirectory => Path.Combine(tempRoot, "log");

    private Task<WebApplication> StartAsync() => TestApp.StartAsync(configuration, LogDirectory);

    private static async Task<JsonElement> StatusAsync(WebApplication app, string mac = Mac)
    {
        (int status, _, string body) = await TestMqtt.SendHttpAsync(app, 80, "GET", "/api/admin/devices/status");
        Assert.AreEqual(200, status, body);
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.EnumerateArray().Single(e => e.GetProperty("mac").GetString() == mac).Clone();
    }

    private static Task WaitOnlineAsync(WebApplication app, bool online)
        => TestMqtt.WaitUntilAsync(async () => (await StatusAsync(app)).GetProperty("online").GetBoolean() == online, $"online이 {online}이 되지 않았다.");

    private static string MessagePart(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("msg").GetRawText();
    }

    // C1: notify·n의 릴레이·계량 요소를 반영하고, 없는 항목은 유지하며, 숫자가 아닌 계량 값은 무시한다. 보고마다 마지막 보고 시각을 바꾼다.
    [TestMethod]
    [DataRow("notify")]
    [DataRow("n")]
    public async Task StatusReport_UpdatesRelayAndMeters(string operation)
    {
        await using WebApplication app = await StartAsync();
        await using TestMqttDevice device = await TestMqttDevice.ConnectAsync(app, configuration, Mac);

        await device.SendAsync(operation, """
            [{"n":"/100/0/31","sv":"true"},{"n":"/100/0/1","sv":"221.40"},{"n":"/100/0/2","sv":"0.512"},
             {"n":"/100/0/3","sv":"0.987"},{"n":"/100/0/11","sv":"111.90"},{"n":"/100/0/4","sv":"9.99"}]
            """);
        JsonElement first = await StatusAsync(app);
        Assert.AreEqual("on", first.GetProperty("relay").GetString());
        Assert.AreEqual(221.40, first.GetProperty("voltage").GetDouble());
        Assert.AreEqual(0.512, first.GetProperty("current").GetDouble());
        Assert.AreEqual(0.987, first.GetProperty("powerFactor").GetDouble());
        Assert.AreEqual(111.90, first.GetProperty("power").GetDouble());
        DateTimeOffset firstReportAt = first.GetProperty("lastReportAt").GetDateTimeOffset();

        await Task.Delay(20);
        await device.SendAsync(operation, """
            [{"n":"/100/0/30","sv":"false"},{"n":"/100/0/1","sv":"x"},{"n":"/100/0/2","sv":"NaN"},{"n":"/100/0/3"},{"n":"/100/0/11","sv":"0.00"}]
            """);
        JsonElement second = await StatusAsync(app);
        Assert.AreEqual("off", second.GetProperty("relay").GetString());
        Assert.AreEqual(221.40, second.GetProperty("voltage").GetDouble());
        Assert.AreEqual(0.512, second.GetProperty("current").GetDouble());
        Assert.AreEqual(0.987, second.GetProperty("powerFactor").GetDouble());
        Assert.AreEqual(0.0, second.GetProperty("power").GetDouble());
        Assert.IsTrue(second.GetProperty("lastReportAt").GetDateTimeOffset() > firstReportAt);

        // 기존 수신 로그(device-server/R02-R5)는 그대로 남는다.
        await TestMqtt.WaitUntilAsync(() => TestMqtt.ReadLogs(LogDirectory).Contains($"Device message \"{operation}\"", StringComparison.Ordinal),
            "상태 보고 로그가 없다.");
        await app.StopAsync();
    }

    // C1: 상태 보고가 아닌 메시지의 요소는 상태에 반영하지 않는다.
    [TestMethod]
    public async Task NonReportMessage_DoesNotChangeState()
    {
        await using WebApplication app = await StartAsync();
        await using TestMqttDevice device = await TestMqttDevice.ConnectAsync(app, configuration, Mac);

        await device.SendAsync("tisyn", """[{"n":"/100/0/31","sv":"true"}]""", "timesync/json");

        JsonElement status = await StatusAsync(app);
        Assert.AreEqual("unknown", status.GetProperty("relay").GetString());
        Assert.AreEqual(JsonValueKind.Null, status.GetProperty("lastReportAt").ValueKind);
        await app.StopAsync();
    }

    // C2: 접속하면 온라인, 끊기면 오프라인이고, 같은 ClientId가 다시 접속해 이전 연결이 끊겨도 온라인으로 남는다.
    [TestMethod]
    public async Task Online_FollowsConnection_AndSurvivesTakeover()
    {
        await using WebApplication app = await StartAsync();
        await TestDevices.RegisterAsync(app, Mac);
        Assert.IsFalse((await StatusAsync(app)).GetProperty("online").GetBoolean());

        TestMqttDevice first = await TestMqttDevice.ConnectAsync(app, configuration, Mac);
        await WaitOnlineAsync(app, true);

        await using TestMqttDevice second = await TestMqttDevice.ConnectAsync(app, configuration, Mac);
        await second.ReportRelayAsync(true);
        await TestMqtt.WaitUntilAsync(() => !first.IsConnected, "이전 연결이 끊기지 않았다.");
        // 이전 연결의 끊김 처리가 늦게 와도 오프라인이 되지 않는지 잠시 본다.
        await Task.Delay(500);
        Assert.IsTrue((await StatusAsync(app)).GetProperty("online").GetBoolean());
        await first.DisposeAsync();
        Assert.IsTrue((await StatusAsync(app)).GetProperty("online").GetBoolean());

        await second.DisconnectAsync();
        await WaitOnlineAsync(app, false);
        await app.StopAsync();
    }

    // C2: 옛 연결의 끊김 이벤트가 새 접속 이벤트 뒤에 와도 온라인으로 남는다(이벤트 순서를 직접 만든다).
    [TestMethod]
    public void Disconnected_OfOldConnection_IsIgnored()
    {
        DeviceStateStore store = new DeviceStateStore(TimeProvider.System, NullLogger<DeviceStateStore>.Instance);
        store.Connected("DAWONDNS-" + Mac, "127.0.0.1:1000");
        store.Connected("DAWONDNS-" + Mac, "127.0.0.1:1001");

        store.Disconnected("DAWONDNS-" + Mac, "127.0.0.1:1000", MqttClientDisconnectType.NotClean);
        Assert.IsTrue(store.Get(Mac).Online);
        store.Disconnected("DAWONDNS-" + Mac, "127.0.0.1:1001", MqttClientDisconnectType.Takeover);
        Assert.IsTrue(store.Get(Mac).Online);

        store.Disconnected("DAWONDNS-" + Mac, "127.0.0.1:1001", MqttClientDisconnectType.Clean);
        Assert.IsFalse(store.Get(Mac).Online);
    }

    // C3: tisyn만 보낸 장비도 토픽을 기억해 제어 가능해진다. LWT 토픽도 장비의 메시지다(D021).
    [TestMethod]
    [DataRow("tisyn", "timesync/json")]
    [DataRow("offline", "lwt")]
    public async Task AnyDeviceMessage_RemembersTopic(string operation, string suffix)
    {
        await using WebApplication app = await StartAsync();
        await using TestMqttDevice device = await TestMqttDevice.ConnectAsync(app, configuration, Mac);
        await WaitOnlineAsync(app, true);
        Assert.IsFalse((await StatusAsync(app)).GetProperty("controllable").GetBoolean());

        await device.SendAsync(operation, "[]", suffix);

        Assert.IsTrue((await StatusAsync(app)).GetProperty("controllable").GetBoolean());
        (int status, _, string body) = await TestMqtt.SendHttpAsync(app, 80, "POST", $"/api/admin/devices/{Mac}/relay", """{"on":true}""");
        Assert.AreEqual(204, status, body);
        await device.FlushAsync();
        Assert.IsTrue(device.Messages.Any(m => m.Topic == device.DownlinkTopic && TestMqtt.Text(m).Contains("\"/100/0/31\"", StringComparison.Ordinal)));
        await app.StopAsync();
    }

    // 용어: 장치 키 안 mac의 위치와 대소문자를 가리지 않는다(D016).
    [TestMethod]
    [DataRow("DAWONDNS-B540_W-3a0000000501")]
    [DataRow("3A0000000501-B540_W-DAWONDNS")]
    public async Task DeviceKey_WithClientMacAnywhere_IsDeviceMessage(string deviceKey)
    {
        await using WebApplication app = await StartAsync();
        await using TestMqttDevice device = await TestMqttDevice.ConnectAsync(app, configuration, Mac);

        await device.SendRawAsync($"dwd.v1/{deviceKey}/iot-server/notify/json", """{"sid":"1","msg":{"o":"n","e":[{"n":"/100/0/31","sv":"true"}]}}""");

        Assert.AreEqual("on", (await StatusAsync(app)).GetProperty("relay").GetString());
        await app.StopAsync();
    }

    // 용어: 장치 키에 보낸 ClientId의 mac이 없으면 Warning을 남기고 반영하지 않는다. 기존 분배(수신 로그)는 그대로다.
    [TestMethod]
    public async Task DeviceKey_WithoutClientMac_IsWarnedAndIgnored()
    {
        const string otherMac = "3a0000000502";
        await using WebApplication app = await StartAsync();
        await TestDevices.RegisterAsync(app, otherMac);
        await using TestMqttDevice device = await TestMqttDevice.ConnectAsync(app, configuration, Mac);

        await device.SendRawAsync($"dwd.v1/DAWONDNS-B540_W-{otherMac}/iot-server/notify/json",
            """{"sid":"1","msg":{"o":"n","e":[{"n":"/100/0/31","sv":"true"}]}}""");

        foreach (string mac in new[] { Mac, otherMac })
        {
            JsonElement status = await StatusAsync(app, mac);
            Assert.AreEqual("unknown", status.GetProperty("relay").GetString());
            Assert.AreEqual(JsonValueKind.Null, status.GetProperty("lastReportAt").ValueKind);
            Assert.IsFalse(status.GetProperty("controllable").GetBoolean());
        }
        await TestMqtt.WaitUntilAsync(() =>
        {
            string log = TestMqtt.ReadLogs(LogDirectory);
            return log.Contains($"[Warning] ", StringComparison.Ordinal)
                && log.Contains($"Device message ignored for device state: clientId=\"DAWONDNS-{Mac}\"", StringComparison.Ordinal)
                && log.Contains("Device message \"n\"", StringComparison.Ordinal);
        }, "Warning 로그나 기존 수신 로그가 없다.");
        await app.StopAsync();
    }

    // C30: 릴레이 상태를 모르면 장비의 메시지 때 읽기를 보내고, 큐에 넣었으면 그 접속에서 다시 보내지 않으며, 다시 접속하면 다시 보낸다.
    [TestMethod]
    public async Task RelayRead_IsSentOncePerConnection()
    {
        await using WebApplication app = await StartAsync();
        TestMqttDevice first = await TestMqttDevice.ConnectAsync(app, configuration, Mac);
        await first.SendAsync("notify", """[{"n":"/100/0/1","sv":"220.00"}]""");
        await first.WaitForPayloadsAsync(1);
        Assert.AreEqual(first.DownlinkTopic, first.Messages[0].Topic);
        Assert.IsFalse(first.Messages[0].Retain);
        Assert.AreEqual(RelayReadJson, MessagePart(first.Payloads[0]));

        await WaitForLogAsync("Device command sent", "operation=\"r\"", 1);
        await first.SendAsync("notify", """[{"n":"/100/0/1","sv":"221.00"}]""");
        await first.SendAsync("notify", """[{"n":"/100/0/1","sv":"222.00"}]""");
        await Task.Delay(300);
        await first.FlushAsync();
        Assert.HasCount(1, first.Payloads);
        await first.DisposeAsync();
        await WaitOnlineAsync(app, false);

        await using TestMqttDevice second = await TestMqttDevice.ConnectAsync(app, configuration, Mac);
        await second.SendAsync("notify", """[{"n":"/100/0/1","sv":"223.00"}]""");
        await second.WaitForPayloadsAsync(1);
        Assert.AreEqual(RelayReadJson, MessagePart(second.Payloads[0]));

        // 장치의 응답은 상태 보고로 반영된다.
        await second.SendAsync("n", """[{"n":"/100/0/30","sv":"true"}]""");
        Assert.AreEqual("on", (await StatusAsync(app)).GetProperty("relay").GetString());
        await app.StopAsync();
    }

    // C30: 큐에 넣지 못하면(구독 없음) 다음 장비의 메시지 때 다시 보낸다.
    [TestMethod]
    public async Task RelayRead_NotEnqueued_IsRetriedOnNextMessage()
    {
        await using WebApplication app = await StartAsync();
        await using TestMqttDevice device = await TestMqttDevice.ConnectAsync(app, configuration, Mac, subscribe: false);
        await device.SendAsync("notify", """[{"n":"/100/0/1","sv":"220.00"}]""");
        await WaitForLogAsync("Device command not enqueued", "operation=\"r\"", 1);

        await device.SubscribeAsync();
        await device.SendAsync("notify", """[{"n":"/100/0/1","sv":"221.00"}]""");
        await device.WaitForPayloadsAsync(1);
        Assert.AreEqual(RelayReadJson, MessagePart(device.Payloads[0]));
        await app.StopAsync();
    }

    // C30: 릴레이 요소가 든 상태 보고에는 읽기를 보내지 않는다(C1 반영이 먼저, D019).
    [TestMethod]
    public async Task RelayRead_IsNotSent_WhenReportHasRelay()
    {
        await using WebApplication app = await StartAsync();
        await using TestMqttDevice device = await TestMqttDevice.ConnectAsync(app, configuration, Mac);
        await device.ReportRelayAsync(false);
        await device.SendAsync("notify", """[{"n":"/100/0/1","sv":"220.00"}]""");
        await Task.Delay(300);
        await device.FlushAsync();
        Assert.IsEmpty(device.Payloads);
        await app.StopAsync();
    }

    // C30: 이전 접속에서 시작한 읽기의 결과가 새 접속의 읽기 상태를 바꾸지 않는다(이벤트 순서를 직접 만든다).
    [TestMethod]
    public void RelayRead_ResultOfOldConnection_IsIgnored()
    {
        DeviceStateStore store = new DeviceStateStore(TimeProvider.System, NullLogger<DeviceStateStore>.Instance);
        DeviceTopic topic = new DeviceTopic("dwd", "DAWONDNS-B540_W-" + Mac, DeviceTopicKind.Uplink);
        string clientId = "DAWONDNS-" + Mac;

        store.Connected(clientId, "127.0.0.1:1000");
        RelayReadRequest? old = store.ApplyMessage(clientId, topic, null);
        Assert.IsNotNull(old);
        Assert.IsNull(store.ApplyMessage(clientId, topic, null), "보내는 중에 겹쳐 보낸다.");

        store.Connected(clientId, "127.0.0.1:1001");
        store.CompleteRelayRead(old, enqueued: true);
        RelayReadRequest? current = store.ApplyMessage(clientId, topic, null);
        Assert.IsNotNull(current, "새 접속에서 읽기를 보내지 않는다.");

        store.CompleteRelayRead(current, enqueued: false);
        RelayReadRequest? retry = store.ApplyMessage(clientId, topic, null);
        Assert.IsNotNull(retry);
        store.CompleteRelayRead(retry, enqueued: true);
        Assert.IsNull(store.ApplyMessage(clientId, topic, null));
    }

    private Task WaitForLogAsync(string start, string part, int count)
        => TestMqtt.WaitUntilAsync(() => TestMqtt.ReadLogs(LogDirectory).Split('\n')
            .Count(l => l.Contains(start, StringComparison.Ordinal) && l.Contains(part, StringComparison.Ordinal)) >= count,
            $"로그가 없다: {start} … {part}");
}
