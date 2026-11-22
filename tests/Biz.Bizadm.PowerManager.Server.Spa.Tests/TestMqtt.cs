using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using MQTTnet;
using MQTTnet.Exceptions;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using System.Collections.Concurrent;
using System.Text;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

/// <summary>
/// 등록 장치로 브로커에 접속하는 테스트 장비. 토픽은 <c>dwd.v1/DAWONDNS-{모델}-{mac}/iot-server/…</c>이고,
/// 서버 → 장치 토픽(<c>/#</c>)으로 받은 메시지를 모은다.
/// </summary>
internal sealed class TestMqttDevice : IAsyncDisposable
{
    private readonly IMqttClient client = new MqttClientFactory().CreateMqttClient();
    private int markerCount;

    private TestMqttDevice(TestDevice device, string deviceKey)
    {
        Device = device;
        DeviceKey = deviceKey;
        client.ApplicationMessageReceivedAsync += e =>
        {
            received.Enqueue(e.ApplicationMessage);
            return Task.CompletedTask;
        };
    }

    private readonly ConcurrentQueue<MqttApplicationMessage> received = new();

    public TestDevice Device { get; }

    public string DeviceKey { get; }

    public string DownlinkTopic => "dwd.v1/iot-server/" + DeviceKey;

    private string MarkerTopic => DownlinkTopic + "/marker";

    /// <summary>
    /// 받은 서버 → 장치 메시지(표시 메시지 제외).
    /// </summary>
    public IReadOnlyList<MqttApplicationMessage> Messages => received.Where(m => m.Topic != MarkerTopic).ToList();

    public IReadOnlyList<string> Payloads => Messages.Select(TestMqtt.Text).ToList();

    public static async Task<TestMqttDevice> ConnectAsync(WebApplication app, AppConfiguration configuration, string mac,
        string modelId = "B540_W", bool subscribe = true)
    {
        TestDevice device = await TestDevices.RegisterAsync(app, mac, modelId);
        TestMqttDevice result = new TestMqttDevice(device, $"DAWONDNS-{modelId}-{mac}");
        await result.ConnectClientAsync(configuration);
        if (subscribe)
            await result.SubscribeAsync();
        return result;
    }

    private Task ConnectClientAsync(AppConfiguration configuration)
        => client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer("127.0.0.1", configuration.MqttPort!.Value)
            .WithDevice(Device)
            .WithProtocolVersion(MqttProtocolVersion.V310)
            .WithTimeout(TestMqtt.Timeout)
            .Build());

    public Task SubscribeAsync() => SubscribeToAsync(DownlinkTopic + "/#");

    public Task SubscribeToAsync(string topicFilter) => client.SubscribeAsync(topicFilter, MqttQualityOfServiceLevel.AtLeastOnce);

    /// <summary>
    /// 장치 → 서버 메시지를 QoS 1로 보낸다. 돌아오면 브로커의 처리(저장소 반영 포함)가 끝난 것이다.
    /// </summary>
    public Task SendAsync(string operation, string elementsJson = "[]", string suffix = "notify/json")
        => SendRawAsync($"dwd.v1/{DeviceKey}/iot-server/{suffix}", $"{{\"sid\":\"1\",\"msg\":{{\"o\":\"{operation}\",\"e\":{elementsJson}}}}}");

    public Task SendRawAsync(string topic, string payload)
        => client.PublishStringAsync(topic, payload, MqttQualityOfServiceLevel.AtLeastOnce);

    /// <summary>
    /// 릴레이 상태를 알려 서버의 릴레이 읽기(<c>r</c>)가 나가지 않게 하고 토픽을 기억시킨다.
    /// </summary>
    public Task ReportRelayAsync(bool on) => SendAsync("n", on ? """[{"n":"/100/0/31","sv":"true"}]""" : """[{"n":"/100/0/31","sv":"false"}]""");

    /// <summary>
    /// 자기 구독 범위의 표시 메시지를 보내고 받을 때까지 기다린다. 브로커는 한 클라이언트에 순서대로 내보내므로
    /// 그 전에 큐에 들어간 메시지도 모두 받은 것이다.
    /// </summary>
    public async Task FlushAsync()
    {
        string marker = "marker-" + Interlocked.Increment(ref markerCount);
        await client.PublishStringAsync(MarkerTopic, marker, MqttQualityOfServiceLevel.AtLeastOnce);
        await TestMqtt.WaitUntilAsync(() => received.Any(m => m.Topic == MarkerTopic && TestMqtt.Text(m) == marker), "표시 메시지가 오지 않았다.");
    }

    public Task WaitForPayloadsAsync(int count)
        => TestMqtt.WaitUntilAsync(() => Messages.Count >= count, $"서버 → 장치 메시지 {count}개가 오지 않았다: " + string.Join(" | ", Payloads));

    public bool IsConnected => client.IsConnected;

    public Task DisconnectAsync() => client.DisconnectAsync();

    /// <summary>
    /// 연결을 끊고 정리한다. 앱이 먼저 멈춰 브로커가 연결을 끊는 중이면 DISCONNECT를 보내지 못할 수 있다.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (client.IsConnected)
                await client.DisconnectAsync();
        }
        catch (MqttCommunicationException)
        {
            // 브로커가 이미 연결을 끊었다.
        }
        client.Dispose();
    }
}

internal static class TestMqtt
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static string Text(MqttApplicationMessage message) => Encoding.UTF8.GetString(message.Payload);

    public static async Task WaitUntilAsync(Func<bool> condition, string message)
        => await WaitUntilAsync(() => Task.FromResult(condition()), message);

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, string message)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(50);
        }
        Assert.Fail(message);
    }

    public static string ReadLogs(string directory)
    {
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
    /// 요청이 들어온 로컬 포트를 정해 보내고 응답 전체를 읽는다(TestServer는 실제 포트를 열지 않는다. 관리 80·443, 장치 18446·18443).
    /// </summary>
    public static async Task<(int Status, string? ContentType, string Body)> SendHttpAsync(WebApplication app, int localPort, string method,
        string path, string? jsonBody = null)
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
        return (context.Response.StatusCode, context.Response.ContentType?.Split(';')[0].Trim(), body);
    }
}
