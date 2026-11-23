using Biz.Bizadm.PowerManager.Core.Mqtt;
using Biz.Bizadm.PowerManager.Server.Core;
using Biz.Bizadm.PowerManager.Server.Spa.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

[TestClass]
public sealed class MqttBrokerTests
{
    private const string DeviceKey = "DAWONDNS-B540_W-aabbccddeeff";
    private const string UplinkTopic = "dwd.v1/" + DeviceKey + "/iot-server/json";
    private const string LwtTopic = "dwd.v1/" + DeviceKey + "/iot-server/lwt";
    private const string RegisterJson = """{"sid":"0","msg":{"o":"rg","e":[{"n":"/3/0/3","sv":"1.01.34"}]}}""";
    private const string PfxPassword = "test-password";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private string tempRoot = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.Server.Spa.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "ClientApp"));
    }

    [TestCleanup]
    public void TestCleanup() => TestApp.DeleteTempRoot(tempRoot);

    private sealed class RecordingHandler : IDeviceMessageHandler
    {
        public ConcurrentQueue<string> Calls { get; } = new();
        public ConcurrentQueue<MqttEnvelope> Notified { get; } = new();
        public TaskCompletionSource<MqttEnvelope> Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<DeviceTopic> Offline { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RegisterAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct)
        {
            Calls.Enqueue("rg");
            Registered.TrySetResult(envelope);
            return Task.CompletedTask;
        }

        public Task DeregisterAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct) { Calls.Enqueue("drg"); return Task.CompletedTask; }

        public Task TimeSyncAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct) { Calls.Enqueue("tisyn"); return Task.CompletedTask; }

        public Task NotifyAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct)
        {
            Calls.Enqueue("notify");
            Notified.Enqueue(envelope);
            return Task.CompletedTask;
        }

        public Task OfflineAsync(DeviceTopic topic, CancellationToken ct)
        {
            Calls.Enqueue("offline");
            Offline.TrySetResult(topic);
            return Task.CompletedTask;
        }
    }

    private Task<WebApplication> StartAsync(AppConfiguration configuration, RecordingHandler handler)
        => TestApp.StartAsync(configuration, Path.Combine(tempRoot, "log"),
            builder => builder.Services.Replace(ServiceDescriptor.Singleton<IDeviceMessageHandler>(handler)));

    private static MqttClientOptionsBuilder ClientOptions(ushort port, string? clientId = null)
        => new MqttClientOptionsBuilder()
            .WithTcpServer("127.0.0.1", port)
            .WithClientId(clientId ?? "test-" + Guid.NewGuid().ToString("N")[..8])
            .WithProtocolVersion(MqttProtocolVersion.V310);

    /// <summary>등록 장치로 접속하는 옵션(브로커는 등록 장치의 발급 키 접속만 받는다).</summary>
    private static MqttClientOptionsBuilder ClientOptions(ushort port, TestDevice device)
        => ClientOptions(port).WithDevice(device);

    /// <summary>테스트가 쓰는 장치 키(<see cref="DeviceKey"/>)의 mac.</summary>
    private const string DeviceMac = "aabbccddeeff";

    [TestMethod]
    public async Task Mqtt310Client_PublishRegister_CallsRegisterOnce()
    {
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            TestDevice device = await TestDevices.RegisterAsync(app, DeviceMac);
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(ClientOptions(configuration.MqttPort!.Value, device).Build());
            await client.PublishStringAsync(UplinkTopic, RegisterJson, MqttQualityOfServiceLevel.AtLeastOnce);

            MqttEnvelope envelope = await handler.Registered.Task.WaitAsync(Timeout);
            Assert.AreEqual("1.01.34", envelope.Msg.Elements[0].StringValue);

            await client.DisconnectAsync();
            await app.StopAsync();
        }
        CollectionAssert.AreEqual(new[] { "rg" }, handler.Calls.ToArray());
    }

    [TestMethod]
    public async Task MessageWithoutMsg_IsNotDispatchedAndLogsWarning()
    {
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            TestDevice device = await TestDevices.RegisterAsync(app, DeviceMac);
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(ClientOptions(configuration.MqttPort!.Value, device).Build());
            await client.PublishStringAsync(UplinkTopic, """{"sid":"0"}""", MqttQualityOfServiceLevel.AtLeastOnce);
            // 같은 클라이언트의 QoS 1 발행은 순서대로 처리되므로, 뒤의 rg 수신으로 앞 메시지 처리 완료를 확인한다.
            await client.PublishStringAsync(UplinkTopic, RegisterJson, MqttQualityOfServiceLevel.AtLeastOnce);
            await handler.Registered.Task.WaitAsync(Timeout);

            await client.DisconnectAsync();
            await app.StopAsync();
        }

        CollectionAssert.AreEqual(new[] { "rg" }, handler.Calls.ToArray());
        string log = ReadLogs();
        StringAssert.Contains(log, "Device message has no msg.o or msg.e");
    }

    [TestMethod]
    public async Task UncleanDisconnectWithWill_CallsOfflineOnce()
    {
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            TestDevice device = await TestDevices.RegisterAsync(app, DeviceMac);
            // MQTTnet 클라이언트의 DisconnectAsync는 DISCONNECT를 보내 Will을 폐기하므로, 원시 TCP로 MQTT 3.1 CONNECT(Will 포함) 후 끊는다.
            using (TcpClient tcp = new TcpClient())
            {
                await tcp.ConnectAsync(IPAddress.Loopback, configuration.MqttPort!.Value);
                NetworkStream stream = tcp.GetStream();
                await stream.WriteAsync(BuildMqtt31Connect(device.ClientId, LwtTopic, "offline", device.UserName, device.Password));
                byte[] connAck = new byte[4];
                await stream.ReadExactlyAsync(connAck).AsTask().WaitAsync(Timeout);
                Assert.AreEqual(0x20, connAck[0]);
                Assert.AreEqual(0x00, connAck[3]);
                tcp.Client.Close();
            }

            DeviceTopic topic = await handler.Offline.Task.WaitAsync(Timeout);
            Assert.AreEqual(new DeviceTopic("dwd", DeviceKey, DeviceTopicKind.Lwt), topic);
            await app.StopAsync();
        }
        CollectionAssert.AreEqual(new[] { "offline" }, handler.Calls.ToArray());
    }

    [TestMethod]
    public async Task UnknownOperation_LogsClientIdAndRawPayload()
    {
        const string clientId = "DAWONDNS-" + DeviceMac;
        const string payload = """{"sid":"0","msg":{"o":"zz","e":[{"n":"/1/0/0","v":1}]}}""";
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            TestDevice device = await TestDevices.RegisterAsync(app, DeviceMac);
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(ClientOptions(configuration.MqttPort!.Value, device).Build());
            await client.PublishStringAsync(UplinkTopic, payload, MqttQualityOfServiceLevel.AtLeastOnce);
            // 같은 클라이언트의 QoS 1 발행은 순서대로 처리되므로, 뒤의 rg 수신으로 앞 메시지 처리 완료를 확인한다.
            await client.PublishStringAsync(UplinkTopic, RegisterJson, MqttQualityOfServiceLevel.AtLeastOnce);
            await handler.Registered.Task.WaitAsync(Timeout);

            await client.DisconnectAsync();
            await app.StopAsync();
        }

        CollectionAssert.AreEqual(new[] { "rg" }, handler.Calls.ToArray());
        string log = ReadLogs();
        StringAssert.Contains(log, $"Unknown device operation \"zz\": \"{DeviceKey}\", topic=\"{UplinkTopic}\", clientId=\"{clientId}\", bytes={Encoding.UTF8.GetByteCount(payload)}, truncated=False, payload={payload}");
    }

    // C1·C2·C3: M130-W 실장비 형태(토픽 notify/json, 줄바꿈 JSON, 요소의 ti)의 "n"과 "notify"를 모두 NotifyAsync로 분배한다.
    [TestMethod]
    public async Task M130NotifyShortOperation_DispatchesToNotify()
    {
        const string notifyTopic = "dwd.v1/DAWONDNS-M130-W-3a0000000101/iot-server/notify/json";
        const string shortPayload = """
            {
              "sid":"1",
              "msg":{"o":"n","e":[
                {"n":"/100/0/1","sv":"228.30","ti":""},
                {"n":"/100/0/0","sv":"true","ti":""},
                {"n":"/99/0/5","sv":"3","ti":""}
              ]}
            }
            """;
        const string notifyPayload = """{"sid":"2","msg":{"o":"notify","e":[{"n":"/3/0/3","sv":"2.01.58"}]}}""";
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            TestDevice device = await TestDevices.RegisterAsync(app, "3a0000000101");
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(ClientOptions(configuration.MqttPort!.Value, device).Build());
            await client.PublishStringAsync(notifyTopic, shortPayload, MqttQualityOfServiceLevel.AtLeastOnce);
            await client.PublishStringAsync(notifyTopic, notifyPayload, MqttQualityOfServiceLevel.AtLeastOnce);
            // 같은 클라이언트의 QoS 1 발행은 순서대로 처리되므로, 뒤의 rg 수신으로 앞 메시지 처리 완료를 확인한다.
            await client.PublishStringAsync(UplinkTopic, RegisterJson, MqttQualityOfServiceLevel.AtLeastOnce);
            await handler.Registered.Task.WaitAsync(Timeout);

            await client.DisconnectAsync();
            await app.StopAsync();
        }

        CollectionAssert.AreEqual(new[] { "notify", "notify", "rg" }, handler.Calls.ToArray());
        MqttEnvelope[] notified = handler.Notified.ToArray();
        CollectionAssert.AreEqual(new[] { "n", "notify" }, notified.Select(n => n.Msg.Operation).ToArray());
        CollectionAssert.AreEqual(
            new[] { ("/100/0/1", "228.30"), ("/100/0/0", "true"), ("/99/0/5", "3") },
            notified[0].Msg.Elements.Select(e => (e.Path, e.StringValue)).ToArray());

        string log = ReadLogs();
        Assert.IsFalse(log.Contains("Unknown device operation", StringComparison.Ordinal), "n이 알 수 없는 오퍼레이션으로 남았다.");
        Assert.IsFalse(log.Contains("not valid JSON", StringComparison.Ordinal), "M130-W 페이로드가 JSON 오류로 남았다.");
        Assert.IsFalse(log.Contains("no msg.o or msg.e", StringComparison.Ordinal), "M130-W 페이로드가 해석 실패로 남았다.");
    }

    [TestMethod]
    public async Task WillOnUnparsedLwtTopic_LogsClientIdAndPayload()
    {
        const string clientId = "DAWONDNS-" + DeviceMac;
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            TestDevice device = await TestDevices.RegisterAsync(app, DeviceMac);
            // 실장비처럼 장치 키 없는 /lwt를 Will 토픽으로 등록하고 DISCONNECT 없이 끊는다.
            using (TcpClient tcp = new TcpClient())
            {
                await tcp.ConnectAsync(IPAddress.Loopback, configuration.MqttPort!.Value);
                NetworkStream stream = tcp.GetStream();
                await stream.WriteAsync(BuildMqtt31Connect(device.ClientId, "/lwt", "offline", device.UserName, device.Password));
                byte[] connAck = new byte[4];
                await stream.ReadExactlyAsync(connAck).AsTask().WaitAsync(Timeout);
                Assert.AreEqual(0x00, connAck[3]);
                tcp.Client.Close();
            }

            await WaitForLogAsync($"Unparsed topic passed through: \"/lwt\", clientId=\"{clientId}\", bytes=7, truncated=False, payload=offline");
            await app.StopAsync();
        }
        Assert.IsTrue(handler.Calls.IsEmpty);
    }

    // injected-message-log C2: 클라이언트가 서버 → 장치 토픽에 보낸 메시지는 서버 주입으로 보지 않고 해석 불가 토픽 로그를 남긴다.
    // (ClientId가 power-manager인 클라이언트는 이제 접속 인증에서 거부되므로 등록 장치로 확인한다. mqtt-connect-auth)
    [TestMethod]
    public async Task ClientPublishToDownlink_LogsUnparsedTopic()
    {
        const string downlinkTopic = "dwd.v1/iot-server/" + DeviceKey;
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            TestDevice device = await TestDevices.RegisterAsync(app, DeviceMac);
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(ClientOptions(configuration.MqttPort!.Value, device).Build());
            await client.PublishStringAsync(downlinkTopic, "hello", MqttQualityOfServiceLevel.AtLeastOnce);

            await WaitForLogAsync($"Unparsed topic passed through: \"{downlinkTopic}\", clientId=\"{device.ClientId}\", bytes=5, truncated=False, payload=hello");
            await client.DisconnectAsync();
            await app.StopAsync();
        }
        Assert.IsTrue(handler.Calls.IsEmpty);
    }

    [TestMethod]
    public async Task UnparsedTopic_LargePayload_IsTruncatedTo4096Bytes()
    {
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            TestDevice device = await TestDevices.RegisterAsync(app, DeviceMac);
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(ClientOptions(configuration.MqttPort!.Value, device).Build());
            await client.PublishStringAsync("test/large", new string('a', 5000), MqttQualityOfServiceLevel.AtLeastOnce);

            await WaitForLogAsync("Unparsed topic passed through: \"test/large\"");
            await client.DisconnectAsync();
            await app.StopAsync();
        }

        string log = ReadLogs();
        StringAssert.Contains(log, "bytes=5000, truncated=True, payload=" + new string('a', 4096));
        Assert.IsFalse(log.Contains(new string('a', 4097)), "4096바이트를 넘는 페이로드가 로그에 남았다.");
    }

    // mqtts-tls-policy C1: MQTTS는 TLS 1.2와 1.3을 받는다. TLS 1.3도 등록 장치로 접속해야 접속 인증(R9)을 지난다.
    [TestMethod]
    public async Task Mqtts_Tls12_PublishRegister_CallsRegister_AndTls13Connects()
    {
        string thumbprint = TestApp.WritePfx(Path.Combine(tempRoot, "server.pfx"), PfxPassword);
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        configuration.Tls = new TlsConfiguration { UseTls = true, ServerCertificate = "server.pfx", ServerCertificatePassword = PfxPassword };
        RecordingHandler handler = new RecordingHandler();

        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            ushort port = configuration.MqttsPort!.Value;
            TestDevice device = await TestDevices.RegisterAsync(app, DeviceMac);
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(ClientOptions(port, device).WithTlsOptions(tls => tls
                .UseTls()
                .WithSslProtocols(SslProtocols.Tls12)
                .WithTargetHost("localhost")
                .WithCertificateValidationHandler(args => args.Certificate.GetCertHashString() == thumbprint)).Build());
            await client.PublishStringAsync(UplinkTopic, RegisterJson, MqttQualityOfServiceLevel.AtLeastOnce);
            await handler.Registered.Task.WaitAsync(Timeout);
            await client.DisconnectAsync();

            using IMqttClient tls13Client = new MqttClientFactory().CreateMqttClient();
            MqttClientConnectResult tls13Result = await tls13Client.ConnectAsync(ClientOptions(port, device).WithTlsOptions(tls => tls
                .UseTls()
                .WithSslProtocols(SslProtocols.Tls13)
                .WithTargetHost("localhost")
                .WithCertificateValidationHandler(args => args.Certificate.GetCertHashString() == thumbprint)).WithTimeout(TimeSpan.FromSeconds(5)).Build());
            Assert.AreEqual(MqttClientConnectResultCode.Success, tls13Result.ResultCode);
            await tls13Client.DisconnectAsync();

            await app.StopAsync();
        }
        CollectionAssert.AreEqual(new[] { "rg" }, handler.Calls.ToArray());
    }

    // mqtt-connect-auth C1·C2·C3·C6: 등록 장치가 발급 키로 접속할 때(Match)만 받고, Mismatch·Unknown은 반환 코드 4로 거부한다.
    // 거부한 접속에도 판정 로그가 하나씩 남고, 비밀번호 값은 남지 않는다.
    [TestMethod]
    public async Task ValidatingConnection_AcceptsOnlyRegisteredDeviceWithMatchingKey()
    {
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        string wrongPassword = "WrongPassword9876";
        string mqttKey;
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            mqttKey = (await TestDevices.RegisterAsync(app, "aabbccddeeff")).Password;
            ushort port = configuration.MqttPort!.Value;

            (string ClientId, string? UserName, string? Password, MqttProtocolVersion Version, MqttClientConnectResultCode Result, string Expected)[] cases =
            [
                ("DAWONDNS-AABBCCDDEEFF", DeviceKey, mqttKey, MqttProtocolVersion.V310, MqttClientConnectResultCode.Success,
                    "hasPassword=True, passwordCheck=\"Match\""),
                ("DAWONDNS-aabbccddeeff", DeviceKey, wrongPassword, MqttProtocolVersion.V310, MqttClientConnectResultCode.BadUserNameOrPassword,
                    "hasPassword=True, passwordCheck=\"Mismatch\""),
                ("DAWONDNS-aabbccddeeff", DeviceKey, null, MqttProtocolVersion.V310, MqttClientConnectResultCode.BadUserNameOrPassword,
                    "hasPassword=False, passwordCheck=\"Mismatch\""),
                ("DAWONDNS-aabbccddeeff", DeviceKey, wrongPassword, MqttProtocolVersion.V311, MqttClientConnectResultCode.BadUserNameOrPassword,
                    "hasPassword=True, passwordCheck=\"Mismatch\""),
                ("DAWONDNS-112233445566", "DAWONDNS-B540_W-112233445566", mqttKey, MqttProtocolVersion.V310, MqttClientConnectResultCode.BadUserNameOrPassword,
                    "hasPassword=True, passwordCheck=\"Unknown\""),
                ("DAWONDNS-112233445566", "DAWONDNS-B540_W-112233445566", mqttKey, MqttProtocolVersion.V311, MqttClientConnectResultCode.BadUserNameOrPassword,
                    "hasPassword=True, passwordCheck=\"Unknown\""),
                ("client-not-device", null, null, MqttProtocolVersion.V310, MqttClientConnectResultCode.BadUserNameOrPassword,
                    "hasPassword=False, passwordCheck=\"Unknown\""),
                ("power-manager", null, null, MqttProtocolVersion.V310, MqttClientConnectResultCode.BadUserNameOrPassword,
                    "hasPassword=False, passwordCheck=\"Unknown\""),
            ];
            foreach ((string clientId, string? userName, string? password, MqttProtocolVersion version, MqttClientConnectResultCode expectedResult, string expected) in cases)
            {
                MqttClientOptionsBuilder options = ClientOptions(port, clientId).WithProtocolVersion(version);
                if (userName is not null)
                    options = password is null ? options.WithCredentials(userName) : options.WithCredentials(userName, password);
                using IMqttClient client = new MqttClientFactory().CreateMqttClient();
                Assert.AreEqual(expectedResult, await ConnectResultAsync(client, options.Build()), $"{clientId} {version}");
                await WaitForLogAsync($"MQTT client connecting: clientId=\"{clientId}\", userName={(userName is null ? "null" : $"\"{userName}\"")}, {expected}");
                if (client.IsConnected)
                    await client.DisconnectAsync();
            }

            // MQTT 3.1 장치가 실제로 받는 CONNACK 반환 코드(넷째 바이트).
            Assert.AreEqual(0x04, await RawConnAckCodeAsync(port, BuildMqtt31Connect("DAWONDNS-aabbccddeeff", null, null, DeviceKey, wrongPassword)), "틀린 비밀번호");
            Assert.AreEqual(0x04, await RawConnAckCodeAsync(port,
                BuildMqtt31Connect("DAWONDNS-112233445566", null, null, "DAWONDNS-B540_W-112233445566", mqttKey)), "미등록 mac");
            Assert.AreEqual(0x00, await RawConnAckCodeAsync(port, BuildMqtt31Connect("DAWONDNS-aabbccddeeff", null, null, DeviceKey, mqttKey)), "Match");
            await app.StopAsync();
        }

        string log = ReadLogs();
        Assert.IsFalse(log.Contains(mqttKey, StringComparison.Ordinal), "로그에 mqtt_key가 있다");
        Assert.IsFalse(log.Contains(wrongPassword, StringComparison.Ordinal), "로그에 비밀번호가 있다");
    }

    // mqtt-connect-auth C4·C6: 접속 확인 중 예외(등록 조회 실패)가 나면 Error 로그를 남기고 반환 코드 3으로 거부하며, 판정은 Unknown이다.
    [TestMethod]
    public async Task ValidatingConnection_CheckFails_RejectsWithServerUnavailable()
    {
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        TestDevice device;
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            device = await TestDevices.RegisterAsync(app, "aabbccddeeff");
            ushort port = configuration.MqttPort!.Value;
            await using (PowerManagerDbContext db = await app.Services.GetRequiredService<IDbContextFactory<PowerManagerDbContext>>().CreateDbContextAsync())
                await db.Database.ExecuteSqlRawAsync("DROP TABLE Devices");

            foreach (MqttProtocolVersion version in new[] { MqttProtocolVersion.V310, MqttProtocolVersion.V311 })
            {
                using IMqttClient client = new MqttClientFactory().CreateMqttClient();
                MqttClientOptions options = ClientOptions(port).WithProtocolVersion(version).WithDevice(device).Build();
                Assert.AreEqual(MqttClientConnectResultCode.ServerUnavailable, await ConnectResultAsync(client, options), version.ToString());
            }
            Assert.AreEqual(0x03, await RawConnAckCodeAsync(port, BuildMqtt31Connect(device.ClientId, null, null, device.UserName, device.Password)));

            await WaitForLogAsync("MQTT connection credential check failed");
            await WaitForLogAsync($"MQTT client connecting: clientId=\"{device.ClientId}\", userName=\"{device.UserName}\", hasPassword=True, passwordCheck=\"Unknown\"");
            await app.StopAsync();
        }
        Assert.IsFalse(ReadLogs().Contains(device.Password, StringComparison.Ordinal), "로그에 mqtt_key가 있다");
    }

    /// <summary>
    /// 접속 결과 코드. MQTTnet 5는 서버가 거부해도 예외 없이 그 결과 코드를 돌려준다.
    /// </summary>
    private static async Task<MqttClientConnectResultCode> ConnectResultAsync(IMqttClient client, MqttClientOptions options)
        => (await client.ConnectAsync(options)).ResultCode;

    /// <summary>
    /// 원시 TCP로 CONNECT를 보내고 CONNACK의 반환 코드(넷째 바이트)를 돌려준다.
    /// </summary>
    private static async Task<byte> RawConnAckCodeAsync(ushort port, byte[] connect)
    {
        using TcpClient tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        NetworkStream stream = tcp.GetStream();
        await stream.WriteAsync(connect);
        byte[] connAck = new byte[4];
        await stream.ReadExactlyAsync(connAck).AsTask().WaitAsync(Timeout);
        Assert.AreEqual(0x20, connAck[0], "CONNACK");
        return connAck[3];
    }

    private string ReadLogs()
    {
        StringBuilder builder = new StringBuilder();
        foreach (string file in Directory.GetFiles(Path.Combine(tempRoot, "log"), "*.log"))
        {
            using FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new StreamReader(stream);
            builder.Append(reader.ReadToEnd());
        }
        return builder.ToString();
    }

    /// <summary>핸들러 신호가 없는 로그(Will 등)를 기다린다. 파일 sink는 버퍼링하지 않아 실행 중에도 읽힌다.</summary>
    private async Task WaitForLogAsync(string expected)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Directory.Exists(Path.Combine(tempRoot, "log")) && ReadLogs().Contains(expected, StringComparison.Ordinal))
                return;
            await Task.Delay(100);
        }
        Assert.Fail($"로그에 기대 문자열이 없다: {expected}{Environment.NewLine}{ReadLogs()}");
    }

    /// <summary>
    /// MQTT 3.1(MQIsdp, level 3) CONNECT 패킷. clean session, keepalive 60초.
    /// <paramref name="willTopic"/>이 있으면 Will(QoS 0, retain 없음), 사용자명·비밀번호가 있으면 그 플래그와 값을 넣는다.
    /// </summary>
    private static byte[] BuildMqtt31Connect(string clientId, string? willTopic, string? willMessage, string? userName, string? password)
    {
        byte flags = 0x02;                 // clean session
        if (willTopic is not null)
            flags |= 0x04;                 // will
        if (userName is not null)
            flags |= 0x80;                 // user name
        if (password is not null)
            flags |= 0x40;                 // password

        List<byte> body = [];
        AddString(body, "MQIsdp");
        body.Add(3);       // protocol level
        body.Add(flags);
        body.Add(0);
        body.Add(60);      // keepalive
        AddString(body, clientId);
        if (willTopic is not null)
        {
            AddString(body, willTopic);
            AddString(body, willMessage ?? string.Empty);
        }
        if (userName is not null)
            AddString(body, userName);
        if (password is not null)
            AddString(body, password);

        List<byte> packet = [0x10];
        int length = body.Count;
        do
        {
            byte encoded = (byte)(length % 128);
            length /= 128;
            if (length > 0)
                encoded |= 0x80;
            packet.Add(encoded);
        } while (length > 0);
        packet.AddRange(body);
        return [.. packet];
    }

    private static void AddString(List<byte> buffer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        buffer.Add((byte)(bytes.Length >> 8));
        buffer.Add((byte)(bytes.Length & 0xFF));
        buffer.AddRange(bytes);
    }
}
