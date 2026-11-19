using Biz.Bizadm.PowerManager.Core.Mqtt;
using Biz.Bizadm.PowerManager.Server.Core;
using Biz.Bizadm.PowerManager.Server.Spa.Devices;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

        public Task NotifyAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct) { Calls.Enqueue("notify"); return Task.CompletedTask; }

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

    [TestMethod]
    public async Task Mqtt310Client_PublishRegister_CallsRegisterOnce()
    {
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(ClientOptions(configuration.MqttPort!.Value).Build());
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
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(ClientOptions(configuration.MqttPort!.Value).Build());
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
            // MQTTnet 클라이언트의 DisconnectAsync는 DISCONNECT를 보내 Will을 폐기하므로, 원시 TCP로 MQTT 3.1 CONNECT(Will 포함) 후 끊는다.
            using (TcpClient tcp = new TcpClient())
            {
                await tcp.ConnectAsync(IPAddress.Loopback, configuration.MqttPort!.Value);
                NetworkStream stream = tcp.GetStream();
                await stream.WriteAsync(BuildMqtt31Connect("lwt-test", LwtTopic, "offline"));
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
        const string clientId = "unknown-op-test";
        const string payload = """{"sid":"0","msg":{"o":"n","e":[{"n":"/1/0/0","v":1}]}}""";
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(ClientOptions(configuration.MqttPort!.Value, clientId).Build());
            await client.PublishStringAsync(UplinkTopic, payload, MqttQualityOfServiceLevel.AtLeastOnce);
            // 같은 클라이언트의 QoS 1 발행은 순서대로 처리되므로, 뒤의 rg 수신으로 앞 메시지 처리 완료를 확인한다.
            await client.PublishStringAsync(UplinkTopic, RegisterJson, MqttQualityOfServiceLevel.AtLeastOnce);
            await handler.Registered.Task.WaitAsync(Timeout);

            await client.DisconnectAsync();
            await app.StopAsync();
        }

        CollectionAssert.AreEqual(new[] { "rg" }, handler.Calls.ToArray());
        string log = ReadLogs();
        StringAssert.Contains(log, $"Unknown device operation \"n\": \"{DeviceKey}\", topic=\"{UplinkTopic}\", clientId=\"{clientId}\", bytes={Encoding.UTF8.GetByteCount(payload)}, truncated=False, payload={payload}");
    }

    [TestMethod]
    public async Task WillOnUnparsedLwtTopic_LogsClientIdAndPayload()
    {
        const string clientId = "lwt-raw-test";
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            // 실장비처럼 장치 키 없는 /lwt를 Will 토픽으로 등록하고 DISCONNECT 없이 끊는다.
            using (TcpClient tcp = new TcpClient())
            {
                await tcp.ConnectAsync(IPAddress.Loopback, configuration.MqttPort!.Value);
                NetworkStream stream = tcp.GetStream();
                await stream.WriteAsync(BuildMqtt31Connect(clientId, "/lwt", "offline"));
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

    [TestMethod]
    public async Task UnparsedTopic_LargePayload_IsTruncatedTo4096Bytes()
    {
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(ClientOptions(configuration.MqttPort!.Value).Build());
            await client.PublishStringAsync("test/large", new string('a', 5000), MqttQualityOfServiceLevel.AtLeastOnce);

            await WaitForLogAsync("Unparsed topic passed through: \"test/large\"");
            await client.DisconnectAsync();
            await app.StopAsync();
        }

        string log = ReadLogs();
        StringAssert.Contains(log, "bytes=5000, truncated=True, payload=" + new string('a', 4096));
        Assert.IsFalse(log.Contains(new string('a', 4097)), "4096바이트를 넘는 페이로드가 로그에 남았다.");
    }

    [TestMethod]
    public async Task Mqtts_Tls12_PublishRegister_CallsRegister_AndTls13OnlyFails()
    {
        string thumbprint = WritePfx(Path.Combine(tempRoot, "server.pfx"));
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        configuration.Tls = new TlsConfiguration { UseTls = true, ServerCertificate = "server.pfx", ServerCertificatePassword = PfxPassword };
        RecordingHandler handler = new RecordingHandler();

        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            ushort port = configuration.MqttsPort!.Value;
            using IMqttClient client = new MqttClientFactory().CreateMqttClient();
            await client.ConnectAsync(ClientOptions(port).WithTlsOptions(tls => tls
                .UseTls()
                .WithSslProtocols(SslProtocols.Tls12)
                .WithTargetHost("localhost")
                .WithCertificateValidationHandler(args => args.Certificate.GetCertHashString() == thumbprint)).Build());
            await client.PublishStringAsync(UplinkTopic, RegisterJson, MqttQualityOfServiceLevel.AtLeastOnce);
            await handler.Registered.Task.WaitAsync(Timeout);
            await client.DisconnectAsync();

            using IMqttClient tls13Client = new MqttClientFactory().CreateMqttClient();
            await Assert.ThrowsAsync<Exception>(() => tls13Client.ConnectAsync(ClientOptions(port).WithTlsOptions(tls => tls
                .UseTls()
                .WithSslProtocols(SslProtocols.Tls13)
                .WithTargetHost("localhost")
                .WithCertificateValidationHandler(_ => true)).WithTimeout(TimeSpan.FromSeconds(5)).Build()));

            await app.StopAsync();
        }
        CollectionAssert.AreEqual(new[] { "rg" }, handler.Calls.ToArray());
    }

    // C8: 접속마다 자격 증명 판정을 관찰 로그로 남기고, 접속은 모두 받는다. 비밀번호 값은 남기지 않는다.
    [TestMethod]
    public async Task ValidatingConnection_LogsPasswordCheckAndAcceptsAll()
    {
        RecordingHandler handler = new RecordingHandler();
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        string wrongPassword = "WrongPassword9876";
        string mqttKey;
        await using (WebApplication app = await StartAsync(configuration, handler))
        {
            mqttKey = (await app.Services.GetRequiredService<DeviceRegistrationService>()
                .RegisterAsync("aabbccddeeff", "user@example.com/kakao", "B540_W", "DAWONDNS", CancellationToken.None)).MqttKey;

            (string ClientId, string? UserName, string? Password, string Expected)[] cases =
            [
                ("DAWONDNS-AABBCCDDEEFF", DeviceKey, mqttKey, "hasPassword=True, passwordCheck=\"Match\""),
                ("DAWONDNS-aabbccddeeff", DeviceKey, wrongPassword, "hasPassword=True, passwordCheck=\"Mismatch\""),
                ("DAWONDNS-aabbccddeeff", DeviceKey, null, "hasPassword=False, passwordCheck=\"Mismatch\""),
                ("DAWONDNS-112233445566", "DAWONDNS-B540_W-112233445566", mqttKey, "hasPassword=True, passwordCheck=\"Unknown\""),
                ("client-not-device", null, null, "hasPassword=False, passwordCheck=\"Unknown\""),
            ];
            foreach ((string clientId, string? userName, string? password, string expected) in cases)
            {
                MqttClientOptionsBuilder options = ClientOptions(configuration.MqttPort!.Value, clientId);
                if (userName is not null)
                    options = password is null ? options.WithCredentials(userName) : options.WithCredentials(userName, password);
                using IMqttClient client = new MqttClientFactory().CreateMqttClient();
                MqttClientConnectResult result = await client.ConnectAsync(options.Build());
                Assert.AreEqual(MqttClientConnectResultCode.Success, result.ResultCode, clientId);
                await WaitForLogAsync($"MQTT client connecting: clientId=\"{clientId}\", userName={(userName is null ? "null" : $"\"{userName}\"")}, {expected}");
                await client.DisconnectAsync();
            }
            await app.StopAsync();
        }

        string log = ReadLogs();
        Assert.IsFalse(log.Contains(mqttKey, StringComparison.Ordinal), "로그에 mqtt_key가 있다");
        Assert.IsFalse(log.Contains(wrongPassword, StringComparison.Ordinal), "로그에 비밀번호가 있다");
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

    private static string WritePfx(string path)
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, PfxPassword));
        return certificate.Thumbprint;
    }

    /// <summary>MQTT 3.1(MQIsdp, level 3) CONNECT 패킷. clean session + Will(QoS 0, retain 없음), keepalive 60초.</summary>
    private static byte[] BuildMqtt31Connect(string clientId, string willTopic, string willMessage)
    {
        List<byte> body = [];
        AddString(body, "MQIsdp");
        body.Add(3);       // protocol level
        body.Add(0x06);    // flags: will + clean session
        body.Add(0);
        body.Add(60);      // keepalive
        AddString(body, clientId);
        AddString(body, willTopic);
        AddString(body, willMessage);

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
