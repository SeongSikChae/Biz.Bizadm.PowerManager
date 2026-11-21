using Microsoft.AspNetCore.Builder;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using System.Collections.Concurrent;
using System.Text;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

/// <summary>
/// 구독·구독 해제와 서버 → 장치 메시지 전달 기록 로그(mqtt-subscribe-log C1~C5). 핸들러를 바꾸지 않은 기본
/// <see cref="Mqtt.LoggingDeviceMessageHandler"/>로 확인한다(tisyn 응답 주입 포함).
/// </summary>
[TestClass]
public sealed class MqttSubscribeLogTests
{
    private const string DeviceClientId = "DAWONDNS-3a0000000301";
    // 브로커는 등록 장치의 발급 키 접속만 받으므로(mqtt-connect-auth) 구독·발행 클라이언트도 등록 장치 ClientId를 쓴다.
    private const string SubClient = "DAWONDNS-3a0000000311";
    private const string UnsubClient = "DAWONDNS-3a0000000312";
    private const string SubA = "DAWONDNS-3a0000000313";
    private const string SubB = "DAWONDNS-3a0000000314";
    private const string Writer = "DAWONDNS-3a0000000315";
    private const string UplinkWatcher = "DAWONDNS-3a0000000316";
    private const string Publisher = "DAWONDNS-3a0000000317";
    private const string DeviceKey = "DAWONDNS-M130-W-3a0000000301";
    private const string TimeSyncTopic = "dwd.v1/" + DeviceKey + "/iot-server/timesync/json";
    private const string UplinkTopic = "dwd.v1/" + DeviceKey + "/iot-server/notify/json";
    private const string DownlinkTopic = "dwd.v1/iot-server/" + DeviceKey;
    private const string DownlinkFilter = DownlinkTopic + "/#";
    private const string TimeSyncJson = """{"sid":"0","msg":{"o":"tisyn","e":[]}}""";
    private const string NotifyJson = """{"sid":"1","msg":{"o":"n","e":[{"n":"/100/0/31","sv":"true","ti":""}]}}""";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private string tempRoot = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.SubscribeLog.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "ClientApp"));
    }

    [TestCleanup]
    public void TestCleanup() => TestApp.DeleteTempRoot(tempRoot);

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

    private Task<WebApplication> StartAsync(AppConfiguration configuration)
        => TestApp.StartAsync(configuration, Path.Combine(tempRoot, "log"));

    private static MqttClientOptions ClientOptions(AppConfiguration configuration, TestDevice device)
        => new MqttClientOptionsBuilder()
            .WithTcpServer("127.0.0.1", configuration.MqttPort!.Value)
            .WithDevice(device)
            .WithProtocolVersion(MqttProtocolVersion.V310)
            .WithTimeout(Timeout)
            .Build();

    /// <summary>
    /// ClientId(<c>DAWONDNS-{mac}</c>)의 mac을 등록하고 그 발급 키로 접속한다.
    /// </summary>
    private static async Task<IMqttClient> ConnectAsync(WebApplication app, AppConfiguration configuration, string clientId, Receiver? receiver = null)
    {
        TestDevice device = await TestDevices.RegisterAsync(app, clientId["DAWONDNS-".Length..]);
        IMqttClient client = new MqttClientFactory().CreateMqttClient();
        receiver?.Attach(client);
        MqttClientConnectResult result = await client.ConnectAsync(ClientOptions(configuration, device));
        Assert.AreEqual(MqttClientConnectResultCode.Success, result.ResultCode, clientId);
        return client;
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

    private string[] ReadLogLines()
    {
        string directory = Path.Combine(tempRoot, "log");
        List<string> lines = [];
        if (Directory.Exists(directory))
        {
            foreach (string file in Directory.GetFiles(directory))
            {
                using FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using StreamReader reader = new StreamReader(stream);
                lines.AddRange(reader.ReadToEnd().Split('\n'));
            }
        }
        return [.. lines];
    }

    private bool HasLine(string level, string text)
        => ReadLogLines().Any(l => l.Contains($"[{level}]", StringComparison.Ordinal) && l.Contains(text, StringComparison.Ordinal));

    /// <summary>
    /// 수준과 문장이 같은 줄에 있는 로그를 기다린다. 파일 sink는 버퍼링하지 않아 실행 중에도 읽힌다.
    /// </summary>
    private async Task WaitForLineAsync(string level, string text)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (HasLine(level, text))
                return;
            await Task.Delay(100);
        }
        Assert.Fail($"로그에 [{level}] {text} 줄이 없다.{Environment.NewLine}{string.Join('\n', ReadLogLines())}");
    }

    private static string Enqueued(string sender, string receiver, string topic)
        => $"Downlink message enqueued: sender=\"{sender}\", receiver=\"{receiver}\", topic=\"{topic}\"";

    private static string NotConsumed(string sender, string topic)
        => $"Downlink message not consumed: sender=\"{sender}\", topic=\"{topic}\"";

    // C1: 한 SUBSCRIBE의 필터마다 요청 QoS로 승인되고, 필터마다 구독 로그가 남으며, 그 필터로 메시지를 받는다.
    // MQTTnet 서버는 필터를 QoS 내림차순으로 처리해 SUBACK에 그 순서로 담으므로 QoS 1, QoS 0 순서로 보낸다(D005).
    [TestMethod]
    public async Task Subscribe_LogsEachFilterWithQos_AndGrantsRequestedQos()
    {
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using WebApplication app = await StartAsync(configuration);
        const string filter1 = "dwd.v1/iot-server/DAWONDNS-M130-W-3a0000000302/#";
        const string filter0 = "dwd.v1/iot-server/DAWONDNS-M130-W-3a0000000303/#";

        Receiver receiver = new Receiver();
        using IMqttClient subscriber = await ConnectAsync(app, configuration, SubClient, receiver);
        MqttClientSubscribeResult result = await subscriber.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(filter1, MqttQualityOfServiceLevel.AtLeastOnce)
            .WithTopicFilter(filter0, MqttQualityOfServiceLevel.AtMostOnce)
            .Build());

        List<MqttClientSubscribeResultItem> items = result.Items.ToList();
        Assert.AreEqual(2, items.Count);
        Assert.AreEqual(filter1, items[0].TopicFilter.Topic);
        Assert.AreEqual(MqttClientSubscribeResultCode.GrantedQoS1, items[0].ResultCode);
        Assert.AreEqual(filter0, items[1].TopicFilter.Topic);
        Assert.AreEqual(MqttClientSubscribeResultCode.GrantedQoS0, items[1].ResultCode);

        await WaitForLineAsync("Information", $"MQTT client subscribe: clientId=\"{SubClient}\", topicFilter=\"{filter1}\", qos=1");
        await WaitForLineAsync("Information", $"MQTT client subscribe: clientId=\"{SubClient}\", topicFilter=\"{filter0}\", qos=0");

        using IMqttClient publisher = await ConnectAsync(app, configuration, Publisher);
        const string topic = "dwd.v1/iot-server/DAWONDNS-M130-W-3a0000000302/write/json";
        await publisher.PublishStringAsync(topic, "hello", MqttQualityOfServiceLevel.AtLeastOnce);
        await WaitUntilAsync(() => receiver.Messages.Any(m => m.Topic == topic), "구독한 필터로 메시지를 받지 못했다.");

        await publisher.DisconnectAsync();
        await subscriber.DisconnectAsync();
        await app.StopAsync();
    }

    // C2: UNSUBSCRIBE한 필터마다 구독 해제 로그가 남고, 해제 뒤에는 그 필터의 메시지를 받지 않는다.
    [TestMethod]
    public async Task Unsubscribe_LogsEachFilter_AndStopsDelivery()
    {
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using WebApplication app = await StartAsync(configuration);
        const string removedFilter = "dwd.v1/iot-server/DAWONDNS-M130-W-3a0000000304/#";
        const string keptFilter = "dwd.v1/iot-server/DAWONDNS-M130-W-3a0000000305/#";
        const string removedTopic = "dwd.v1/iot-server/DAWONDNS-M130-W-3a0000000304";
        const string keptTopic = "dwd.v1/iot-server/DAWONDNS-M130-W-3a0000000305";

        Receiver receiver = new Receiver();
        using IMqttClient subscriber = await ConnectAsync(app, configuration, UnsubClient, receiver);
        await subscriber.SubscribeAsync(removedFilter, MqttQualityOfServiceLevel.AtLeastOnce);
        await subscriber.SubscribeAsync(keptFilter, MqttQualityOfServiceLevel.AtLeastOnce);
        await subscriber.UnsubscribeAsync(removedFilter);

        await WaitForLineAsync("Information", $"MQTT client unsubscribe: clientId=\"{UnsubClient}\", topicFilter=\"{removedFilter}\"");

        // 같은 발행자가 QoS 1로 차례로 보내므로, 남은 필터의 표시 메시지를 받았으면 그 앞의 해제된 토픽 메시지도 처리가 끝났다.
        using IMqttClient publisher = await ConnectAsync(app, configuration, Publisher);
        await publisher.PublishStringAsync(removedTopic, "removed", MqttQualityOfServiceLevel.AtLeastOnce);
        await publisher.PublishStringAsync(keptTopic, "marker", MqttQualityOfServiceLevel.AtLeastOnce);
        await WaitUntilAsync(() => receiver.Messages.Any(m => m.Topic == keptTopic), "남은 필터의 표시 메시지가 오지 않았다.");
        Assert.IsFalse(receiver.Messages.Any(m => m.Topic == removedTopic), "해제한 필터의 메시지를 받았다.");

        await publisher.DisconnectAsync();
        await subscriber.DisconnectAsync();
        await app.StopAsync();
    }

    // C3: 서버가 주입한 tisyn 응답은 구독자마다 보낸 ClientId power-manager로, 다른 클라이언트가 접미 토픽으로 보낸 메시지는
    // 그 ClientId로 큐 넣음 로그가 남는다.
    [TestMethod]
    public async Task DownlinkEnqueued_LogsSenderAndEachReceiver()
    {
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using WebApplication app = await StartAsync(configuration);

        Receiver receiverA = new Receiver();
        Receiver receiverB = new Receiver();
        using IMqttClient subscriberA = await ConnectAsync(app, configuration, SubA, receiverA);
        using IMqttClient subscriberB = await ConnectAsync(app, configuration, SubB, receiverB);
        await subscriberA.SubscribeAsync(DownlinkFilter, MqttQualityOfServiceLevel.AtLeastOnce);
        await subscriberB.SubscribeAsync(DownlinkFilter, MqttQualityOfServiceLevel.AtLeastOnce);

        using IMqttClient device = await ConnectAsync(app, configuration, DeviceClientId);
        await device.PublishStringAsync(TimeSyncTopic, TimeSyncJson, MqttQualityOfServiceLevel.AtLeastOnce);
        await WaitForLineAsync("Information", Enqueued("power-manager", SubA, DownlinkTopic));
        await WaitForLineAsync("Information", Enqueued("power-manager", SubB, DownlinkTopic));

        const string suffixTopic = DownlinkTopic + "/write/json";
        using IMqttClient writer = await ConnectAsync(app, configuration, Writer);
        await writer.PublishStringAsync(suffixTopic, """{"sid":"0","msg":{"o":"w","e":[]}}""", MqttQualityOfServiceLevel.AtLeastOnce);
        await WaitForLineAsync("Information", Enqueued(Writer, SubA, suffixTopic));
        await WaitForLineAsync("Information", Enqueued(Writer, SubB, suffixTopic));
        await WaitUntilAsync(() => receiverA.Messages.Any(m => m.Topic == suffixTopic) && receiverB.Messages.Any(m => m.Topic == suffixTopic),
            "구독자가 접미 토픽 메시지를 받지 못했다.");

        await writer.DisconnectAsync();
        await device.DisconnectAsync();
        await subscriberA.DisconnectAsync();
        await subscriberB.DisconnectAsync();
        await app.StopAsync();
    }

    // C4: 서버 → 장치 메시지에 맞는 구독이 없으면 보낸 ClientId와 토픽으로 Warning이 남는다(클라이언트 발행과 주입 응답 모두).
    [TestMethod]
    public async Task DownlinkWithoutSubscriber_LogsNotConsumedWarning()
    {
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using WebApplication app = await StartAsync(configuration);

        const string suffixTopic = DownlinkTopic + "/write/json";
        using IMqttClient writer = await ConnectAsync(app, configuration, Writer);
        await writer.PublishStringAsync(suffixTopic, "x", MqttQualityOfServiceLevel.AtLeastOnce);
        await WaitForLineAsync("Warning", NotConsumed(Writer, suffixTopic));

        using IMqttClient device = await ConnectAsync(app, configuration, DeviceClientId);
        await device.PublishStringAsync(TimeSyncTopic, TimeSyncJson, MqttQualityOfServiceLevel.AtLeastOnce);
        await WaitForLineAsync("Warning", NotConsumed("power-manager", DownlinkTopic));

        Assert.IsFalse(ReadLogLines().Any(l => l.Contains("Downlink message enqueued", StringComparison.Ordinal)), "구독자가 없는데 큐 넣음 로그가 있다.");

        await device.DisconnectAsync();
        await writer.DisconnectAsync();
        await app.StopAsync();
    }

    // C5: 서버 → 장치 토픽이 아닌 메시지(장치 → 서버 토픽, /lwt, 빈 접두)에는 큐 넣음·구독자 없음 로그가 없다.
    [TestMethod]
    public async Task NonDownlinkTopics_HaveNoDeliveryLogs()
    {
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using WebApplication app = await StartAsync(configuration);

        Receiver watcher = new Receiver();
        using IMqttClient uplinkWatcher = await ConnectAsync(app, configuration, UplinkWatcher, watcher);
        await uplinkWatcher.SubscribeAsync("dwd.v1/" + DeviceKey + "/iot-server/#", MqttQualityOfServiceLevel.AtLeastOnce);

        using IMqttClient device = await ConnectAsync(app, configuration, DeviceClientId);
        await device.PublishStringAsync(UplinkTopic, NotifyJson, MqttQualityOfServiceLevel.AtLeastOnce);
        await WaitUntilAsync(() => watcher.Messages.Any(m => m.Topic == UplinkTopic), "장치 → 서버 메시지가 구독자에게 오지 않았다.");
        await device.PublishStringAsync("/lwt", "offline", MqttQualityOfServiceLevel.AtLeastOnce);
        const string emptyPrefixTopic = ".v1/iot-server/" + DeviceKey;
        await device.PublishStringAsync(emptyPrefixTopic, "x", MqttQualityOfServiceLevel.AtLeastOnce);

        // 같은 클라이언트가 QoS 1로 차례로 보내므로, 마지막 서버 → 장치 메시지의 로그가 보이면 앞 메시지의 처리도 끝났다.
        const string markerTopic = DownlinkTopic + "/marker";
        await device.PublishStringAsync(markerTopic, "marker", MqttQualityOfServiceLevel.AtLeastOnce);
        await WaitForLineAsync("Warning", NotConsumed(DeviceClientId, markerTopic));

        string[] deliveryLines = ReadLogLines().Where(l => l.Contains("Downlink message", StringComparison.Ordinal)).ToArray();
        Assert.IsFalse(deliveryLines.Any(l => l.Contains($"topic=\"{UplinkTopic}\"", StringComparison.Ordinal)), "장치 → 서버 메시지에 전달 로그가 있다.");
        Assert.IsFalse(deliveryLines.Any(l => l.Contains("topic=\"/lwt\"", StringComparison.Ordinal)), "/lwt에 전달 로그가 있다.");
        Assert.IsFalse(deliveryLines.Any(l => l.Contains($"topic=\"{emptyPrefixTopic}\"", StringComparison.Ordinal)), "빈 접두 토픽에 전달 로그가 있다.");
        Assert.AreEqual(1, deliveryLines.Length, string.Join('\n', deliveryLines));

        await device.DisconnectAsync();
        await uplinkWatcher.DisconnectAsync();
        await app.StopAsync();
    }
}
