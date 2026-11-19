using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System.Buffers;
using System.Security.Authentication;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Relay
{
    /// <summary>
    /// 중계 모드의 MQTT 처리. 내장 브로커에 접속한 클라이언트마다 같은 자격 증명·Will로 다원 브로커에 대신 접속(세션)하고,
    /// 구독·구독 해제·발행·종료를 양쪽에 맞추며 양방향을 기록한다. 이벤트별 쓰임은 docs/specs/dawon-relay/plan.md 인터페이스 절을 따른다.
    /// </summary>
    public sealed class DawonMqttRelay(AppConfiguration configuration, DawonRelayRecorder recorder, ILogger<DawonMqttRelay> logger)
        : IAsyncDisposable
    {
        /// <summary>
        /// 다원 → 클라이언트 주입 메시지의 발신자 ClientId 접두. 뒤에 세션 ID가 붙는다.
        /// </summary>
        public const string InjectedSenderPrefix = "dawon-relay/";

        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly object gate = new object();
        private readonly Dictionary<string, Session> current = new(StringComparer.Ordinal);
        private readonly List<Session> sessions = [];
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> validationLocks = new(StringComparer.Ordinal);
        private MqttServer? server;

        private sealed class PendingSubscribe(ushort packetIdentifier, List<MqttTopicFilter> filters)
        {
            public ushort PacketIdentifier { get; } = packetIdentifier;
            public List<MqttTopicFilter> Filters { get; } = filters;
            public bool[] Applied { get; } = new bool[filters.Count];
            public int[]? Results { get; set; }
            public bool Failed { get; set; }
        }

        private sealed record Held(string Topic, byte[] Payload, MqttQualityOfServiceLevel QoS, bool Retain);

        private sealed class Session(string clientId, string? endPoint, IMqttClient upstream)
        {
            public string Id { get; } = Guid.NewGuid().ToString("N");
            public string ClientId { get; } = clientId;
            public string? EndPoint { get; } = endPoint;
            public IMqttClient Upstream { get; } = upstream;
            public bool Replacing { get; set; }
            public bool UpstreamLost { get; set; }
            public bool Closing { get; set; }
            public List<PendingSubscribe> Pending { get; } = [];
            public List<Held> HeldMessages { get; } = [];
            public List<(string Topic, byte[] Payload)> RetainRestore { get; } = [];
            public SemaphoreSlim Delivery { get; } = new SemaphoreSlim(1, 1);
        }

        public void Attach(MqttServer mqttServer) => server = mqttServer;

        private string DawonTarget => $"{configuration.DawonRelay!.MqttServerHost}:{configuration.DawonRelay!.MqttServerPort}";

        public static bool IsInjected(string? senderClientId)
            => senderClientId is not null && senderClientId.StartsWith(InjectedSenderPrefix, StringComparison.Ordinal);

        private Session? Find(string clientId, string? endPoint)
        {
            lock (gate)
                return sessions.FirstOrDefault(s => s.ClientId == clientId && s.EndPoint == endPoint && !s.Closing);
        }

        // ---------- 접속(C5) ----------

        /// <summary>
        /// 같은 ClientId의 접속 검증을 하나씩 처리한다. 로컬 끊기(<see cref="MqttServer.DisconnectClientAsync"/>)가 ClientId로만
        /// 대상을 찾으므로, 검증이 겹치면 거부되는 쪽이 방금 받아들인 다른 접속을 끊을 수 있다.
        /// </summary>
        public async Task OnValidatingConnectionAsync(ValidatingConnectionEventArgs e)
        {
            SemaphoreSlim validation = validationLocks.GetOrAdd(e.ClientId, _ => new SemaphoreSlim(1, 1));
            // 앞 검증은 다원 접속 시간 제한(10초)과 기록 안에서 끝나므로 그 두 배까지만 기다린다.
            if (!await validation.WaitAsync(Timeout * 2))
            {
                logger.LogWarning("Dawon relay validation queue timeout: {ClientId}", e.ClientId);
                await RecordErrorAsync(e.ClientId, DawonTarget, "validation queue timeout");
                e.ReasonCode = MqttConnectReasonCode.ServerUnavailable;
                return;
            }
            try
            {
                await ValidateAsync(e);
            }
            finally
            {
                validation.Release();
            }
        }

        private async Task ValidateAsync(ValidatingConnectionEventArgs e)
        {
            string clientId = e.ClientId;
            string? endPoint = e.RemoteEndPoint?.ToString();
            DawonRelayConfiguration relay = configuration.DawonRelay!;
            string target = $"{relay.MqttServerHost}:{relay.MqttServerPort}";

            if (!MqttConnectPacketReader.TryReadWill(e, out MqttConnectPacketReader.Will? will))
            {
                logger.LogError("Dawon relay cannot read CONNECT packet (MQTTnet internals changed): {ClientId}", clientId);
                await RecordErrorAsync(clientId, target, "cannot read CONNECT packet");
                e.ReasonCode = MqttConnectReasonCode.ServerUnavailable;
                return;
            }

            await recorder.WriteAsync("mqtt", DawonRelayRecorder.DeviceToDawon, "connect", w =>
            {
                w.WriteString("clientId", clientId);
                if (e.UserName is null) w.WriteNull("userName"); else w.WriteString("userName", e.UserName);
                w.WriteBoolean("hasPassword", e.RawPassword is { Length: > 0 });
                w.WriteNumber("keepAlive", e.KeepAlivePeriod ?? 0);
                w.WriteBoolean("cleanSession", e.CleanSession ?? true);
                if (will is null)
                    w.WriteNull("will");
                else
                {
                    w.WriteStartObject("will");
                    w.WriteString("topic", will.Topic);
                    DawonRelayRecorder.WriteBytes(w, "payload", "payloadEncoding", will.Payload);
                    w.WriteNumber("qos", (int)will.QoS);
                    w.WriteBoolean("retain", will.Retain);
                    w.WriteEndObject();
                }
            });

            IMqttClient upstream = new MqttClientFactory().CreateMqttClient();
            Session session = new Session(clientId, endPoint, upstream);
            Session? old;
            lock (gate)
            {
                old = current.GetValueOrDefault(clientId);
                if (old is not null)
                    old.Replacing = true;
                sessions.Add(session);
            }
            upstream.ApplicationMessageReceivedAsync += args => OnUpstreamMessageAsync(session, args);
            upstream.DisconnectedAsync += args => OnUpstreamDisconnectedAsync(session, args);

            int returnCode;
            try
            {
                using CancellationTokenSource timeout = new CancellationTokenSource(Timeout);
                MqttClientConnectResult result = await upstream.ConnectAsync(BuildUpstreamOptions(e, will), timeout.Token);
                returnCode = ToMqtt31ReturnCode(result.ResultCode);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Dawon relay MQTT connect failed: {ClientId}, target={Target}, error={Error}", clientId, target, ex.Message);
                await RecordErrorAsync(clientId, target, ErrorText(ex));
                returnCode = -1;
            }

            if (returnCode >= 0)
            {
                await recorder.WriteAsync("mqtt", DawonRelayRecorder.DawonToDevice, "connack", w =>
                {
                    w.WriteString("clientId", clientId);
                    w.WriteNumber("returnCode", returnCode);
                });
            }

            if (returnCode == 0)
            {
                bool committed = false;
                lock (gate)
                {
                    // 확정 전에 다원 접속이 끊겼으면(같은 ClientId의 다른 접속이 밀어낸 경우 등) 받아들이지 않는다(C8).
                    if (!session.UpstreamLost && upstream.IsConnected)
                    {
                        committed = true;
                        current[clientId] = session;
                        if (old is not null)
                        {
                            old.Replacing = false;
                            old.Closing = true;
                            sessions.Remove(old);
                        }
                    }
                }
                if (committed)
                {
                    if (old is not null)
                        old.Upstream.Dispose(); // 다원은 같은 ClientId의 새 접속으로 옛 접속을 이미 정리한다.
                    logger.LogInformation("Dawon relay MQTT session opened: {ClientId}", clientId);
                    return;
                }
                logger.LogWarning("Dawon relay MQTT disconnected before accept: {ClientId}", clientId);
                await RecordErrorAsync(clientId, target, "disconnected before accept");
                returnCode = 3;
            }

            e.ReasonCode = ToConnectReasonCode(returnCode);
            bool closeOld = false;
            lock (gate)
            {
                session.Closing = true;
                sessions.Remove(session);
                if (old is not null)
                {
                    old.Replacing = false;
                    closeOld = old.UpstreamLost && current.GetValueOrDefault(clientId) == old;
                }
            }
            upstream.Dispose();
            if (closeOld)
                DisconnectLocal(clientId);
        }

        private MqttClientOptions BuildUpstreamOptions(ValidatingConnectionEventArgs e, MqttConnectPacketReader.Will? will)
        {
            DawonRelayConfiguration relay = configuration.DawonRelay!;
            MqttClientOptionsBuilder builder = new MqttClientOptionsBuilder()
                .WithTcpServer(relay.MqttServerHost, relay.MqttServerPort!.Value)
                .WithClientId(e.ClientId)
                .WithProtocolVersion(MqttProtocolVersion.V310)
                .WithCleanSession(e.CleanSession ?? true)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(e.KeepAlivePeriod ?? 0))
                .WithTimeout(Timeout)
                .WithTlsOptions(tls =>
                {
                    tls.UseTls().WithTargetHost(relay.MqttServerHost).WithSslProtocols(SslProtocols.Tls12);
                    if (relay.SkipCertificateValidation == true)
                        tls.WithCertificateValidationHandler(_ => true);
                });
            if (e.UserName is not null)
                builder = builder.WithCredentials(e.UserName, e.RawPassword);
            if (will is not null)
            {
                builder = builder.WithWillTopic(will.Topic).WithWillPayload(will.Payload)
                    .WithWillQualityOfServiceLevel(will.QoS).WithWillRetain(will.Retain);
            }
            return builder.Build();
        }

        /// <summary>
        /// MQTTnet이 3.1 CONNACK 반환 코드를 바꿔 준 값을 3.1 코드로 되돌린다. 대응표에 없으면 3(서버 사용 불가).
        /// </summary>
        public static int ToMqtt31ReturnCode(MqttClientConnectResultCode code) => code switch
        {
            MqttClientConnectResultCode.Success => 0,
            MqttClientConnectResultCode.UnsupportedProtocolVersion => 1,
            MqttClientConnectResultCode.ClientIdentifierNotValid => 2,
            MqttClientConnectResultCode.ServerUnavailable => 3,
            MqttClientConnectResultCode.BadUserNameOrPassword => 4,
            MqttClientConnectResultCode.NotAuthorized => 5,
            _ => 3,
        };

        private static MqttConnectReasonCode ToConnectReasonCode(int returnCode) => returnCode switch
        {
            1 => MqttConnectReasonCode.UnsupportedProtocolVersion,
            2 => MqttConnectReasonCode.ClientIdentifierNotValid,
            4 => MqttConnectReasonCode.BadUserNameOrPassword,
            5 => MqttConnectReasonCode.NotAuthorized,
            _ => MqttConnectReasonCode.ServerUnavailable,
        };

        // ---------- 클라이언트 → 다원(C6·C7) ----------

        public async Task OnInboundPacketAsync(InterceptingPacketEventArgs e)
        {
            if (e.Packet is not (MqttPublishPacket or MqttSubscribePacket or MqttUnsubscribePacket))
                return;
            Session? session = Find(e.ClientId, e.RemoteEndPoint?.ToString());
            if (session is null)
                return;

            switch (e.Packet)
            {
                case MqttPublishPacket publish:
                    await ForwardPublishAsync(session, publish);
                    break;
                case MqttSubscribePacket subscribe:
                    await ForwardSubscribeAsync(session, subscribe);
                    break;
                case MqttUnsubscribePacket unsubscribe:
                    await ForwardUnsubscribeAsync(session, unsubscribe);
                    break;
            }
        }

        private async Task ForwardPublishAsync(Session session, MqttPublishPacket publish)
        {
            byte[] payload = publish.Payload.ToArray();
            await RecordPublishAsync(DawonRelayRecorder.DeviceToDawon, session.ClientId, publish.Topic, payload, publish.QualityOfServiceLevel, publish.Retain);
            try
            {
                using CancellationTokenSource timeout = new CancellationTokenSource(Timeout);
                await session.Upstream.PublishAsync(new MqttApplicationMessage
                {
                    Topic = publish.Topic,
                    Payload = new ReadOnlySequence<byte>(payload),
                    QualityOfServiceLevel = publish.QualityOfServiceLevel,
                    Retain = publish.Retain,
                }, timeout.Token);
            }
            catch (Exception ex)
            {
                // 다원 끊김은 DisconnectedAsync에서 클라이언트 종료로 이어진다(C8). 로컬 처리는 계속한다.
                logger.LogWarning("Dawon relay publish failed: {ClientId}, topic={Topic}, error={Error}", session.ClientId, publish.Topic, ex.Message);
                await RecordErrorAsync(session.ClientId, DawonTarget, "publish: " + ex.Message);
            }
        }

        private async Task ForwardSubscribeAsync(Session session, MqttSubscribePacket subscribe)
        {
            List<MqttTopicFilter> filters = subscribe.TopicFilters
                .Select(f => new MqttTopicFilter { Topic = f.Topic, QualityOfServiceLevel = f.QualityOfServiceLevel })
                .ToList();
            PendingSubscribe pending = new PendingSubscribe(subscribe.PacketIdentifier, filters);
            lock (gate)
                session.Pending.Add(pending); // 이때부터 다원 메시지를 보관한다.

            await recorder.WriteAsync("mqtt", DawonRelayRecorder.DeviceToDawon, "subscribe", w =>
            {
                w.WriteString("clientId", session.ClientId);
                w.WriteStartArray("filters");
                foreach (MqttTopicFilter f in filters)
                {
                    w.WriteStartObject();
                    w.WriteString("topic", f.Topic);
                    w.WriteNumber("qos", (int)f.QualityOfServiceLevel);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            });

            try
            {
                MqttClientSubscribeOptions options = new MqttClientSubscribeOptions { TopicFilters = filters.Select(f => new MqttTopicFilter
                {
                    Topic = f.Topic,
                    QualityOfServiceLevel = f.QualityOfServiceLevel,
                }).ToList() };
                using CancellationTokenSource timeout = new CancellationTokenSource(Timeout);
                MqttClientSubscribeResult result = await session.Upstream.SubscribeAsync(options, timeout.Token);
                int[] results = result.Items.Select(i => (int)i.ResultCode).ToArray();
                lock (gate)
                    pending.Results = results;
                await recorder.WriteAsync("mqtt", DawonRelayRecorder.DawonToDevice, "suback", w =>
                {
                    w.WriteString("clientId", session.ClientId);
                    w.WriteStartArray("results");
                    foreach (int r in results)
                        w.WriteNumberValue(r);
                    w.WriteEndArray();
                });
            }
            catch (Exception ex)
            {
                lock (gate)
                    pending.Failed = true;
                logger.LogWarning("Dawon relay subscribe failed: {ClientId}, error={Error}", session.ClientId, ex.Message);
                await RecordErrorAsync(session.ClientId, DawonTarget, "subscribe: " + ErrorText(ex));
            }
        }

        private async Task ForwardUnsubscribeAsync(Session session, MqttUnsubscribePacket unsubscribe)
        {
            List<string> topics = unsubscribe.TopicFilters.ToList();
            await recorder.WriteAsync("mqtt", DawonRelayRecorder.DeviceToDawon, "unsubscribe", w =>
            {
                w.WriteString("clientId", session.ClientId);
                w.WriteStartArray("filters");
                foreach (string t in topics)
                    w.WriteStringValue(t);
                w.WriteEndArray();
            });
            try
            {
                MqttClientUnsubscribeOptions options = new MqttClientUnsubscribeOptions { TopicFilters = topics };
                using CancellationTokenSource timeout = new CancellationTokenSource(Timeout);
                await session.Upstream.UnsubscribeAsync(options, timeout.Token);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Dawon relay unsubscribe failed: {ClientId}, error={Error}", session.ClientId, ex.Message);
                await RecordErrorAsync(session.ClientId, DawonTarget, "unsubscribe: " + ErrorText(ex));
                DisconnectLocal(session.ClientId);
            }
        }

        /// <summary>
        /// 구독 가로채기: 같은 ClientId 세션의 대기 SUBSCRIBE에서 이 필터의 미적용 항목을 찾아 다원 결과를 반영한다.
        /// </summary>
        public Task OnSubscriptionAsync(InterceptingSubscriptionEventArgs e)
        {
            lock (gate)
            {
                foreach (Session session in sessions.Where(s => s.ClientId == e.ClientId && !s.Closing))
                {
                    foreach (PendingSubscribe pending in session.Pending)
                    {
                        for (int i = 0; i < pending.Filters.Count; i++)
                        {
                            MqttTopicFilter filter = pending.Filters[i];
                            if (pending.Applied[i] || filter.Topic != e.TopicFilter.Topic || filter.QualityOfServiceLevel != e.TopicFilter.QualityOfServiceLevel)
                                continue;
                            pending.Applied[i] = true;
                            if (pending.Failed || pending.Results is null || i >= pending.Results.Length)
                            {
                                e.CloseConnection = true;
                                return Task.CompletedTask;
                            }
                            int code = pending.Results[i];
                            e.Response.ReasonCode = (MqttSubscribeReasonCode)code;
                            if (code is >= 0 and <= 2)
                                e.TopicFilter.QualityOfServiceLevel = (MqttQualityOfServiceLevel)code;
                            else
                                e.ProcessSubscription = false;
                            return Task.CompletedTask;
                        }
                    }
                }
            }
            e.CloseConnection = true;
            return Task.CompletedTask;
        }

        // ---------- 다원 → 클라이언트(C6) ----------

        private async Task OnUpstreamMessageAsync(Session session, MqttApplicationMessageReceivedEventArgs args)
        {
            MqttApplicationMessage m = args.ApplicationMessage;
            Held message = new Held(m.Topic, m.Payload.ToArray(), m.QualityOfServiceLevel, m.Retain);
            await RecordPublishAsync(DawonRelayRecorder.DawonToDevice, session.ClientId, message.Topic, message.Payload, message.QoS, message.Retain);

            await session.Delivery.WaitAsync();
            try
            {
                lock (gate)
                {
                    if (session.Closing)
                        return;
                    // 구독 처리 중이거나 아직 풀지 않은 보관 메시지가 있으면 뒤에 붙여 순서를 지킨다(풀기는 ReleaseHeldAsync).
                    if (session.Pending.Count > 0 || session.HeldMessages.Count > 0)
                    {
                        session.HeldMessages.Add(message);
                        return;
                    }
                }
                await InjectAsync(session, message);
            }
            finally
            {
                session.Delivery.Release();
            }
        }

        private async Task InjectAsync(Session session, Held message)
        {
            if (server is null)
                return;
            if (message.Retain)
            {
                lock (gate)
                    session.RetainRestore.Add((message.Topic, message.Payload));
            }
            // retain은 로컬 retained 저장소에 남지 않게 끄고, 나가는 PUBLISH에서 되돌린다.
            await server.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessage
            {
                Topic = message.Topic,
                Payload = new ReadOnlySequence<byte>(message.Payload),
                QualityOfServiceLevel = message.QoS,
                Retain = false,
            })
            { SenderClientId = InjectedSenderPrefix + session.Id });
        }

        /// <summary>
        /// 주입 메시지는 그 세션의 클라이언트에만 넣는다.
        /// </summary>
        public Task OnClientEnqueueAsync(InterceptingClientApplicationMessageEnqueueEventArgs e)
        {
            if (!IsInjected(e.SenderClientId))
                return Task.CompletedTask;
            string sessionId = e.SenderClientId[InjectedSenderPrefix.Length..];
            lock (gate)
                e.AcceptEnqueue = sessions.Any(s => s.Id == sessionId && s.ClientId == e.ReceiverClientId && !s.Closing);
            return Task.CompletedTask;
        }

        public Task OnOutboundPacketAsync(InterceptingPacketEventArgs e)
        {
            if (e.Packet is not (MqttSubAckPacket or MqttPublishPacket))
                return Task.CompletedTask;
            Session? session = Find(e.ClientId, e.RemoteEndPoint?.ToString());
            if (session is null)
                return Task.CompletedTask;

            if (e.Packet is MqttPublishPacket publish)
            {
                lock (gate)
                {
                    int index = session.RetainRestore.FindIndex(r => r.Topic == publish.Topic && publish.Payload.ToArray().AsSpan().SequenceEqual(r.Payload));
                    if (index >= 0)
                    {
                        session.RetainRestore.RemoveAt(index);
                        publish.Retain = true;
                    }
                }
                return Task.CompletedTask;
            }

            MqttSubAckPacket subAck = (MqttSubAckPacket)e.Packet;
            bool release;
            lock (gate)
            {
                PendingSubscribe? pending = session.Pending.FirstOrDefault(p => p.PacketIdentifier == subAck.PacketIdentifier);
                if (pending is null)
                    return Task.CompletedTask;
                if (!pending.Failed && pending.Results is not null)
                    subAck.ReasonCodes = pending.Results.Select(r => (MqttSubscribeReasonCode)r).ToList();
                session.Pending.Remove(pending);
                release = session.Pending.Count == 0 && session.HeldMessages.Count > 0;
            }
            if (release)
                _ = ReleaseHeldAsync(session);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 보관 메시지를 순서대로 주입한다. 목록은 주입 순서 잠금(<c>Delivery</c>)을 잡은 뒤 꺼내므로,
        /// 그사이 새로 온 메시지는 목록 끝에 붙어 보관분 뒤에 주입된다.
        /// </summary>
        private async Task ReleaseHeldAsync(Session session)
        {
            await session.Delivery.WaitAsync();
            try
            {
                while (true)
                {
                    List<Held> messages;
                    lock (gate)
                    {
                        if (session.Closing || session.Pending.Count > 0 || session.HeldMessages.Count == 0)
                            return;
                        messages = [.. session.HeldMessages];
                        session.HeldMessages.Clear();
                    }
                    foreach (Held message in messages)
                    {
                        if (session.Closing)
                            return;
                        await InjectAsync(session, message);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dawon relay held message delivery failed: {ClientId}", session.ClientId);
            }
            finally
            {
                session.Delivery.Release();
            }
        }

        // ---------- 종료(C8) ----------

        public async Task OnClientDisconnectedAsync(ClientDisconnectedEventArgs e)
        {
            if (e.DisconnectType == MqttClientDisconnectType.Takeover)
                return;
            Session? session = Find(e.ClientId, e.RemoteEndPoint?.ToString());
            if (session is null)
                return;
            lock (gate)
            {
                session.Closing = true;
                sessions.Remove(session);
                if (current.GetValueOrDefault(e.ClientId) == session)
                    current.Remove(e.ClientId);
                session.Pending.Clear();
                session.HeldMessages.Clear();
            }

            bool clean = e.DisconnectType == MqttClientDisconnectType.Clean;
            await recorder.WriteAsync("mqtt", DawonRelayRecorder.DeviceToDawon, "disconnect", w =>
            {
                w.WriteString("clientId", e.ClientId);
                w.WriteBoolean("clean", clean);
            });
            if (clean)
            {
                try
                {
                    using CancellationTokenSource timeout = new CancellationTokenSource(Timeout);
                    await session.Upstream.DisconnectAsync(new MqttClientDisconnectOptions(), timeout.Token);
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Dawon relay clean disconnect failed: {ClientId}, error={Error}", e.ClientId, ex.Message);
                }
            }
            // 비정상 끊김은 DISCONNECT 없이 끊어 다원이 Will을 발행하게 한다(Dispose는 DISCONNECT를 보내지 않는다).
            session.Upstream.Dispose();
        }

        private async Task OnUpstreamDisconnectedAsync(Session session, MqttClientDisconnectedEventArgs args)
        {
            if (!args.ClientWasConnected)
                return;
            bool disconnect;
            lock (gate)
            {
                if (session.Closing)
                    return;
                // 교체 중이거나 아직 확정 전인 세션이면 표시만 남기고, 확정·교체 처리 쪽이 이 표시를 보고 정리한다.
                session.UpstreamLost = true;
                if (session.Replacing)
                    return;
                disconnect = current.GetValueOrDefault(session.ClientId) == session;
            }
            await recorder.WriteAsync("mqtt", DawonRelayRecorder.DawonToDevice, "disconnect", w =>
            {
                w.WriteString("clientId", session.ClientId);
                w.WriteBoolean("clean", args.Reason == MqttClientDisconnectReason.NormalDisconnection);
            });
            if (disconnect)
            {
                logger.LogInformation("Dawon relay upstream disconnected, closing client: {ClientId}, reason={Reason}", session.ClientId, args.Reason);
                DisconnectLocal(session.ClientId);
            }
        }

        /// <summary>
        /// 로컬 클라이언트를 끊는다. 클라이언트 처리 흐름 안에서 불릴 수 있어 별도 작업으로 넘긴다.
        /// </summary>
        private void DisconnectLocal(string clientId)
        {
            MqttServer? s = server;
            if (s is null)
                return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await s.DisconnectClientAsync(clientId, new MqttServerClientDisconnectOptions());
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Dawon relay local disconnect failed: {ClientId}, error={Error}", clientId, ex.Message);
                }
            });
        }

        // ---------- 기록 ----------

        /// <summary>
        /// 시간 초과(우리 취소 토큰 또는 MQTTnet 클라이언트 시간 제한)는 "timeout", 그 밖에는 예외 메시지.
        /// </summary>
        private static string ErrorText(Exception ex)
            => ex is OperationCanceledException or MQTTnet.Exceptions.MqttCommunicationTimedOutException ? "timeout" : ex.Message;

        private Task RecordPublishAsync(string direction, string clientId, string topic, byte[] payload, MqttQualityOfServiceLevel qos, bool retain)
            => recorder.WriteAsync("mqtt", direction, "publish", w =>
            {
                w.WriteString("clientId", clientId);
                w.WriteString("topic", topic);
                w.WriteNumber("qos", (int)qos);
                w.WriteBoolean("retain", retain);
                DawonRelayRecorder.WriteBytes(w, "payload", "payloadEncoding", payload);
            });

        private Task RecordErrorAsync(string clientId, string target, string error)
            => recorder.WriteAsync("mqtt", DawonRelayRecorder.DawonToDevice, "error", w =>
            {
                w.WriteString("clientId", clientId);
                w.WriteString("target", target);
                w.WriteString("error", error);
            });

        public ValueTask DisposeAsync()
        {
            List<Session> all;
            lock (gate)
            {
                all = [.. sessions];
                foreach (Session s in all)
                    s.Closing = true;
                sessions.Clear();
                current.Clear();
            }
            foreach (Session s in all)
                s.Upstream.Dispose();
            server = null;
            return ValueTask.CompletedTask;
        }
    }
}
