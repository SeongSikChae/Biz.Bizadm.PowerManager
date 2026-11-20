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
    private const string DownlinkTopic = "dwd.v1/iot-server/" + DeviceKey;
    private const string DownlinkFilter = DownlinkTopic + "/#";
    private const string MarkerTopic = DownlinkTopic + "/marker";
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

    private static MqttClientOptions ClientOptions(AppConfiguration configuration, string clientId)
        => new MqttClientOptionsBuilder()
            .WithTcpServer("127.0.0.1", configuration.MqttPort!.Value)
            .WithClientId(clientId)
            .WithProtocolVersion(MqttProtocolVersion.V310)
            .WithTimeout(Timeout)
            .Build();

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
    /// 장치가 tisyn을 보내면 와일드카드로 구독한 클라이언트가 받은 응답 하나(표시 메시지 제외)를 돌려준다.
    /// </summary>
    private static async Task<List<MqttApplicationMessage>> SendTimeSyncAsync(AppConfiguration configuration, string requestPayload)
    {
        Receiver receiver = new Receiver();
        using IMqttClient subscriber = receiver.Attach(new MqttClientFactory().CreateMqttClient());
        await subscriber.ConnectAsync(ClientOptions(configuration, "subscriber"));
        await subscriber.SubscribeAsync(DownlinkFilter, MqttQualityOfServiceLevel.AtLeastOnce);

        using IMqttClient device = new MqttClientFactory().CreateMqttClient();
        await device.ConnectAsync(ClientOptions(configuration, "DAWONDNS-3a0000000201"));
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
            messages = await SendTimeSyncAsync(configuration, """{"sid":"7","msg":{"o":"tisyn","e":[]}}""");
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
            messages = await SendTimeSyncAsync(configuration, """{"msg":{"o":"tisyn","e":[]}}""");
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
        await SendTimeSyncAsync(configuration, """{"sid":"0","msg":{"o":"tisyn","e":[]}}""");

        Receiver late = new Receiver();
        using IMqttClient client = late.Attach(new MqttClientFactory().CreateMqttClient());
        await client.ConnectAsync(ClientOptions(configuration, "late-subscriber"));
        await client.SubscribeAsync(DownlinkFilter, MqttQualityOfServiceLevel.AtLeastOnce);
        // retained 메시지는 구독 직후 큐에 들어가므로, 뒤에 보낸 표시 메시지를 받으면 retained 여부가 정해진다.
        await FlushAsync(client, late, "late-marker");

        Assert.IsFalse(late.Messages.Any(m => m.Topic == DownlinkTopic), "응답이 retained로 남았다.");
        await client.DisconnectAsync();
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
