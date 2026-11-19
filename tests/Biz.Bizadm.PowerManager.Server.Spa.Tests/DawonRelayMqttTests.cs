using Biz.Bizadm.PowerManager.Core.Mqtt;
using Biz.Bizadm.PowerManager.Server.Spa.Relay;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

[TestClass]
public sealed class DawonRelayMqttTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private const string ClientId = "DAWONDNS-aabbccddeeff";
    private const string DeviceKey = "DAWONDNS-B540_W-aabbccddeeff";
    private const string UplinkTopic = "dwd.v1/" + DeviceKey + "/iot-server/json";
    private const string DownlinkTopic = "dwd.v1/iot-server/" + DeviceKey;
    private const string RegisterJson = """{"sid":"0","msg":{"o":"rg","e":[{"n":"/3/0/3","sv":"1.01.34"}]}}""";
    private const string Password = "RelayTestPassword42";

    private FakeDawonBroker broker = null!;
    private string tempRoot = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.RelayMqtt.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "ClientApp"));
        broker = await FakeDawonBroker.StartAsync();
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        await broker.DisposeAsync();
        TestApp.DeleteTempRoot(tempRoot);
    }

    private sealed class RecordingHandler : IDeviceMessageHandler
    {
        public ConcurrentQueue<string> Calls { get; } = new();
        public TaskCompletionSource Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RegisterAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct)
        {
            Calls.Enqueue("rg");
            Registered.TrySetResult();
            return Task.CompletedTask;
        }

        public Task DeregisterAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct) { Calls.Enqueue("drg"); return Task.CompletedTask; }
        public Task TimeSyncAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct) { Calls.Enqueue("tisyn"); return Task.CompletedTask; }
        public Task NotifyAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct) { Calls.Enqueue("notify"); return Task.CompletedTask; }
        public Task OfflineAsync(DeviceTopic topic, CancellationToken ct) { Calls.Enqueue("offline"); return Task.CompletedTask; }
    }

    private AppConfiguration Configuration(ushort? mqttServerPort = null, bool skipCertificateValidation = true)
    {
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        configuration.DawonRelay = new DawonRelayConfiguration
        {
            Enabled = true,
            ApiServerHost = "localhost",
            ApiServerPort = TestApp.FreeTcpPort(),
            MqttServerHost = "localhost",
            MqttServerPort = mqttServerPort ?? broker.Port,
            RecordDirectory = "dawon-relay",
            SkipCertificateValidation = skipCertificateValidation,
        };
        return configuration;
    }

    private Task<WebApplication> StartAsync(AppConfiguration configuration, RecordingHandler? handler = null)
        => TestApp.StartAsync(configuration, Path.Combine(tempRoot, "log"),
            builder => builder.Services.Replace(ServiceDescriptor.Singleton<IDeviceMessageHandler>(handler ?? new RecordingHandler())));

    /// <summary>
    /// 중계 서버(비TLS MQTT 포트)에 붙는 3.1 클라이언트 옵션.
    /// </summary>
    private static MqttClientOptionsBuilder RelayOptions(AppConfiguration configuration, string clientId = ClientId)
        => new MqttClientOptionsBuilder()
            .WithTcpServer("127.0.0.1", configuration.MqttPort!.Value)
            .WithClientId(clientId)
            .WithProtocolVersion(MqttProtocolVersion.V310)
            .WithTimeout(Timeout);

    /// <summary>
    /// 가짜 다원 브로커에 직접 붙는 TLS 클라이언트 옵션.
    /// </summary>
    private MqttClientOptionsBuilder DirectOptions(string clientId)
        => new MqttClientOptionsBuilder()
            .WithTcpServer("127.0.0.1", broker.Port)
            .WithClientId(clientId)
            .WithProtocolVersion(MqttProtocolVersion.V310)
            .WithTlsOptions(tls => tls.UseTls().WithTargetHost("localhost").WithSslProtocols(SslProtocols.Tls12).WithCertificateValidationHandler(_ => true))
            .WithTimeout(Timeout);

    private sealed class Receiver
    {
        public ConcurrentQueue<MqttApplicationMessage> Messages { get; } = new();

        public IMqttClient Attach(IMqttClient client)
        {
            client.ApplicationMessageReceivedAsync += e =>
            {
                Messages.Enqueue(e.ApplicationMessage);
                return Task.CompletedTask;
            };
            return client;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string message)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }
        Assert.Fail(message);
    }

    private List<JsonElement> ReadRecords()
    {
        string directory = Path.Combine(tempRoot, "dawon-relay");
        if (!Directory.Exists(directory))
            return [];
        List<JsonElement> records = [];
        foreach (string file in Directory.GetFiles(directory, "dawon-relay-*.jsonl"))
        {
            using FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new StreamReader(stream);
            while (reader.ReadLine() is string line)
                records.Add(JsonDocument.Parse(line).RootElement.Clone());
        }
        return records;
    }

    private string ReadAllText(string folder)
    {
        string directory = Path.Combine(tempRoot, folder);
        if (!Directory.Exists(directory))
            return string.Empty;
        StringBuilder builder = new StringBuilder();
        foreach (string file in Directory.GetFiles(directory))
        {
            using FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new StreamReader(stream);
            builder.Append(reader.ReadToEnd());
        }
        return builder.ToString();
    }

    private static string Text(MqttApplicationMessage m) => Encoding.UTF8.GetString(m.Payload);

    /// <summary>
    /// 이벤트 인자 생성용 빈 채널 어댑터(호출되지 않는다).
    /// </summary>
    public class NullProxy : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) => null;
    }

    // ---------- 가짜 브로커 전제 ----------

    // T017: 가짜 브로커가 받은 필터 순서대로 지정한 SUBACK 결과를 돌려준다(MQTTnet의 QoS 정렬을 되돌림).
    [TestMethod]
    public async Task FakeBroker_SubAck_FollowsReceivedOrder()
    {
        broker.SubscribeResults["a/0"] = MqttSubscribeReasonCode.GrantedQoS1;
        broker.SubscribeResults["b/1"] = MqttSubscribeReasonCode.UnspecifiedError;
        using IMqttClient client = new MqttClientFactory().CreateMqttClient();
        await client.ConnectAsync(DirectOptions("precheck").Build());

        MqttClientSubscribeResult result = await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter("a/0", MqttQualityOfServiceLevel.AtMostOnce)
            .WithTopicFilter("b/1", MqttQualityOfServiceLevel.AtLeastOnce)
            .Build());

        CollectionAssert.AreEqual(
            new[] { MqttClientSubscribeResultCode.GrantedQoS1, MqttClientSubscribeResultCode.UnspecifiedError },
            result.Items.Select(i => i.ResultCode).ToArray());
        await client.DisconnectAsync();
    }

    // T018: MQTTnet 클라이언트를 DisconnectAsync 없이 Dispose하면 DISCONNECT 없이 끊겨 브로커가 Will을 발행한다.
    [TestMethod]
    public async Task ClientDispose_WithoutDisconnect_PublishesWill()
    {
        IMqttClient client = new MqttClientFactory().CreateMqttClient();
        await client.ConnectAsync(DirectOptions("will-check")
            .WithWillTopic("will/topic").WithWillPayload("offline").Build());
        Assert.IsTrue(broker.Connections.Single().Will is { Topic: "will/topic" }, "가짜 브로커가 Will을 읽지 못했다.");

        client.Dispose();

        await WaitUntilAsync(() => broker.Publishes.Any(p => p.ClientId == "will-check" && p.Topic == "will/topic"
            && Encoding.UTF8.GetString(p.Payload) == "offline"), "Will이 발행되지 않았다.");
        Assert.AreEqual(MqttClientDisconnectType.NotClean, broker.Disconnects.Single().Type);
    }

    // ---------- C5 접속 ----------

    // C5·C9·C11: 자격 증명·keepalive·clean session·Will을 그대로 다원에 쓰고, connect·connack을 기록하며 비밀번호는 남기지 않는다.
    [TestMethod]
    public async Task Connect_ForwardsCredentialsKeepAliveCleanSessionAndWill()
    {
        AppConfiguration configuration = Configuration();
        await using (WebApplication app = await StartAsync(configuration))
        {
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            MqttClientConnectResult result = await client.ConnectAsync(RelayOptions(configuration)
                .WithCredentials(DeviceKey, Password)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(45))
                .WithCleanSession(false)
                .WithWillTopic("/lwt").WithWillPayload("offline").WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).WithWillRetain(true)
                .Build());

            Assert.AreEqual(MqttClientConnectResultCode.Success, result.ResultCode);
            FakeDawonBroker.Connection connection = broker.Connections.Single();
            Assert.AreEqual(ClientId, connection.ClientId);
            Assert.AreEqual(DeviceKey, connection.UserName);
            Assert.AreEqual(Password, Encoding.UTF8.GetString(connection.Password!));
            Assert.AreEqual((ushort)45, connection.KeepAlive);
            Assert.IsFalse(connection.CleanSession);
            Assert.AreEqual("/lwt", connection.Will!.Topic);
            Assert.AreEqual("offline", Encoding.UTF8.GetString(connection.Will.Payload));
            Assert.AreEqual(MqttQualityOfServiceLevel.AtLeastOnce, connection.Will.QoS);
            Assert.IsTrue(connection.Will.Retain);

            await client.DisconnectAsync();
            await app.StopAsync();
        }

        List<JsonElement> records = ReadRecords();
        JsonElement connect = records.Single(r => r.GetProperty("kind").GetString() == "connect");
        Assert.AreEqual("device-to-dawon", connect.GetProperty("direction").GetString());
        Assert.AreEqual(ClientId, connect.GetProperty("clientId").GetString());
        Assert.AreEqual(DeviceKey, connect.GetProperty("userName").GetString());
        Assert.IsTrue(connect.GetProperty("hasPassword").GetBoolean());
        Assert.AreEqual(45, connect.GetProperty("keepAlive").GetInt32());
        Assert.IsFalse(connect.GetProperty("cleanSession").GetBoolean());
        Assert.AreEqual("/lwt", connect.GetProperty("will").GetProperty("topic").GetString());
        Assert.AreEqual("offline", connect.GetProperty("will").GetProperty("payload").GetString());
        Assert.AreEqual(0, records.Single(r => r.GetProperty("kind").GetString() == "connack").GetProperty("returnCode").GetInt32());
        Assert.IsTrue(records.Any(r => r.GetProperty("kind").GetString() == "disconnect" && r.GetProperty("clean").GetBoolean()));
        Assert.DoesNotContain(Password, ReadAllText("dawon-relay") + ReadAllText("log"));
    }

    // C5: 다원이 거부하면 그 반환 코드로 클라이언트 접속을 거부한다.
    [TestMethod]
    [DataRow(MqttConnectReasonCode.BadUserNameOrPassword, MqttClientConnectResultCode.BadUserNameOrPassword, 4)]
    [DataRow(MqttConnectReasonCode.NotAuthorized, MqttClientConnectResultCode.NotAuthorized, 5)]
    public async Task Connect_DawonRejects_ForwardsReturnCode(MqttConnectReasonCode dawon, MqttClientConnectResultCode expected, int returnCode)
    {
        broker.ConnectResult = _ => dawon;
        AppConfiguration configuration = Configuration();
        await using (WebApplication app = await StartAsync(configuration))
        {
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            MqttClientConnectResult result = await client.ConnectAsync(RelayOptions(configuration).Build());

            Assert.AreEqual(expected, result.ResultCode);
            Assert.IsFalse(client.IsConnected);
            await app.StopAsync();
        }
        Assert.AreEqual(returnCode, ReadRecords().Single(r => r.GetProperty("kind").GetString() == "connack").GetProperty("returnCode").GetInt32());
    }

    // C5: 다원에 연결하지 못하면 서버 사용 불가(3)로 거부하고 error를 기록한다.
    [TestMethod]
    public async Task Connect_DawonUnreachable_RejectsServerUnavailable()
    {
        AppConfiguration configuration = Configuration(mqttServerPort: TestApp.FreeTcpPort());
        await using (WebApplication app = await StartAsync(configuration))
        {
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            MqttClientConnectResult result = await client.ConnectAsync(RelayOptions(configuration).Build());

            Assert.AreEqual(MqttClientConnectResultCode.ServerUnavailable, result.ResultCode);
            await app.StopAsync();
        }
        Assert.AreEqual("mqtt", ReadRecords().Single(r => r.GetProperty("kind").GetString() == "error").GetProperty("channel").GetString());
    }

    // C1·C5: 인증서 검증 기본값(켜짐)에서는 자체 서명 인증서라 연결 실패 경로(3)다.
    [TestMethod]
    public async Task Connect_CertificateValidationDefault_RejectsServerUnavailable()
    {
        AppConfiguration configuration = Configuration(skipCertificateValidation: false);
        await using (WebApplication app = await StartAsync(configuration))
        {
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            MqttClientConnectResult result = await client.ConnectAsync(RelayOptions(configuration).Build());

            Assert.AreEqual(MqttClientConnectResultCode.ServerUnavailable, result.ResultCode);
            Assert.IsEmpty(broker.Connections);
            await app.StopAsync();
        }
        Assert.IsTrue(ReadRecords().Any(r => r.GetProperty("kind").GetString() == "error"));
    }

    // C5: Will 읽기(MQTTnet 비공개 필드). 패키지를 올려 필드가 사라지면 이 테스트가 실패한다.
    [TestMethod]
    public void ConnectPacketReader_ReadsWill()
    {
        MqttConnectPacket packet = new MqttConnectPacket
        {
            ClientId = "c", WillFlag = true, WillTopic = "w/t", WillMessage = [1, 2], WillQoS = MqttQualityOfServiceLevel.ExactlyOnce, WillRetain = true,
        };
        MQTTnet.Adapter.IMqttChannelAdapter adapter = System.Reflection.DispatchProxy.Create<MQTTnet.Adapter.IMqttChannelAdapter, NullProxy>();
        ValidatingConnectionEventArgs e = (ValidatingConnectionEventArgs)Activator.CreateInstance(typeof(ValidatingConnectionEventArgs),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null,
            [packet, adapter, new Dictionary<object, object>(), CancellationToken.None], null)!;

        Assert.IsTrue(MqttConnectPacketReader.TryReadWill(e, out MqttConnectPacketReader.Will? will));
        Assert.AreEqual(new MqttConnectPacketReader.Will("w/t", [1, 2], MqttQualityOfServiceLevel.ExactlyOnce, true).Topic, will!.Topic);
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, will.Payload);
        Assert.AreEqual(MqttQualityOfServiceLevel.ExactlyOnce, will.QoS);
        Assert.IsTrue(will.Retain);
    }

    // ---------- C6 구독 ----------

    // C6·C9: 다원 허용 QoS(낮춘 값)와 실패 값을 그대로 돌려주고 subscribe·suback을 기록한다.
    [TestMethod]
    public async Task Subscribe_ReturnsDawonGrantedQoSAndFailure()
    {
        broker.SubscribeResults["s/low"] = MqttSubscribeReasonCode.GrantedQoS1;
        broker.SubscribeResults["s/fail"] = MqttSubscribeReasonCode.UnspecifiedError;
        AppConfiguration configuration = Configuration();
        await using (WebApplication app = await StartAsync(configuration))
        {
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(RelayOptions(configuration).Build());

            MqttClientSubscribeResult low = await client.SubscribeAsync("s/low", MqttQualityOfServiceLevel.ExactlyOnce);
            MqttClientSubscribeResult fail = await client.SubscribeAsync("s/fail", MqttQualityOfServiceLevel.AtLeastOnce);

            Assert.AreEqual(MqttClientSubscribeResultCode.GrantedQoS1, low.Items.Single().ResultCode);
            Assert.AreEqual(MqttClientSubscribeResultCode.UnspecifiedError, fail.Items.Single().ResultCode);
            await client.DisconnectAsync();
            await app.StopAsync();
        }

        List<JsonElement> records = ReadRecords();
        JsonElement subscribe = records.First(r => r.GetProperty("kind").GetString() == "subscribe");
        Assert.AreEqual("s/low", subscribe.GetProperty("filters")[0].GetProperty("topic").GetString());
        Assert.AreEqual(2, subscribe.GetProperty("filters")[0].GetProperty("qos").GetInt32());
        CollectionAssert.AreEqual(new[] { 1, 128 },
            records.Where(r => r.GetProperty("kind").GetString() == "suback").Select(r => r.GetProperty("results")[0].GetInt32()).ToArray());
    }

    // C6: QoS가 다른 다중 필터 SUBSCRIBE는 다원에 한 번에 같은 순서로 가고, SUBACK은 원래 필터 순서로 돌아온다.
    [TestMethod]
    public async Task Subscribe_MultipleFilters_KeepsOrder()
    {
        broker.SubscribeResults["m/a"] = MqttSubscribeReasonCode.GrantedQoS1;
        broker.SubscribeResults["m/b"] = MqttSubscribeReasonCode.UnspecifiedError;
        AppConfiguration configuration = Configuration();
        await using WebApplication app = await StartAsync(configuration);
        using IMqttClient client = new MqttClientFactory().CreateMqttClient();
        await client.ConnectAsync(RelayOptions(configuration).Build());

        MqttClientSubscribeResult result = await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter("m/a", MqttQualityOfServiceLevel.AtMostOnce)
            .WithTopicFilter("m/b", MqttQualityOfServiceLevel.AtLeastOnce)
            .Build());

        FakeDawonBroker.Subscribe received = broker.Subscribes.Single();
        CollectionAssert.AreEqual(new[] { "m/a", "m/b" }, received.Filters.Select(f => f.Topic).ToArray());
        CollectionAssert.AreEqual(
            new[] { MqttClientSubscribeResultCode.GrantedQoS1, MqttClientSubscribeResultCode.UnspecifiedError },
            result.Items.Select(i => i.ResultCode).ToArray());
        await client.DisconnectAsync();
        await app.StopAsync();
    }

    // C6: 다원이 SUBACK 직후 보내는 retained 메시지가 빠지지 않고 retain 1로 전달된다(20회). 로컬 retained에는 남지 않는다.
    [TestMethod]
    public async Task Subscribe_RetainedRightAfterSubAck_IsDelivered()
    {
        AppConfiguration configuration = Configuration();
        await using WebApplication app = await StartAsync(configuration);
        for (int i = 0; i < 20; i++)
        {
            await broker.PublishAsync("r/topic", "v" + i, MqttQualityOfServiceLevel.AtLeastOnce, retain: true);
            Receiver receiver = new Receiver();
            using IMqttClient client = receiver.Attach(new MqttClientFactory().CreateMqttClient());
            await client.ConnectAsync(RelayOptions(configuration, "retained-" + i).Build());

            await client.SubscribeAsync("r/topic", MqttQualityOfServiceLevel.AtLeastOnce);

            await WaitUntilAsync(() => receiver.Messages.Any(m => Text(m) == "v" + i), $"{i}회차 retained 메시지가 오지 않았다.");
            Assert.IsTrue(receiver.Messages.Single().Retain, $"{i}회차 retain이 보존되지 않았다.");
            await client.DisconnectAsync();
        }

        // 다원 retained를 지운 뒤 새로 구독하면 아무것도 오지 않는다(로컬 브로커에 retained가 남지 않았다).
        await broker.PublishAsync("r/topic", "", MqttQualityOfServiceLevel.AtLeastOnce, retain: true);
        Receiver after = new Receiver();
        using IMqttClient check = after.Attach(new MqttClientFactory().CreateMqttClient());
        await check.ConnectAsync(RelayOptions(configuration, "retained-check").Build());
        await check.SubscribeAsync("r/topic", MqttQualityOfServiceLevel.AtLeastOnce);
        await Task.Delay(1000);
        Assert.IsEmpty(after.Messages);
        await check.DisconnectAsync();
        await app.StopAsync();
    }

    // C6: SUBACK을 기다리지 않고 SUBSCRIBE 둘(둘째는 필터 둘)을 보내도 둘째의 두 필터가 모두 다원에 구독되고 retained가 빠지지 않는다(20회).
    [TestMethod]
    public async Task Subscribe_Consecutive_DeliversAllRetained()
    {
        AppConfiguration configuration = Configuration();
        await using WebApplication app = await StartAsync(configuration);
        for (int i = 0; i < 20; i++)
        {
            await broker.PublishAsync($"y{i}/1", "a" + i, MqttQualityOfServiceLevel.AtLeastOnce, retain: true);
            await broker.PublishAsync($"y{i}/2", "b" + i, MqttQualityOfServiceLevel.AtLeastOnce, retain: true);
            Receiver receiver = new Receiver();
            using IMqttClient client = receiver.Attach(new MqttClientFactory().CreateMqttClient());
            string clientId = "consecutive-" + i;
            await client.ConnectAsync(RelayOptions(configuration, clientId).Build());

            Task<MqttClientSubscribeResult> first = client.SubscribeAsync($"x{i}/1", MqttQualityOfServiceLevel.AtLeastOnce);
            Task<MqttClientSubscribeResult> second = client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter($"y{i}/1", MqttQualityOfServiceLevel.AtLeastOnce)
                .WithTopicFilter($"y{i}/2", MqttQualityOfServiceLevel.AtLeastOnce)
                .Build());
            await Task.WhenAll(first, second);

            await WaitUntilAsync(() => receiver.Messages.Count >= 2, $"{i}회차 retained 메시지가 빠졌다.");
            CollectionAssert.AreEquivalent(new[] { "a" + i, "b" + i }, receiver.Messages.Select(Text).ToArray());
            CollectionAssert.AreEquivalent(new[] { $"y{i}/1", $"y{i}/2" },
                broker.Subscribes.Where(s => s.ClientId == clientId && s.Filters.Count == 2).Single().Filters.Select(f => f.Topic).ToArray());
            await client.DisconnectAsync();
        }
        await app.StopAsync();
    }

    /// <summary>
    /// 접속이 끊기면 완료되는 신호를 단다.
    /// </summary>
    private static TaskCompletionSource OnDisconnected(IMqttClient client)
    {
        TaskCompletionSource disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.DisconnectedAsync += _ =>
        {
            disconnected.TrySetResult();
            return Task.CompletedTask;
        };
        return disconnected;
    }

    // C6: 다원 구독이 시간 초과(10초)로 실패하면 클라이언트 접속을 끊고 error를 기록한다.
    [TestMethod]
    public async Task Subscribe_DawonTimeout_DisconnectsClient()
    {
        broker.SubscribeDelay = TimeSpan.FromSeconds(30);
        AppConfiguration configuration = Configuration();
        await using (WebApplication app = await StartAsync(configuration))
        {
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(RelayOptions(configuration).WithTimeout(TimeSpan.FromSeconds(30)).Build());
            TaskCompletionSource disconnected = OnDisconnected(client);

            Task<MqttClientSubscribeResult> subscribe = client.SubscribeAsync("slow/topic", MqttQualityOfServiceLevel.AtLeastOnce);

            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Assert.ThrowsAsync<Exception>(() => subscribe);
            await app.StopAsync();
        }
        Assert.IsTrue(ReadRecords().Any(r => r.GetProperty("kind").GetString() == "error"
            && r.GetProperty("error").GetString() == "subscribe: timeout"), "구독 시간 초과 error 기록이 없다.");
    }

    // C6: 다원 구독 해제가 시간 초과(10초)로 실패하면 클라이언트 접속을 끊는다.
    [TestMethod]
    public async Task Unsubscribe_DawonTimeout_DisconnectsClient()
    {
        AppConfiguration configuration = Configuration();
        await using (WebApplication app = await StartAsync(configuration))
        {
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(RelayOptions(configuration).WithTimeout(TimeSpan.FromSeconds(30)).Build());
            await client.SubscribeAsync("slow/unsub", MqttQualityOfServiceLevel.AtLeastOnce);
            TaskCompletionSource disconnected = OnDisconnected(client);
            broker.UnsubscribeDelay = TimeSpan.FromSeconds(30);

            Task<MqttClientUnsubscribeResult> unsubscribe = client.UnsubscribeAsync("slow/unsub");

            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Assert.ThrowsAsync<Exception>(() => unsubscribe);
            await app.StopAsync();
        }
        Assert.IsTrue(ReadRecords().Any(r => r.GetProperty("kind").GetString() == "error"
            && r.GetProperty("error").GetString() == "unsubscribe: timeout"), "구독 해제 시간 초과 error 기록이 없다.");
    }

    // C6: 구독 해제는 필터 둘을 다원에 한 번에 보내고, 해제 뒤 그 토픽의 다원 메시지는 오지 않는다.
    [TestMethod]
    public async Task Unsubscribe_ForwardedAndStopsDelivery()
    {
        AppConfiguration configuration = Configuration();
        await using (WebApplication app = await StartAsync(configuration))
        {
            Receiver receiver = new Receiver();
            using IMqttClient client = receiver.Attach(new MqttClientFactory().CreateMqttClient());
            await client.ConnectAsync(RelayOptions(configuration).Build());
            await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter("u/1").WithTopicFilter("u/2").Build());

            await client.UnsubscribeAsync(new MqttClientUnsubscribeOptionsBuilder().WithTopicFilter("u/1").WithTopicFilter("u/2").Build());

            CollectionAssert.AreEqual(new[] { "u/1", "u/2" }, broker.Unsubscribes.Single().Topics.ToArray());
            await broker.PublishAsync("u/1", "after");
            await Task.Delay(1000);
            Assert.IsEmpty(receiver.Messages);
            await client.DisconnectAsync();
            await app.StopAsync();
        }
        Assert.IsTrue(ReadRecords().Any(r => r.GetProperty("kind").GetString() == "unsubscribe"));
    }

    // C6: 다원 메시지는 그 클라이언트의 세션으로만 간다(두 클라이언트가 같은 토픽을 구독해도 각자 한 번씩).
    [TestMethod]
    public async Task DawonMessage_GoesOnlyToOwnClient()
    {
        AppConfiguration configuration = Configuration();
        await using WebApplication app = await StartAsync(configuration);
        Receiver first = new Receiver();
        Receiver second = new Receiver();
        using IMqttClient a = first.Attach(new MqttClientFactory().CreateMqttClient());
        using IMqttClient b = second.Attach(new MqttClientFactory().CreateMqttClient());
        await a.ConnectAsync(RelayOptions(configuration, "own-a").Build());
        await b.ConnectAsync(RelayOptions(configuration, "own-b").Build());
        await a.SubscribeAsync("d/t", MqttQualityOfServiceLevel.AtLeastOnce);
        await b.SubscribeAsync("d/t", MqttQualityOfServiceLevel.AtLeastOnce);

        await broker.PublishAsync("d/t", "hello", MqttQualityOfServiceLevel.AtLeastOnce);

        await WaitUntilAsync(() => !first.Messages.IsEmpty && !second.Messages.IsEmpty, "다원 메시지가 오지 않았다.");
        await Task.Delay(500);
        Assert.HasCount(1, first.Messages);
        Assert.HasCount(1, second.Messages);
        Assert.AreEqual(MqttQualityOfServiceLevel.AtLeastOnce, first.Messages.Single().QualityOfServiceLevel);
        await a.DisconnectAsync();
        await b.DisconnectAsync();
        await app.StopAsync();
    }

    // ---------- C7 발행 ----------

    // C7·C9: 클라이언트 발행은 다원에 가고 로컬 핸들러도 호출된다. 양방향 publish를 기록한다.
    [TestMethod]
    public async Task Publish_ForwardedToDawonAndHandledLocally()
    {
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = Configuration();
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            Receiver receiver = new Receiver();
            using IMqttClient client = receiver.Attach(new MqttClientFactory().CreateMqttClient());
            await client.ConnectAsync(RelayOptions(configuration).Build());
            await client.SubscribeAsync(DownlinkTopic + "/#", MqttQualityOfServiceLevel.AtLeastOnce);

            await client.PublishStringAsync(UplinkTopic, RegisterJson, MqttQualityOfServiceLevel.AtLeastOnce);
            await handler.Registered.Task.WaitAsync(Timeout);
            await WaitUntilAsync(() => broker.Publishes.Any(p => p.ClientId == ClientId && p.Topic == UplinkTopic), "다원이 발행을 받지 못했다.");
            FakeDawonBroker.Publish publish = broker.Publishes.First(p => p.ClientId == ClientId && p.Topic == UplinkTopic);
            Assert.AreEqual(RegisterJson, Encoding.UTF8.GetString(publish.Payload));
            Assert.AreEqual(MqttQualityOfServiceLevel.AtLeastOnce, publish.QoS);

            await broker.PublishAsync(DownlinkTopic + "/x", """{"sid":"1","msg":{"o":"w"}}""", MqttQualityOfServiceLevel.AtLeastOnce);
            await WaitUntilAsync(() => !receiver.Messages.IsEmpty, "다원 → 장치 메시지가 오지 않았다.");
            await client.DisconnectAsync();
            await app.StopAsync();
        }

        List<JsonElement> publishes = ReadRecords().Where(r => r.GetProperty("kind").GetString() == "publish").ToList();
        JsonElement up = publishes.Single(r => r.GetProperty("direction").GetString() == "device-to-dawon");
        Assert.AreEqual(UplinkTopic, up.GetProperty("topic").GetString());
        Assert.AreEqual(RegisterJson, up.GetProperty("payload").GetString());
        Assert.AreEqual(1, up.GetProperty("qos").GetInt32());
        Assert.IsFalse(up.GetProperty("retain").GetBoolean());
        JsonElement down = publishes.Single(r => r.GetProperty("direction").GetString() == "dawon-to-device");
        Assert.AreEqual(DownlinkTopic + "/x", down.GetProperty("topic").GetString());
        Assert.AreEqual("utf8", down.GetProperty("payloadEncoding").GetString());
    }

    // C7: 주입한 다원 메시지는 해석되는 토픽이어도 핸들러에 분배하지 않고, 해석 못 한 토픽은 R7 로그가 남는다.
    [TestMethod]
    public async Task InjectedMessage_NotDispatchedButLoggedWhenUnparsed()
    {
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = Configuration();
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            Receiver receiver = new Receiver();
            using IMqttClient client = receiver.Attach(new MqttClientFactory().CreateMqttClient());
            await client.ConnectAsync(RelayOptions(configuration).Build());
            await client.SubscribeAsync(UplinkTopic, MqttQualityOfServiceLevel.AtLeastOnce);
            await client.SubscribeAsync(DownlinkTopic, MqttQualityOfServiceLevel.AtLeastOnce);

            await broker.PublishAsync(UplinkTopic, RegisterJson, MqttQualityOfServiceLevel.AtLeastOnce);
            await broker.PublishAsync(DownlinkTopic, RegisterJson, MqttQualityOfServiceLevel.AtLeastOnce);

            await WaitUntilAsync(() => receiver.Messages.Count >= 2, "다원 메시지가 오지 않았다.");
            await WaitUntilAsync(() => ReadAllText("log").Contains($"Unparsed topic passed through: \"{DownlinkTopic}\", clientId=\"dawon-relay/", StringComparison.Ordinal),
                "주입 메시지의 R7 로그가 없다.");
            await client.DisconnectAsync();
            await app.StopAsync();
        }
        Assert.IsEmpty(handler.Calls);
    }

    // ---------- C8 종료 ----------

    // C8: 정상 종료하면 다원도 정상 종료하고 Will이 발행되지 않는다.
    [TestMethod]
    public async Task CleanDisconnect_DawonCleanWithoutWill()
    {
        AppConfiguration configuration = Configuration();
        await using WebApplication app = await StartAsync(configuration);
        using IMqttClient client = new MqttClientFactory().CreateMqttClient();
        await client.ConnectAsync(RelayOptions(configuration).WithWillTopic("/lwt").WithWillPayload("offline").Build());

        await client.DisconnectAsync();

        await WaitUntilAsync(() => broker.Disconnects.Any(d => d.ClientId == ClientId), "다원 접속이 끊기지 않았다.");
        Assert.AreEqual(MqttClientDisconnectType.Clean, broker.Disconnects.Single().Type);
        await Task.Delay(500);
        Assert.IsFalse(broker.Publishes.Any(p => p.Topic == "/lwt"));
        await app.StopAsync();
    }

    // C8: 비정상 끊김이면 다원이 Will을 한 번 발행하고(다원 쪽 Will), 로컬 브로커의 Will은 다원으로 가지 않는다.
    [TestMethod]
    public async Task UncleanDisconnect_DawonPublishesWillOnce()
    {
        AppConfiguration configuration = Configuration();
        await using WebApplication app = await StartAsync(configuration);
        IMqttClient client = new MqttClientFactory().CreateMqttClient();
        await client.ConnectAsync(RelayOptions(configuration).WithWillTopic("/lwt").WithWillPayload("offline").Build());

        client.Dispose();

        await WaitUntilAsync(() => broker.Publishes.Any(p => p.Topic == "/lwt"), "다원에서 Will이 발행되지 않았다.");
        await Task.Delay(1000);
        Assert.HasCount(1, broker.Publishes.Where(p => p.Topic == "/lwt").ToList());
        Assert.AreEqual(MqttClientDisconnectType.NotClean, broker.Disconnects.Single().Type);
        await app.StopAsync();
    }

    // C8: 다원 쪽에서 끊으면 클라이언트 접속도 끊긴다.
    [TestMethod]
    public async Task DawonDisconnects_ClientIsDisconnected()
    {
        AppConfiguration configuration = Configuration();
        await using WebApplication app = await StartAsync(configuration);
        using IMqttClient client = new MqttClientFactory().CreateMqttClient();
        TaskCompletionSource disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.DisconnectedAsync += _ =>
        {
            disconnected.TrySetResult();
            return Task.CompletedTask;
        };
        await client.ConnectAsync(RelayOptions(configuration).Build());

        await broker.DisconnectClientAsync(ClientId);

        await disconnected.Task.WaitAsync(Timeout);
        await app.StopAsync();
    }

    // C8: 같은 ClientId·다른 끝점의 종료 이벤트는 현재 세션에 적용되지 않는다.
    [TestMethod]
    public async Task DisconnectEventFromOtherEndPoint_IsIgnored()
    {
        AppConfiguration configuration = Configuration();
        await using WebApplication app = await StartAsync(configuration);
        using IMqttClient client = new MqttClientFactory().CreateMqttClient();
        await client.ConnectAsync(RelayOptions(configuration).Build());
        DawonMqttRelay relay = app.Services.GetRequiredService<DawonMqttRelay>();

        await relay.OnClientDisconnectedAsync(new ClientDisconnectedEventArgs(new MqttConnectPacket { ClientId = ClientId }, null,
            MqttClientDisconnectType.NotClean, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 50000), new Dictionary<object, object>()));

        await Task.Delay(500);
        CollectionAssert.Contains((await broker.ConnectedClientIdsAsync()).ToList(), ClientId);
        await client.PublishStringAsync("e/t", "still", MqttQualityOfServiceLevel.AtLeastOnce);
        await WaitUntilAsync(() => broker.Publishes.Any(p => p.Topic == "e/t"), "현재 세션이 닫혔다.");
        await client.DisconnectAsync();
        await app.StopAsync();
    }

    // C8 takeover: 같은 ClientId로 새로 접속하면 새 접속과 새 다원 접속이 유지되고 옛 로컬 접속만 끊긴다.
    [TestMethod]
    public async Task Takeover_KeepsNewConnection()
    {
        AppConfiguration configuration = Configuration();
        await using WebApplication app = await StartAsync(configuration);
        using IMqttClient oldClient = new MqttClientFactory().CreateMqttClient();
        TaskCompletionSource oldDisconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        oldClient.DisconnectedAsync += _ =>
        {
            oldDisconnected.TrySetResult();
            return Task.CompletedTask;
        };
        await oldClient.ConnectAsync(RelayOptions(configuration).Build());
        using IMqttClient newClient = new MqttClientFactory().CreateMqttClient();

        await newClient.ConnectAsync(RelayOptions(configuration).Build());

        await oldDisconnected.Task.WaitAsync(Timeout);
        await Task.Delay(1000);
        Assert.IsTrue(newClient.IsConnected);
        CollectionAssert.Contains((await broker.ConnectedClientIdsAsync()).ToList(), ClientId);
        await newClient.PublishStringAsync("t/new", "new", MqttQualityOfServiceLevel.AtLeastOnce);
        await WaitUntilAsync(() => broker.Publishes.Any(p => p.Topic == "t/new"), "새 세션의 발행이 다원에 가지 않았다.");
        await newClient.DisconnectAsync();
        await app.StopAsync();
    }

    // C8 takeover: 같은 ClientId의 새 접속 둘이 겹쳐 들어와도(다원 응답이 느림) 마지막 접속은 유지되고 다원 접속도 남는다(10회).
    [TestMethod]
    public async Task Takeover_OverlappingConnections_LastOneKept()
    {
        AppConfiguration configuration = Configuration();
        await using WebApplication app = await StartAsync(configuration);
        using IMqttClient first = new MqttClientFactory().CreateMqttClient();
        await first.ConnectAsync(RelayOptions(configuration).Build());
        broker.ConnectDelay = TimeSpan.FromMilliseconds(300);

        for (int i = 0; i < 10; i++)
        {
            using IMqttClient b = new MqttClientFactory().CreateMqttClient();
            using IMqttClient c = new MqttClientFactory().CreateMqttClient();
            Task<MqttClientConnectResult> connectB = b.ConnectAsync(RelayOptions(configuration).WithTimeout(TimeSpan.FromSeconds(30)).Build());
            Task<MqttClientConnectResult> connectC = c.ConnectAsync(RelayOptions(configuration).WithTimeout(TimeSpan.FromSeconds(30)).Build());
            await Task.WhenAll(connectB, connectC);

            await Task.Delay(1000);
            IMqttClient[] connected = new[] { b, c }.Where(x => x.IsConnected).ToArray();
            Assert.HasCount(1, connected, $"{i}회차: 겹친 접속 가운데 하나만 남아야 한다.");
            CollectionAssert.Contains((await broker.ConnectedClientIdsAsync()).ToList(), ClientId, $"{i}회차: 다원 접속이 남지 않았다.");
            string topic = "overlap/" + i;
            await connected[0].PublishStringAsync(topic, "ok", MqttQualityOfServiceLevel.AtLeastOnce);
            await WaitUntilAsync(() => broker.Publishes.Any(p => p.Topic == topic), $"{i}회차: 남은 접속의 발행이 다원에 가지 않았다.");
            await connected[0].DisconnectAsync();
        }
        await app.StopAsync();
    }

    // C8 takeover: 새 접속의 다원 접속이 거부되고 다원이 옛 접속을 이미 끊었으면 옛 로컬 접속도 끊긴다.
    [TestMethod]
    public async Task Takeover_DawonRejectsNew_OldIsDisconnected()
    {
        AppConfiguration configuration = Configuration();
        await using WebApplication app = await StartAsync(configuration);
        using IMqttClient oldClient = new MqttClientFactory().CreateMqttClient();
        TaskCompletionSource oldDisconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        oldClient.DisconnectedAsync += _ =>
        {
            oldDisconnected.TrySetResult();
            return Task.CompletedTask;
        };
        await oldClient.ConnectAsync(RelayOptions(configuration).Build());
        broker.KickExistingOnReject = true;
        broker.ConnectResult = _ => MqttConnectReasonCode.NotAuthorized;
        using IMqttClient newClient = new MqttClientFactory().CreateMqttClient();

        MqttClientConnectResult result = await newClient.ConnectAsync(RelayOptions(configuration).Build());

        Assert.AreEqual(MqttClientConnectResultCode.NotAuthorized, result.ResultCode);
        await oldDisconnected.Task.WaitAsync(Timeout);
        await app.StopAsync();
    }
}
