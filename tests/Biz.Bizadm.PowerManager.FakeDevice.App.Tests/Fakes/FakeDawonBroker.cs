using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System.Collections.Concurrent;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Tests.Fakes;

/// <summary>
/// 가짜 다원 MQTT 브로커(MQTTnet 서버, TLS 1.2 끝점, 자체 서명 인증서, 빈 포트). 접속·구독·발행·끊김을 기록하고,
/// 접속 거부 코드·CONNACK 지연·SUBACK 결과·SUBACK 지연을 설정할 수 있다.
/// Will은 MQTTnet 5 브로커 이벤트가 공개 속성으로 주지 않아 기록하지 않는다(plan 결정 절, 옵션 단위 테스트로 확인).
/// </summary>
internal sealed class FakeDawonBroker : IAsyncDisposable
{
    public sealed record Connection(MqttProtocolVersion ProtocolVersion, string ClientId, string? UserName, byte[]? Password, ushort KeepAlive, bool CleanSession);

    public sealed record Subscribe(string ClientId, IReadOnlyList<(string Topic, MqttQualityOfServiceLevel QoS)> Filters);

    public sealed record Publish(string ClientId, string Topic, byte[] Payload, MqttQualityOfServiceLevel QoS, bool Retain, DateTimeOffset At);

    public sealed record Disconnect(string ClientId, MqttClientDisconnectType Type);

    private readonly MqttServer server;
    private readonly X509Certificate2 certificate;
    private readonly ConcurrentDictionary<(string ClientId, ushort PacketId), IReadOnlyList<(string Topic, MqttQualityOfServiceLevel QoS)>> pendingSubscribes = new();

    public ushort Port { get; }

    public ConcurrentQueue<Connection> Connections { get; } = new();
    public ConcurrentQueue<Subscribe> Subscribes { get; } = new();
    public ConcurrentQueue<Publish> Publishes { get; } = new();
    public ConcurrentQueue<Disconnect> Disconnects { get; } = new();

    /// <summary>접속 결과. 기본은 성공.</summary>
    public MqttConnectReasonCode ConnectResult { get; set; } = MqttConnectReasonCode.Success;

    /// <summary>CONNACK을 늦춘다.</summary>
    public TimeSpan ConnectDelay { get; set; }

    /// <summary>토픽 필터별 SUBACK 결과. 없으면 요청 QoS를 허용한다.</summary>
    public ConcurrentDictionary<string, MqttSubscribeReasonCode> SubscribeResults { get; } = new();

    /// <summary>SUBSCRIBE 처리를 늦춘다(그 클라이언트의 수신 처리를 멈춤).</summary>
    public TimeSpan SubscribeDelay { get; set; }

    private FakeDawonBroker(MqttServer server, X509Certificate2 certificate, ushort port)
    {
        this.server = server;
        this.certificate = certificate;
        Port = port;
    }

    public static async Task<FakeDawonBroker> StartAsync()
    {
        X509Certificate2 certificate = TestCertificate.CreateLocalhost();
        ushort port = TestHarness.FreeTcpPort();
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

    private async Task OnValidatingConnectionAsync(ValidatingConnectionEventArgs e)
    {
        Connections.Enqueue(new Connection(e.ProtocolVersion, e.ClientId, e.UserName, e.RawPassword, e.KeepAlivePeriod ?? 0, e.CleanSession ?? false));
        if (ConnectDelay > TimeSpan.Zero)
            await DelayAsync(ConnectDelay, e.CancellationToken);
        e.ReasonCode = ConnectResult;
    }

    private async Task OnInboundPacketAsync(InterceptingPacketEventArgs e)
    {
        if (e.Packet is not MqttSubscribePacket subscribe)
            return;
        List<(string, MqttQualityOfServiceLevel)> filters = subscribe.TopicFilters.Select(f => (f.Topic, f.QualityOfServiceLevel)).ToList();
        pendingSubscribes[(e.ClientId, subscribe.PacketIdentifier)] = filters;
        Subscribes.Enqueue(new Subscribe(e.ClientId, filters));
        if (SubscribeDelay > TimeSpan.Zero)
            await DelayAsync(SubscribeDelay, e.CancellationToken);
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
            e.ProcessSubscription = false;
        else
            e.TopicFilter.QualityOfServiceLevel = (MqttQualityOfServiceLevel)(int)code;
        e.Response.ReasonCode = code;
        return Task.CompletedTask;
    }

    // MQTTnet 브로커는 SUBACK을 요청 QoS 순으로 만들 수 있어, 받은 SUBSCRIBE의 필터 순서로 결과를 다시 배열한다
    private Task OnOutboundPacketAsync(InterceptingPacketEventArgs e)
    {
        if (e.Packet is MqttSubAckPacket subAck && pendingSubscribes.TryRemove((e.ClientId, subAck.PacketIdentifier), out var filters))
            subAck.ReasonCodes = filters.Select(f => ResultFor(f.Topic, f.QoS)).ToList();
        return Task.CompletedTask;
    }

    private Task OnPublishAsync(InterceptingPublishEventArgs e)
    {
        MqttApplicationMessage m = e.ApplicationMessage;
        Publishes.Enqueue(new Publish(e.ClientId, m.Topic, System.Buffers.BuffersExtensions.ToArray(m.Payload), m.QualityOfServiceLevel, m.Retain, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    /// <summary>다원 → 장치 메시지를 발행한다.</summary>
    public Task PublishAsync(string topic, string payload)
        => server.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
            .WithTopic(topic).WithPayload(payload).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce).Build())
        { SenderClientId = "fake-dawon" });

    public Task DisconnectClientAsync(string clientId) => server.DisconnectClientAsync(clientId, new MqttServerClientDisconnectOptions());

    public async ValueTask DisposeAsync()
    {
        await server.StopAsync();
        server.Dispose();
        certificate.Dispose();
    }
}
