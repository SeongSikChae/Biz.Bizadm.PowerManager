using Biz.Bizadm.PowerManager.Core.Mqtt;
using Biz.Bizadm.PowerManager.Server.Core;
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
    public void TestCleanup()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

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

    private static MqttClientOptionsBuilder ClientOptions(ushort port)
        => new MqttClientOptionsBuilder()
            .WithTcpServer("127.0.0.1", port)
            .WithClientId("test-" + Guid.NewGuid().ToString("N")[..8])
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
