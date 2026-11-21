using Biz.Bizadm.PowerManager.Server.Core;
using Biz.Bizadm.PowerManager.Server.Spa.Mqtt;
using Microsoft.AspNetCore.Builder;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

/// <summary>
/// 브로커가 MQTT 패킷을 나눠 쓰지 않는지(mqtt-no-fragmentation C1·C2). 장비 MQTT 라이브러리는 여러 수신에 걸친 패킷을
/// 합치지 않는 것으로 보여, 서버 → 장치 PUBLISH가 TLS 레코드 하나에 담겨야 한다.
/// </summary>
[TestClass]
public sealed class MqttPacketFragmentationTests
{
    private const string DeviceClientId = "DAWONDNS-3a0000000401";
    private const string SubscriberMac = "3a0000000411";
    private const string DeviceKey = "DAWONDNS-M130-W-3a0000000401";
    private const string TimeSyncTopic = "dwd.v1/" + DeviceKey + "/iot-server/timesync/json";
    private const string DownlinkTopic = "dwd.v1/iot-server/" + DeviceKey;
    private const string TimeSyncJson = """{"sid":"0","msg":{"o":"tisyn","e":[]}}""";
    private const string PfxPassword = "test-password";
    private const byte TlsApplicationData = 23;
    private const int TlsRecordHeaderLength = 5;
    private const int MaxTlsPlaintext = 16384;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private string tempRoot = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.Fragmentation.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "ClientApp"));
    }

    [TestCleanup]
    public void TestCleanup() => TestApp.DeleteTempRoot(tempRoot);

    // C2: 인증서 유무와 상관없이 비TLS·TLS 엔드포인트 모두 패킷 분할을 끈다.
    [TestMethod]
    public void BuildServerOptions_DisablesPacketFragmentationOnBothEndpoints()
    {
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        string pfx = Path.Combine(tempRoot, "server.pfx");
        TestApp.WritePfx(pfx, PfxPassword);
        using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(pfx, PfxPassword);

        foreach (X509Certificate2? cert in new[] { null, certificate })
        {
            MqttServerOptions options = MqttBrokerHostedService.BuildServerOptions(configuration, cert);
            string label = cert is null ? "인증서 없음" : "인증서 있음";
            Assert.IsFalse(options.DefaultEndpointOptions.AllowPacketFragmentation, label + ": 비TLS 엔드포인트");
            Assert.IsFalse(options.TlsEndpointOptions.AllowPacketFragmentation, label + ": TLS 엔드포인트");
            Assert.AreEqual(cert is not null, options.TlsEndpointOptions.IsEnabled, label + ": TLS 엔드포인트 사용 여부");
        }
    }

    // C1: TLS 엔드포인트에서 tisyn 응답 PUBLISH가 TLS 레코드 하나로 온다(원시 수신 바이트의 레코드 머리로 판정).
    [TestMethod]
    public async Task Mqtts_TimeSyncResponse_ArrivesInSingleTlsRecord()
    {
        string thumbprint = TestApp.WritePfx(Path.Combine(tempRoot, "server.pfx"), PfxPassword);
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        configuration.Tls = new TlsConfiguration { UseTls = true, ServerCertificate = "server.pfx", ServerCertificatePassword = PfxPassword };
        using CancellationTokenSource cts = new CancellationTokenSource(Timeout);

        await using (WebApplication app = await TestApp.StartAsync(configuration, Path.Combine(tempRoot, "log")))
        {
            // 브로커는 등록 장치의 발급 키 접속만 받는다(mqtt-connect-auth).
            TestDevice subscriber = await TestDevices.RegisterAsync(app, SubscriberMac);
            TestDevice deviceIdentity = await TestDevices.RegisterAsync(app, DeviceClientId["DAWONDNS-".Length..]);
            using TcpClient tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", configuration.MqttsPort!.Value, cts.Token);
            RecordingStream recorder = new RecordingStream(tcp.GetStream());
            await using SslStream ssl = new SslStream(recorder, leaveInnerStreamOpen: false,
                (_, cert, _, _) => cert is not null && cert.GetCertHashString() == thumbprint);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                EnabledSslProtocols = SslProtocols.Tls12,
            }, cts.Token);

            await ssl.WriteAsync(BuildMqtt31Connect(subscriber.ClientId, subscriber.UserName, subscriber.Password), cts.Token);
            Assert.AreEqual(0x20, (await ReadPacketAsync(ssl, cts.Token))[0], "CONNACK");
            await ssl.WriteAsync(BuildSubscribe(DownlinkTopic), cts.Token);
            Assert.AreEqual(0x90, (await ReadPacketAsync(ssl, cts.Token))[0], "SUBACK");

            long mark = recorder.ReceivedLength;
            using (IMqttClient device = new MqttClientFactory().CreateMqttClient())
            {
                await device.ConnectAsync(new MqttClientOptionsBuilder()
                    .WithTcpServer("127.0.0.1", configuration.MqttPort!.Value)
                    .WithDevice(deviceIdentity)
                    .WithProtocolVersion(MqttProtocolVersion.V310)
                    .WithTimeout(Timeout)
                    .Build(), cts.Token);
                await device.PublishStringAsync(TimeSyncTopic, TimeSyncJson, MqttQualityOfServiceLevel.AtMostOnce, cancellationToken: cts.Token);

                byte[] publish = await ReadPacketAsync(ssl, cts.Token);
                Assert.AreEqual(0x30, publish[0], "QoS 0, retain 없는 PUBLISH");
                Assert.IsTrue(publish.Length <= MaxTlsPlaintext, $"PUBLISH가 {publish.Length}바이트라 레코드 하나에 담길 수 없다.");
                (string topic, string payload) = ParsePublish(publish);
                Assert.AreEqual(DownlinkTopic, topic);
                StringAssert.Contains(payload, "\"o\":\"w\"");

                byte[] raw = recorder.ReceivedSince(mark);
                List<(byte Type, int Length)> records = ParseTlsRecords(raw, out int leftover);
                Assert.AreEqual(0, leftover, "레코드 머리로 나누고 남은 바이트");
                Assert.AreEqual(1, records.Count,
                    $"PUBLISH({publish.Length}바이트)가 TLS 레코드 {records.Count}개로 왔다: {string.Join(", ", records.Select(r => $"{r.Type}:{r.Length}"))}");
                Assert.AreEqual(TlsApplicationData, records[0].Type);

                await device.DisconnectAsync(cancellationToken: cts.Token);
            }
            await app.StopAsync();
        }
    }

    /// <summary>MQTT 3.1(MQIsdp, level 3) CONNECT. clean session, keepalive 60초, Will 없음, 사용자명·비밀번호 있음.</summary>
    private static byte[] BuildMqtt31Connect(string clientId, string userName, string password)
    {
        List<byte> body = [];
        AddString(body, "MQIsdp");
        body.Add(3);       // protocol level
        body.Add(0xC2);    // flags: user name + password + clean session
        body.Add(0);
        body.Add(60);      // keepalive
        AddString(body, clientId);
        AddString(body, userName);
        AddString(body, password);
        return Packet(0x10, body);
    }

    /// <summary>패킷 식별자 1, 필터 하나, QoS 0 SUBSCRIBE.</summary>
    private static byte[] BuildSubscribe(string topicFilter)
    {
        List<byte> body = [0, 1];
        AddString(body, topicFilter);
        body.Add(0);
        return Packet(0x82, body);
    }

    private static byte[] Packet(byte header, List<byte> body)
    {
        List<byte> packet = [header];
        int length = body.Count;
        do
        {
            byte b = (byte)(length % 128);
            length /= 128;
            if (length > 0)
                b |= 0x80;
            packet.Add(b);
        }
        while (length > 0);
        packet.AddRange(body);
        return packet.ToArray();
    }

    private static void AddString(List<byte> target, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        target.Add((byte)(bytes.Length >> 8));
        target.Add((byte)bytes.Length);
        target.AddRange(bytes);
    }

    /// <summary>고정 헤더부터 남은 길이만큼 MQTT 패킷 하나를 통째로 읽는다.</summary>
    private static async Task<byte[]> ReadPacketAsync(Stream stream, CancellationToken ct)
    {
        List<byte> packet = [];
        byte[] one = new byte[1];
        await stream.ReadExactlyAsync(one, ct);
        packet.Add(one[0]);
        int remaining = 0;
        int multiplier = 1;
        do
        {
            await stream.ReadExactlyAsync(one, ct);
            packet.Add(one[0]);
            remaining += (one[0] & 0x7F) * multiplier;
            multiplier *= 128;
        }
        while ((one[0] & 0x80) != 0);
        byte[] body = new byte[remaining];
        await stream.ReadExactlyAsync(body, ct);
        packet.AddRange(body);
        return packet.ToArray();
    }

    /// <summary>QoS 0 PUBLISH의 토픽과 페이로드(UTF-8).</summary>
    private static (string Topic, string Payload) ParsePublish(byte[] packet)
    {
        int i = 1;
        while ((packet[i] & 0x80) != 0)
            i++;
        i++;
        int topicLength = (packet[i] << 8) | packet[i + 1];
        string topic = Encoding.UTF8.GetString(packet, i + 2, topicLength);
        int payloadStart = i + 2 + topicLength;
        return (topic, Encoding.UTF8.GetString(packet, payloadStart, packet.Length - payloadStart));
    }

    /// <summary>원시 바이트를 TLS 레코드 머리(형식 1, 버전 2, 길이 2바이트)로 나눈다.</summary>
    private static List<(byte Type, int Length)> ParseTlsRecords(byte[] raw, out int leftover)
    {
        List<(byte, int)> records = [];
        int i = 0;
        while (i + TlsRecordHeaderLength <= raw.Length)
        {
            int length = (raw[i + 3] << 8) | raw[i + 4];
            if (i + TlsRecordHeaderLength + length > raw.Length)
                break;
            records.Add((raw[i], length));
            i += TlsRecordHeaderLength + length;
        }
        leftover = raw.Length - i;
        return records;
    }

    /// <summary>
    /// 받은 바이트를 모두 복사해 두는 스트림. <see cref="SslStream"/> 아래에 끼워 TLS 레코드 경계를 본다.
    /// </summary>
    private sealed class RecordingStream(Stream inner) : Stream
    {
        private readonly MemoryStream received = new MemoryStream();

        public long ReceivedLength
        {
            get { lock (received) return received.Length; }
        }

        public byte[] ReceivedSince(long offset)
        {
            lock (received)
                return received.ToArray()[(int)offset..];
        }

        private int Record(ReadOnlySpan<byte> data)
        {
            lock (received)
                received.Write(data);
            return data.Length;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int n = await inner.ReadAsync(buffer, cancellationToken);
            return Record(buffer.Span[..n]);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
            => Record(buffer.AsSpan(offset, inner.Read(buffer, offset, count)));

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => inner.WriteAsync(buffer, cancellationToken);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => inner.WriteAsync(buffer, offset, count, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override void Flush() => inner.Flush();

        public override bool CanRead => true;

        public override bool CanWrite => true;

        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                received.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
