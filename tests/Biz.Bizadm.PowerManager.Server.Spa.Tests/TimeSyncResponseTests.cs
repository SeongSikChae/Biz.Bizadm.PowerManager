using Biz.Bizadm.PowerManager.Core.Mqtt;
using Biz.Bizadm.PowerManager.Server.Spa.Mqtt;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using System.Collections.Concurrent;
using System.Text;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

/// <summary>
/// tisyn 시간 동기 응답(tisyn-response C1·C2·C4). 핸들러를 바꾸지 않은 기본 <see cref="LoggingDeviceMessageHandler"/>로 확인한다.
/// </summary>
[TestClass]
public sealed class TimeSyncResponseTests
{
    private const string DeviceKey = "DAWONDNS-M130-W-3a0000000201";
    private const string TimeSyncTopic = "dwd.v1/" + DeviceKey + "/iot-server/timesync/json";
    private const string NotifyTopic = "dwd.v1/" + DeviceKey + "/iot-server/notify/json";
    private const string RelayOnJson = """{"sid":"1","msg":{"o":"n","e":[{"n":"/100/0/31","sv":"true"}]}}""";
    private const string DownlinkTopic = "dwd.v1/iot-server/" + DeviceKey;
    private const string DownlinkFilter = DownlinkTopic + "/#";
    private const string MarkerTopic = DownlinkTopic + "/marker";
    private const string DeviceClientId = "DAWONDNS-3a0000000201";
    private const string Subscriber = "DAWONDNS-3a0000000211";
    private const string LateSubscriber = "DAWONDNS-3a0000000212";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // 소수 초(0.750)가 있는 시각. epoch 초는 버림으로 1795147200(2026-11-20T04:00:00Z)이다.
    private static readonly DateTimeOffset FixedNow = new DateTimeOffset(2026, 11, 20, 4, 0, 0, 750, TimeSpan.Zero);
    private const string FixedEpochSeconds = "1795147200";

    private string tempRoot = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.TimeSync.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "ClientApp"));
    }

    [TestCleanup]
    public void TestCleanup() => TestApp.DeleteTempRoot(tempRoot);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// 로그 수준과 서식을 적용한 문장을 모은다.
    /// </summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Enqueue((logLevel, formatter(state, exception), exception));
    }

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
        => TestApp.StartAsync(configuration, Path.Combine(tempRoot, "log"),
            builder => builder.Services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTime(FixedNow))));

    /// <summary>
    /// ClientId(<c>DAWONDNS-{mac}</c>)의 mac을 등록하고 그 발급 키로 접속하는 옵션을 만든다(브로커는 등록 장치 접속만 받는다, mqtt-connect-auth).
    /// </summary>
    private static async Task<MqttClientOptions> ClientOptionsAsync(WebApplication app, AppConfiguration configuration, string clientId)
    {
        TestDevice device = await TestDevices.RegisterAsync(app, clientId["DAWONDNS-".Length..]);
        return new MqttClientOptionsBuilder()
            .WithTcpServer("127.0.0.1", configuration.MqttPort!.Value)
            .WithDevice(device)
            .WithProtocolVersion(MqttProtocolVersion.V310)
            .WithTimeout(Timeout)
            .Build();
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

    /// <summary>
    /// 받는 쪽 클라이언트가 자기 구독 범위의 표시 메시지를 QoS 1로 보내고 받을 때까지 기다린다.
    /// 브로커는 한 클라이언트에 보내는 메시지를 순서대로 내보내므로, 표시 메시지를 받으면 그 전에 큐에 들어간 메시지도 모두 받은 것이다.
    /// </summary>
    private static async Task FlushAsync(IMqttClient client, Receiver receiver, string marker)
    {
        await client.PublishStringAsync(MarkerTopic, marker, MqttQualityOfServiceLevel.AtLeastOnce);
        await WaitUntilAsync(() => receiver.Messages.Any(m => m.Topic == MarkerTopic && Text(m) == marker), "표시 메시지가 오지 않았다.");
    }

    private static string Text(MqttApplicationMessage m) => Encoding.UTF8.GetString(m.Payload);

    private string ReadLogs()
    {
        string directory = Path.Combine(tempRoot, "log");
        StringBuilder builder = new StringBuilder();
        if (Directory.Exists(directory))
        {
            foreach (string file in Directory.GetFiles(directory))
            {
                using FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using StreamReader reader = new StreamReader(stream);
                builder.Append(reader.ReadToEnd());
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// 릴레이 상태를 먼저 보고해, tisyn 뒤에 서버가 릴레이 읽기(r)를 보내지 않게 한다(device-control D019).
    /// </summary>
    private static Task ReportRelayAsync(IMqttClient device)
        => device.PublishStringAsync(NotifyTopic, RelayOnJson, MqttQualityOfServiceLevel.AtLeastOnce);

    /// <summary>
    /// 장치가 tisyn을 보내면 와일드카드로 구독한 클라이언트가 받은 응답 하나(표시 메시지 제외)를 돌려준다.
    /// </summary>
    private static async Task<List<MqttApplicationMessage>> SendTimeSyncAsync(WebApplication app, AppConfiguration configuration, string requestPayload)
    {
        Receiver receiver = new Receiver();
        using IMqttClient subscriber = receiver.Attach(new MqttClientFactory().CreateMqttClient());
        await subscriber.ConnectAsync(await ClientOptionsAsync(app, configuration, Subscriber));
        await subscriber.SubscribeAsync(DownlinkFilter, MqttQualityOfServiceLevel.AtLeastOnce);

        using IMqttClient device = new MqttClientFactory().CreateMqttClient();
        await device.ConnectAsync(await ClientOptionsAsync(app, configuration, DeviceClientId));
        await ReportRelayAsync(device);
        await device.PublishStringAsync(TimeSyncTopic, requestPayload, MqttQualityOfServiceLevel.AtLeastOnce);

        await WaitUntilAsync(() => receiver.Messages.Any(m => m.Topic == DownlinkTopic), "시간 동기 응답이 오지 않았다.");
        await FlushAsync(subscriber, receiver, "after-response");

        await device.DisconnectAsync();
        await subscriber.DisconnectAsync();
        return receiver.Messages.Where(m => m.Topic != MarkerTopic).ToList();
    }

    // C1·C2·C4: 요청 sid를 그대로 쓴 w /99/0/1 응답 하나가 retain 없이 서버 → 장치 토픽으로 오고, 응답 로그가 남는다.
    [TestMethod]
    public async Task TimeSync_SendsWriteResponseWithRequestSid()
    {
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        List<MqttApplicationMessage> messages;
        await using (WebApplication app = await StartAsync(configuration))
        {
            messages = await SendTimeSyncAsync(app, configuration, """{"sid":"7","msg":{"o":"tisyn","e":[]}}""");
            await app.StopAsync();
        }

        MqttApplicationMessage response = messages.Single();
        Assert.AreEqual(DownlinkTopic, response.Topic);
        Assert.IsFalse(response.Retain);
        Assert.AreEqual("""{"sid":"7","msg":{"o":"w","e":[{"n":"/99/0/1","sv":"1795147200"}]}}""", Text(response));

        string log = ReadLogs();
        StringAssert.Contains(log, "Device message \"tisyn\"");
        StringAssert.Contains(log,
            $"Time sync response sent: device=\"{DeviceKey}\", topic=\"{DownlinkTopic}\", sid=\"7\", value=\"{FixedEpochSeconds}\"");
    }

    // C2: 요청에 sid가 없으면 응답 sid는 "0"이다.
    [TestMethod]
    public async Task TimeSync_WithoutSid_UsesZero()
    {
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        List<MqttApplicationMessage> messages;
        await using (WebApplication app = await StartAsync(configuration))
        {
            messages = await SendTimeSyncAsync(app, configuration, """{"msg":{"o":"tisyn","e":[]}}""");
            await app.StopAsync();
        }

        Assert.AreEqual("""{"sid":"0","msg":{"o":"w","e":[{"n":"/99/0/1","sv":"1795147200"}]}}""", Text(messages.Single()));
    }

    // C1: 응답 뒤 새로 구독한 클라이언트는 그 응답을 retained 메시지로 받지 않는다.
    [TestMethod]
    public async Task TimeSync_ResponseIsNotRetained()
    {
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using WebApplication app = await StartAsync(configuration);
        await SendTimeSyncAsync(app, configuration, """{"sid":"0","msg":{"o":"tisyn","e":[]}}""");

        Receiver late = new Receiver();
        using IMqttClient client = late.Attach(new MqttClientFactory().CreateMqttClient());
        await client.ConnectAsync(await ClientOptionsAsync(app, configuration, LateSubscriber));
        await client.SubscribeAsync(DownlinkFilter, MqttQualityOfServiceLevel.AtLeastOnce);
        // retained 메시지는 구독 직후 큐에 들어가므로, 뒤에 보낸 표시 메시지를 받으면 retained 여부가 정해진다.
        await FlushAsync(client, late, "late-marker");

        Assert.IsFalse(late.Messages.Any(m => m.Topic == DownlinkTopic), "응답이 retained로 남았다.");
        await client.DisconnectAsync();
        await app.StopAsync();
    }

    private static string ResponseSentLine => $"Time sync response sent: device=\"{DeviceKey}\", topic=\"{DownlinkTopic}\"";
    private static string UnparsedDownlinkLine => $"Unparsed topic passed through: \"{DownlinkTopic}\"";

    /// <summary>
    /// 로그에 모든 문장이 나타날 때까지 기다린다. R5 응답 로그는 주입의 await가 끝난 뒤 쓰여 R7·R12·R13보다 늦으므로,
    /// R5 줄까지 기다리면 R7 로그가 있었다면 이미 쓰여 있다(injected-message-log D001).
    /// </summary>
    private async Task WaitForLogsAsync(params string[] lines)
        => await WaitUntilAsync(() => { string log = ReadLogs(); return lines.All(l => log.Contains(l, StringComparison.Ordinal)); },
            "로그에 기대한 줄이 나타나지 않았다: " + string.Join(" | ", lines));

    // injected-message-log C1: 구독자가 있으면 주입 응답은 전달되고 큐 넣음·응답 로그는 남지만 해석 불가 토픽 로그는 남지 않는다.
    [TestMethod]
    public async Task InjectedResponse_WithSubscriber_DoesNotLogUnparsedTopic()
    {
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using WebApplication app = await StartAsync(configuration);
        List<MqttApplicationMessage> messages = await SendTimeSyncAsync(app, configuration, """{"sid":"5","msg":{"o":"tisyn","e":[]}}""");
        Assert.AreEqual(DownlinkTopic, messages.Single().Topic);

        await WaitForLogsAsync(
            $"Downlink message enqueued: sender=\"power-manager\", receiver=\"{Subscriber}\", topic=\"{DownlinkTopic}\"",
            ResponseSentLine);
        Assert.IsFalse(ReadLogs().Contains(UnparsedDownlinkLine, StringComparison.Ordinal), "주입 응답이 해석 불가 토픽 로그로 남았다.");
        await app.StopAsync();
    }

    // injected-message-log C1: 구독자가 없으면 구독자 없음·응답 로그는 남지만 해석 불가 토픽 로그는 남지 않는다.
    [TestMethod]
    public async Task InjectedResponse_WithoutSubscriber_DoesNotLogUnparsedTopic()
    {
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using WebApplication app = await StartAsync(configuration);
        using (IMqttClient device = new MqttClientFactory().CreateMqttClient())
        {
            await device.ConnectAsync(await ClientOptionsAsync(app, configuration, DeviceClientId));
            await ReportRelayAsync(device);
            await device.PublishStringAsync(TimeSyncTopic, """{"sid":"6","msg":{"o":"tisyn","e":[]}}""", MqttQualityOfServiceLevel.AtLeastOnce);

            await WaitForLogsAsync(
                $"Downlink message not consumed: sender=\"power-manager\", topic=\"{DownlinkTopic}\"",
                ResponseSentLine);
            await device.DisconnectAsync();
        }

        Assert.IsFalse(ReadLogs().Contains(UnparsedDownlinkLine, StringComparison.Ordinal), "주입 응답이 해석 불가 토픽 로그로 남았다.");
        await app.StopAsync();
    }

    // C4: 브로커가 없으면(시작 전·종료 중) 예외 없이 Error 로그만 남긴다.
    [TestMethod]
    public async Task TimeSync_WithoutServer_LogsErrorWithoutThrowing()
    {
        ListLogger<LoggingDeviceMessageHandler> logger = new ListLogger<LoggingDeviceMessageHandler>();
        LoggingDeviceMessageHandler handler = new LoggingDeviceMessageHandler(logger, new MqttServerAccessor(), new FixedTime(FixedNow));
        DeviceTopic topic = new DeviceTopic("dwd", DeviceKey, DeviceTopicKind.Uplink);
        MqttEnvelope envelope = new MqttEnvelope("0", new MqttMessage(MqttOperation.TimeSync, []));

        await handler.TimeSyncAsync(topic, envelope, CancellationToken.None);

        Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Error
            && e.Message == $"Time sync response failed: device={DeviceKey}, topic={DownlinkTopic}"), "Error 로그가 없다.");
        Assert.IsFalse(logger.Entries.Any(e => e.Message.StartsWith("Time sync response sent", StringComparison.Ordinal)));
    }
}
