using Biz.Bizadm.PowerManager.FakeDevice.App.Mqtt;
using Biz.Bizadm.PowerManager.FakeDevice.App.Tests.Fakes;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System.Text;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Tests;

[TestClass]
public sealed class RunnerMqttTests
{
    private const string Up = "dwd.v1/" + TestHarness.DeviceKey + "/iot-server/";
    private const string Down = "dwd.v1/iot-server/" + TestHarness.DeviceKey;
    private const string Filter = Down + "/#";

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

    private FakeDeviceOptions Options() => TestHarness.Options(http, broker, recordDirectory);

    private static async Task SubscribedAsync(RunnerSession session)
        => await TestHarness.WaitUntilAsync(() => session.Records().Of("mqtt", "suback").Any(), "SUBACK record");

    private List<FakeDawonBroker.Publish> ClientPublishes() => broker.Publishes.Where(p => p.ClientId == TestHarness.ClientId).ToList();

    // ---------- C4 ----------

    [TestMethod]
    public async Task Connect_DefaultValues()
    {
        await using RunnerSession session = RunnerSession.Start(Options());
        await SubscribedAsync(session);

        FakeDawonBroker.Connection connection = broker.Connections.Single();
        Assert.AreEqual(MqttProtocolVersion.V310, connection.ProtocolVersion);
        Assert.AreEqual(TestHarness.ClientId, connection.ClientId);
        Assert.AreEqual(TestHarness.DeviceKey, connection.UserName);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(TestHarness.TestKey), connection.Password);
        Assert.AreEqual(60, connection.KeepAlive);
        Assert.IsTrue(connection.CleanSession);

        List<JsonElement> records = session.Records();
        JsonElement connect = records.Of("mqtt", "connect").Single();
        Assert.AreEqual(TestHarness.ClientId, connect.GetProperty("clientId").GetString());
        Assert.AreEqual(TestHarness.DeviceKey, connect.GetProperty("userName").GetString());
        Assert.IsTrue(connect.GetProperty("hasPassword").GetBoolean());
        Assert.AreEqual(60, connect.GetProperty("keepAlive").GetInt32());
        Assert.IsTrue(connect.GetProperty("cleanSession").GetBoolean());
        Assert.AreEqual("/lwt", connect.GetProperty("will").GetProperty("topic").GetString());
        Assert.AreEqual(0, records.Of("mqtt", "connack").Single().GetProperty("returnCode").GetInt32());
    }

    [TestMethod]
    public async Task Connect_ConfiguredValues()
    {
        await using RunnerSession session = RunnerSession.Start(Options() with
        {
            ClientId = "custom-client",
            UserName = "custom-user",
            Password = TestHarness.TestPassword,
            KeepAliveSeconds = 30,
        });
        await SubscribedAsync(session);

        FakeDawonBroker.Connection connection = broker.Connections.Single();
        Assert.AreEqual("custom-client", connection.ClientId);
        Assert.AreEqual("custom-user", connection.UserName);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(TestHarness.TestPassword), connection.Password);
        Assert.AreEqual(30, connection.KeepAlive);
    }

    [TestMethod]
    [DataRow(4)]
    [DataRow(5)]
    public async Task Connect_Rejected_RecordsConnAckAndFails(int returnCode)
    {
        broker.ConnectResult = returnCode == 4 ? MqttConnectReasonCode.BadUserNameOrPassword : MqttConnectReasonCode.NotAuthorized;
        await using RunnerSession session = RunnerSession.Start(Options());

        Assert.IsFalse(await session.ResultAsync());

        Assert.AreEqual(returnCode, session.Records().Of("mqtt", "connack").Single().GetProperty("returnCode").GetInt32());
        StringAssert.Contains(session.Console, $"MQTT CONNACK {returnCode}");
        Assert.IsTrue(broker.Subscribes.IsEmpty);
    }

    [TestMethod]
    [DataRow("closed-port")]
    [DataRow("connack-delay")]
    public async Task Connect_Failure_RecordsErrorAndFails(string scenario)
    {
        FakeDeviceOptions options = Options();
        if (scenario == "closed-port")
            options = options with { MqttServerPort = TestHarness.FreeTcpPort() };
        else
            broker.ConnectDelay = TimeSpan.FromSeconds(5);
        await using RunnerSession session = RunnerSession.Start(options);

        Assert.IsFalse(await session.ResultAsync());

        List<JsonElement> records = session.Records();
        Assert.IsFalse(records.Of("mqtt", "connack").Any());
        JsonElement error = records.Of("mqtt", "error").Single();
        Assert.AreEqual($"127.0.0.1:{options.MqttServerPort}", error.GetProperty("target").GetString());
        if (scenario == "connack-delay")
            Assert.AreEqual("timeout", error.GetProperty("error").GetString());
        StringAssert.Contains(session.Console, "MQTT connect error");
    }

    // ---------- C5 ----------

    [TestMethod]
    public async Task Subscribe_FilterQos0_Recorded()
    {
        await using RunnerSession session = RunnerSession.Start(Options());
        await SubscribedAsync(session);

        FakeDawonBroker.Subscribe subscribe = broker.Subscribes.Single();
        Assert.AreEqual(Filter, subscribe.Filters.Single().Topic);
        Assert.AreEqual(MqttQualityOfServiceLevel.AtMostOnce, subscribe.Filters.Single().QoS);

        List<JsonElement> records = session.Records();
        JsonElement filter = records.Of("mqtt", "subscribe").Single().GetProperty("filters").EnumerateArray().Single();
        Assert.AreEqual(Filter, filter.GetProperty("topic").GetString());
        Assert.AreEqual(0, filter.GetProperty("qos").GetInt32());
        Assert.AreEqual(0, records.Of("mqtt", "suback").Single().GetProperty("results").EnumerateArray().Single().GetInt32());
    }

    [TestMethod]
    public async Task Subscribe_FailureValue_RecordsAndContinues()
    {
        broker.SubscribeResults[Filter] = MqttSubscribeReasonCode.UnspecifiedError;
        await using RunnerSession session = RunnerSession.Start(Options());

        await TestHarness.WaitUntilAsync(() => ClientPublishes().Any(p => p.Topic == Up + "json"), "rg after SUBACK failure");

        int result = session.Records().Of("mqtt", "suback").Single().GetProperty("results").EnumerateArray().Single().GetInt32();
        Assert.IsTrue(result > 2, $"failure value {result}");
        StringAssert.Contains(session.Console, "MQTT SUBACK failure");
    }

    [TestMethod]
    public async Task Subscribe_NoSubAck_FailsAndDisconnects()
    {
        broker.SubscribeDelay = TimeSpan.FromSeconds(5);
        await using RunnerSession session = RunnerSession.Start(Options());

        Assert.IsFalse(await session.ResultAsync());

        List<JsonElement> records = session.Records();
        Assert.IsFalse(records.Of("mqtt", "suback").Any());
        Assert.AreEqual("timeout", records.Of("mqtt", "error").Single().GetProperty("error").GetString());
        Assert.IsTrue(records.Of("mqtt", "disconnect").Single().GetProperty("clean").GetBoolean());
        Assert.IsTrue(ClientPublishes().Count == 0);
    }

    [TestMethod]
    public async Task Subscribe_ServerDisconnects_RecordsUnclean()
    {
        broker.SubscribeDelay = TimeSpan.FromSeconds(5);
        await using RunnerSession session = RunnerSession.Start(Options() with { MqttTimeout = TimeSpan.FromSeconds(4) });
        await TestHarness.WaitUntilAsync(() => !broker.Subscribes.IsEmpty, "SUBSCRIBE received");

        await broker.DisconnectClientAsync(TestHarness.ClientId);

        Assert.IsFalse(await session.ResultAsync());
        JsonElement disconnect = session.Records().Of("mqtt", "disconnect").Single();
        Assert.AreEqual("dawon-to-device", disconnect.GetProperty("direction").GetString());
        Assert.IsFalse(disconnect.GetProperty("clean").GetBoolean());
    }

    // ---------- C6·C9 ----------

    [TestMethod]
    public async Task FixedPublishes_OrderTopicQosRetain()
    {
        await using RunnerSession session = RunnerSession.Start(Options() with { NotifyInterval = TimeSpan.FromSeconds(30) });

        await TestHarness.WaitUntilAsync(() => ClientPublishes().Count >= 3, "three fixed publishes");

        List<FakeDawonBroker.Publish> publishes = ClientPublishes();
        CollectionAssert.AreEqual(new[] { Up + "json", Up + "timesync/json", Up + "notify/json" }, publishes.Select(p => p.Topic).ToArray());
        CollectionAssert.AreEqual(DevicePayloads.Register("DAWONDNS", "B540_W", TestHarness.Mac, "1.01.36"), publishes[0].Payload);
        CollectionAssert.AreEqual(DevicePayloads.TimeSync(), publishes[1].Payload);
        CollectionAssert.AreEqual(DevicePayloads.Notify([new NotifyElement("/100/0/31", "false")]), publishes[2].Payload);
        Assert.IsTrue(publishes.All(p => p.QoS == MqttQualityOfServiceLevel.AtMostOnce && !p.Retain));

        await TestHarness.WaitUntilAsync(() => session.Records().Of("mqtt", "publish", "device-to-dawon").Count() >= 3, "publish records");
        JsonElement record = session.Records().Of("mqtt", "publish", "device-to-dawon").First();
        Assert.AreEqual(Up + "json", record.GetProperty("topic").GetString());
        Assert.AreEqual(Encoding.UTF8.GetString(publishes[0].Payload), record.GetProperty("payload").GetString());
        Assert.AreEqual(0, record.GetProperty("qos").GetInt32());
        Assert.IsFalse(record.GetProperty("retain").GetBoolean());
        Assert.AreEqual(TestHarness.ClientId, record.GetProperty("clientId").GetString());
    }

    [TestMethod]
    public async Task FixedPublishes_Disabled()
    {
        await using RunnerSession session = RunnerSession.Start(Options() with { SendRegister = false, SendTimeSync = false });
        await SubscribedAsync(session);

        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.AreEqual(0, ClientPublishes().Count);
    }

    [TestMethod]
    public async Task Periodic_TimeSyncAndNotify()
    {
        await using RunnerSession session = RunnerSession.Start(Options() with
        {
            TimeSyncInterval = TimeSpan.FromSeconds(1),
            NotifyInterval = TimeSpan.FromSeconds(1),
        });
        await SubscribedAsync(session);

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        List<FakeDawonBroker.Publish> publishes = ClientPublishes();
        Assert.IsTrue(publishes.Count(p => p.Topic == Up + "timesync/json") >= 2, "tisyn twice");
        Assert.IsTrue(publishes.Count(p => p.Topic == Up + "notify/json") >= 2, "n twice");
    }

    // ---------- C7 ----------

    [TestMethod]
    public async Task Console_PubUpUnknownQuit()
    {
        await using RunnerSession session = RunnerSession.Start(Options() with { SendRegister = false, SendTimeSync = false });
        await SubscribedAsync(session);

        session.Input.Send("pub any/topic/x not json payload");
        session.Input.Send("up w/json {\"sid\":\"9\"}");
        session.Input.Send("unknown command");
        await TestHarness.WaitUntilAsync(() => ClientPublishes().Count >= 2, "two console publishes");
        await TestHarness.WaitUntilAsync(() => session.Console.Contains("Commands:"), "usage");

        List<FakeDawonBroker.Publish> publishes = ClientPublishes();
        Assert.AreEqual("any/topic/x", publishes[0].Topic);
        Assert.AreEqual("not json payload", Encoding.UTF8.GetString(publishes[0].Payload));
        Assert.AreEqual(Up + "w/json", publishes[1].Topic);
        Assert.AreEqual("{\"sid\":\"9\"}", Encoding.UTF8.GetString(publishes[1].Payload));

        session.Input.Send("quit");
        Assert.IsTrue(await session.ResultAsync());
    }

    // ---------- C8 ----------

    [TestMethod]
    public async Task Received_RecordedAndPrinted_NoResponse()
    {
        await using RunnerSession session = RunnerSession.Start(Options());
        await TestHarness.WaitUntilAsync(() => ClientPublishes().Count >= 2, "fixed publishes");

        await broker.PublishAsync(Down + "/timesync/json", "{\"sid\":\"0\",\"msg\":{\"o\":\"r\",\"e\":[]}}");
        await TestHarness.WaitUntilAsync(() => session.Records().Of("mqtt", "publish", "dawon-to-device").Any(), "received record");
        int before = ClientPublishes().Count;

        JsonElement record = session.Records().Of("mqtt", "publish", "dawon-to-device").Single();
        Assert.AreEqual(Down + "/timesync/json", record.GetProperty("topic").GetString());
        Assert.AreEqual("{\"sid\":\"0\",\"msg\":{\"o\":\"r\",\"e\":[]}}", record.GetProperty("payload").GetString());
        Assert.AreEqual(0, record.GetProperty("qos").GetInt32());
        Assert.IsFalse(record.GetProperty("retain").GetBoolean());
        StringAssert.Contains(session.Console, "<< " + Down + "/timesync/json {\"sid\":\"0\"");

        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.AreEqual(before, ClientPublishes().Count, "no response publish");
    }

    // ---------- C11 ----------

    [TestMethod]
    [DataRow("quit")]
    [DataRow("stop-token")]
    public async Task Stop_SendsDisconnect_Succeeds(string how)
    {
        await using RunnerSession session = RunnerSession.Start(Options());
        await SubscribedAsync(session);

        if (how == "quit")
            session.Input.Send("exit");
        else
            session.Stop.Cancel();

        Assert.IsTrue(await session.ResultAsync());
        await TestHarness.WaitUntilAsync(() => !broker.Disconnects.IsEmpty, "broker disconnect");
        Assert.AreEqual(MqttClientDisconnectType.Clean, broker.Disconnects.Single().Type);
        JsonElement disconnect = session.Records().Of("mqtt", "disconnect").Single();
        Assert.AreEqual("device-to-dawon", disconnect.GetProperty("direction").GetString());
        Assert.IsTrue(disconnect.GetProperty("clean").GetBoolean());
    }

    [TestMethod]
    public async Task ServerDisconnect_RecordsUncleanAndFails()
    {
        await using RunnerSession session = RunnerSession.Start(Options());
        await SubscribedAsync(session);

        await broker.DisconnectClientAsync(TestHarness.ClientId);

        Assert.IsFalse(await session.ResultAsync());
        JsonElement disconnect = session.Records().Of("mqtt", "disconnect").Single();
        Assert.AreEqual("dawon-to-device", disconnect.GetProperty("direction").GetString());
        Assert.IsFalse(disconnect.GetProperty("clean").GetBoolean());
    }

    // ---------- C10 ----------

    [TestMethod]
    public async Task Secrets_NotInRecordsOrConsole()
    {
        await using RunnerSession session = RunnerSession.Start(Options() with { Password = TestHarness.TestPassword });
        await SubscribedAsync(session);
        session.Input.Send("quit");
        Assert.IsTrue(await session.ResultAsync());

        string raw = TestHarness.RawRecords(recordDirectory);
        foreach (string secret in new[] { TestHarness.TestKey, TestHarness.TestPassword })
        {
            Assert.IsFalse(raw.Contains(secret, StringComparison.Ordinal), "secret in records");
            Assert.IsFalse(session.Console.Contains(secret, StringComparison.Ordinal), "secret in console");
        }
    }
}
