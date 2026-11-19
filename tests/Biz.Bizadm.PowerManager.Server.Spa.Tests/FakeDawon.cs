using Biz.Bizadm.PowerManager.Server.Spa.Relay;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System.Buffers;
using System.Security.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

/// <summary>
/// 테스트용 자체 서명 서버 인증서(localhost). 키를 보존하도록 PFX로 내보냈다가 다시 불러온다.
/// </summary>
internal static class TestCertificate
{
    public static X509Certificate2 CreateLocalhost()
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }
}

/// <summary>
/// 가짜 다원 REST 서버(Kestrel HTTPS, 자체 서명 인증서, 빈 포트). 시나리오별 응답을 돌려주고 받은 요청을 기록한다.
/// </summary>
internal sealed class FakeDawonRest : IAsyncDisposable
{
    public sealed record Received(string Method, string Path, IReadOnlyDictionary<string, string> Headers, byte[] Body);

    public sealed record Reply(int Status, string? ContentType, byte[] Body, TimeSpan Delay = default);

    private readonly WebApplication app;
    private readonly X509Certificate2 certificate;

    public ConcurrentQueue<Received> Requests { get; } = new();

    /// <summary>
    /// 받은 요청에 돌려줄 응답. 테스트가 바꾼다.
    /// </summary>
    public Func<Received, Reply> Responder { get; set; } = _ => new Reply(200, "application/json", "{}"u8.ToArray());

    public ushort Port { get; }

    private FakeDawonRest(WebApplication app, X509Certificate2 certificate, ushort port)
    {
        this.app = app;
        this.certificate = certificate;
        Port = port;
    }

    public static async Task<FakeDawonRest> StartAsync()
    {
        X509Certificate2 certificate = TestCertificate.CreateLocalhost();
        ushort port = TestApp.FreeTcpPort();
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port, listen => listen.UseHttps(certificate)));
        WebApplication app = builder.Build();
        FakeDawonRest fake = new FakeDawonRest(app, certificate, port);
        app.Run(fake.HandleAsync);
        await app.StartAsync();
        return fake;
    }

    private async Task HandleAsync(HttpContext context)
    {
        using MemoryStream body = new MemoryStream();
        await context.Request.Body.CopyToAsync(body);
        Dictionary<string, string> headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        Received received = new Received(context.Request.Method, context.Request.Path + context.Request.QueryString, headers, body.ToArray());
        Requests.Enqueue(received);

        Reply reply = Responder(received);
        if (reply.Delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(reply.Delay, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        context.Response.StatusCode = reply.Status;
        if (reply.ContentType is not null)
            context.Response.ContentType = reply.ContentType;
        await context.Response.Body.WriteAsync(reply.Body);
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
        certificate.Dispose();
    }
}

/// <summary>
/// 가짜 다원 MQTT 브로커(MQTTnet TLS 1.2, 자체 서명 인증서, 빈 포트). 접속 결과를 지정하고, 받은 접속·구독·구독 해제·발행·종료를 기록한다.
/// MQTTnet 브로커는 SUBACK을 요청 QoS 내림차순으로 만들므로, 받은 SUBSCRIBE의 필터 순서로 SUBACK을 다시 배열한다.
/// </summary>
internal sealed class FakeDawonBroker : IAsyncDisposable
{
    public sealed record Connection(string ClientId, string? UserName, byte[]? Password, ushort KeepAlive, bool CleanSession, MqttConnectPacketReader.Will? Will);

    public sealed record Subscribe(string ClientId, ushort PacketIdentifier, IReadOnlyList<(string Topic, MqttQualityOfServiceLevel QoS)> Filters);

    public sealed record Publish(string ClientId, string Topic, byte[] Payload, MqttQualityOfServiceLevel QoS, bool Retain);

    public sealed record Disconnect(string ClientId, MqttClientDisconnectType Type);

    private readonly MqttServer server;
    private readonly X509Certificate2 certificate;
    private readonly ConcurrentDictionary<(string ClientId, ushort PacketId), IReadOnlyList<(string Topic, MqttQualityOfServiceLevel QoS)>> pendingSubscribes = new();

    public ushort Port { get; }

    public ConcurrentQueue<Connection> Connections { get; } = new();
    public ConcurrentQueue<Subscribe> Subscribes { get; } = new();
    public ConcurrentQueue<(string ClientId, IReadOnlyList<string> Topics)> Unsubscribes { get; } = new();
    public ConcurrentQueue<Publish> Publishes { get; } = new();
    public ConcurrentQueue<Disconnect> Disconnects { get; } = new();

    /// <summary>
    /// 접속 결과. 기본은 성공.
    /// </summary>
    public Func<string, MqttConnectReasonCode> ConnectResult { get; set; } = _ => MqttConnectReasonCode.Success;

    /// <summary>
    /// 토픽 필터별 SUBACK 결과. 없으면 요청 QoS를 허용한다.
    /// </summary>
    public ConcurrentDictionary<string, MqttSubscribeReasonCode> SubscribeResults { get; } = new();

    private FakeDawonBroker(MqttServer server, X509Certificate2 certificate, ushort port)
    {
        this.server = server;
        this.certificate = certificate;
        Port = port;
    }

    public static async Task<FakeDawonBroker> StartAsync()
    {
        X509Certificate2 certificate = TestCertificate.CreateLocalhost();
        ushort port = TestApp.FreeTcpPort();
        MqttServerFactory factory = new MqttServerFactory();
        MqttServerOptions options = factory.CreateServerOptionsBuilder()
            .WithoutDefaultEndpoint()
            .WithEncryptedEndpoint()
            .WithEncryptedEndpointPort(port)
            .WithEncryptionCertificate(certificate)
            .WithEncryptionSslProtocol(SslProtocols.Tls12)
            .Build();
        MqttServer server = factory.CreateMqttServer(options);
        FakeDawonBroker fake = new FakeDawonBroker(server, certificate, port);
        server.ValidatingConnectionAsync += fake.OnValidatingConnectionAsync;
        server.InterceptingInboundPacketAsync += fake.OnInboundPacketAsync;
        server.InterceptingSubscriptionAsync += fake.OnSubscriptionAsync;
        server.InterceptingOutboundPacketAsync += fake.OnOutboundPacketAsync;
        server.InterceptingPublishAsync += fake.OnPublishAsync;
        server.ClientDisconnectedAsync += e =>
        {
            fake.Disconnects.Enqueue(new Disconnect(e.ClientId, e.DisconnectType));
            return Task.CompletedTask;
        };
        await server.StartAsync();
        return fake;
    }

    /// <summary>
    /// 접속을 거부할 때 같은 ClientId의 기존 접속을 먼저 끊는다(다원이 새 접속 시도로 옛 접속을 정리한 상황).
    /// </summary>
    public bool KickExistingOnReject { get; set; }

    /// <summary>
    /// CONNACK을 늦춘다. 같은 ClientId 접속이 겹치는 상황 재현용.
    /// </summary>
    public TimeSpan ConnectDelay { get; set; }

    private async Task OnValidatingConnectionAsync(ValidatingConnectionEventArgs e)
    {
        MqttConnectPacketReader.TryReadWill(e, out MqttConnectPacketReader.Will? will);
        Connections.Enqueue(new Connection(e.ClientId, e.UserName, e.RawPassword, e.KeepAlivePeriod ?? 0, e.CleanSession ?? false, will));
        if (ConnectDelay > TimeSpan.Zero)
            await DelayAsync(ConnectDelay, e.CancellationToken);
        MqttConnectReasonCode code = ConnectResult(e.ClientId);
        if (code != MqttConnectReasonCode.Success && KickExistingOnReject && (await ConnectedClientIdsAsync()).Contains(e.ClientId))
            await DisconnectClientAsync(e.ClientId);
        e.ReasonCode = code;
    }

    /// <summary>
    /// SUBSCRIBE·UNSUBSCRIBE 응답을 늦춘다(그 클라이언트의 수신 처리를 멈춤). 중계의 시간 초과 확인용.
    /// </summary>
    public TimeSpan SubscribeDelay { get; set; }
    public TimeSpan UnsubscribeDelay { get; set; }

    private async Task OnInboundPacketAsync(InterceptingPacketEventArgs e)
    {
        switch (e.Packet)
        {
            case MqttSubscribePacket subscribe:
                List<(string, MqttQualityOfServiceLevel)> filters = subscribe.TopicFilters.Select(f => (f.Topic, f.QualityOfServiceLevel)).ToList();
                pendingSubscribes[(e.ClientId, subscribe.PacketIdentifier)] = filters;
                Subscribes.Enqueue(new Subscribe(e.ClientId, subscribe.PacketIdentifier, filters));
                if (SubscribeDelay > TimeSpan.Zero)
                    await DelayAsync(SubscribeDelay, e.CancellationToken);
                break;
            case MqttUnsubscribePacket unsubscribe:
                Unsubscribes.Enqueue((e.ClientId, unsubscribe.TopicFilters.ToList()));
                if (UnsubscribeDelay > TimeSpan.Zero)
                    await DelayAsync(UnsubscribeDelay, e.CancellationToken);
                break;
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private MqttSubscribeReasonCode ResultFor(string topic, MqttQualityOfServiceLevel requested)
        => SubscribeResults.TryGetValue(topic, out MqttSubscribeReasonCode code) ? code : (MqttSubscribeReasonCode)(int)requested;

    private Task OnSubscriptionAsync(InterceptingSubscriptionEventArgs e)
    {
        MqttSubscribeReasonCode code = ResultFor(e.TopicFilter.Topic, e.TopicFilter.QualityOfServiceLevel);
        if ((int)code > 2)
        {
            e.ProcessSubscription = false;
            e.Response.ReasonCode = code;
        }
        else
        {
            e.TopicFilter.QualityOfServiceLevel = (MqttQualityOfServiceLevel)(int)code;
            e.Response.ReasonCode = code;
        }
        return Task.CompletedTask;
    }

    private Task OnOutboundPacketAsync(InterceptingPacketEventArgs e)
    {
        if (e.Packet is MqttSubAckPacket subAck && pendingSubscribes.TryRemove((e.ClientId, subAck.PacketIdentifier), out var filters))
            subAck.ReasonCodes = filters.Select(f => ResultFor(f.Topic, f.QoS)).ToList();
        return Task.CompletedTask;
    }

    private Task OnPublishAsync(InterceptingPublishEventArgs e)
    {
        MqttApplicationMessage m = e.ApplicationMessage;
        Publishes.Enqueue(new Publish(e.ClientId, m.Topic, m.Payload.ToArray(), m.QualityOfServiceLevel, m.Retain));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 다원 → 장치 메시지를 발행한다. <paramref name="retain"/>이면 retained로 저장돼 이후 구독에도 전달된다.
    /// </summary>
    public Task PublishAsync(string topic, string payload, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtMostOnce, bool retain = false)
        => server.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
            .WithTopic(topic).WithPayload(payload).WithQualityOfServiceLevel(qos).WithRetainFlag(retain).Build())
        { SenderClientId = "fake-dawon" });

    public Task DisconnectClientAsync(string clientId) => server.DisconnectClientAsync(clientId, new MqttServerClientDisconnectOptions());

    public async Task<IReadOnlyList<string>> ConnectedClientIdsAsync()
        => (await server.GetClientsAsync()).Select(c => c.Id).ToList();

    public async ValueTask DisposeAsync()
    {
        await server.StopAsync();
        server.Dispose();
        certificate.Dispose();
    }
}
