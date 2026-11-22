using Biz.Bizadm.PowerManager.Core.Devices;
using Biz.Bizadm.PowerManager.Core.Mqtt;
using MQTTnet.Server;
using System.Globalization;
using System.Threading.Channels;

namespace Biz.Bizadm.PowerManager.Server.Spa.Devices
{
    public enum RelayState
    {
        Unknown,
        On,
        Off,
    }

    /// <summary>
    /// 장비 하나의 현재 상태 사본. <see cref="Topic"/>은 장비가 보낸 메시지의 토픽(접두·장치 키)이고 아직 모르면 null이다.
    /// </summary>
    public sealed record DeviceState(
        bool Online,
        RelayState Relay,
        double? Voltage,
        double? Current,
        double? PowerFactor,
        double? Power,
        DateTimeOffset? LastReportAt,
        DeviceCommandTarget? Topic,
        DeviceSettingsResponse Settings)
    {
        public static DeviceState Empty { get; } = new DeviceState(false, RelayState.Unknown, null, null, null, null, null, null,
            new DeviceSettingsResponse(null, null, null));

        /// <summary>
        /// 온라인이고 제어 토픽을 안다.
        /// </summary>
        public bool Controllable => Online && Topic is not null;
    }

    /// <summary>
    /// 보낼 릴레이 읽기. <paramref name="Connection"/>은 이 읽기를 정한 접속의 순번이라, 그 사이 다시 접속했으면 결과를 버린다.
    /// </summary>
    public sealed record RelayReadRequest(DeviceCommandTarget Target, long Connection);

    /// <summary>
    /// 장비별 최신 상태와 마지막으로 보낸 설정을 메모리에 둔다(서버를 다시 시작하면 사라진다).
    /// 브로커가 접속·끊김과 장치 → 서버 메시지를 알리고, 바뀐 장비의 mac을 구독자에게 알린다.
    /// </summary>
    public sealed class DeviceStateStore(TimeProvider timeProvider, ILogger<DeviceStateStore> logger)
    {
        public const string RelayReadPath = "/100/0/30";
        public const string RelayPath = "/100/0/31";
        private const string VoltagePath = "/100/0/1";
        private const string CurrentPath = "/100/0/2";
        private const string PowerFactorPath = "/100/0/3";
        private const string PowerPath = "/100/0/11";

        private enum RelayReadState
        {
            None,
            Sending,
            Enqueued,
        }

        private sealed class Entry
        {
            public DeviceState State { get; set; } = DeviceState.Empty;

            /// <summary>
            /// 접속할 때마다 늘린다. 이전 접속에서 시작한 읽기의 결과를 가린다.
            /// </summary>
            public long Connection { get; set; }

            /// <summary>
            /// 지금 접속의 원격 주소. 옛 연결의 끊김 이벤트가 새 접속 뒤에 와도 오프라인으로 바꾸지 않게 비교한다.
            /// </summary>
            public string? RemoteEndPoint { get; set; }

            public RelayReadState RelayRead { get; set; }
        }

        private readonly Lock gate = new Lock();
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly HashSet<DeviceChangeSubscription> subscriptions = [];

        /// <summary>
        /// 지금 열린 변경 알림 구독 수.
        /// </summary>
        public int SubscriptionCount
        {
            get
            {
                lock (gate)
                    return subscriptions.Count;
            }
        }

        public DeviceState Get(string mac)
        {
            lock (gate)
                return entries.TryGetValue(mac, out Entry? entry) ? entry.State : DeviceState.Empty;
        }

        /// <summary>
        /// 장비 클라이언트가 접속했다. 온라인으로 바꾸고 이번 접속의 읽기 상태를 지운다.
        /// </summary>
        public void Connected(string clientId, string? remoteEndPoint)
        {
            if (!DeviceIdentity.TryParseClientId(clientId, out string? mac))
                return;
            lock (gate)
            {
                Entry entry = GetOrAdd(mac);
                entry.Connection++;
                entry.RemoteEndPoint = remoteEndPoint;
                entry.RelayRead = RelayReadState.None;
                Update(mac, entry, entry.State with { Online = true });
            }
        }

        /// <summary>
        /// 장비 클라이언트의 연결이 끊겼다. 같은 ClientId의 새 접속으로 교체된 것이거나(<see cref="MqttClientDisconnectType.Takeover"/>)
        /// 지금 접속의 연결이 아니면 무시한다.
        /// </summary>
        public void Disconnected(string clientId, string? remoteEndPoint, MqttClientDisconnectType disconnectType)
        {
            if (disconnectType == MqttClientDisconnectType.Takeover || !DeviceIdentity.TryParseClientId(clientId, out string? mac))
                return;
            lock (gate)
            {
                Entry entry = GetOrAdd(mac);
                if (entry.RemoteEndPoint == remoteEndPoint)
                    Update(mac, entry, entry.State with { Online = false });
            }
        }

        /// <summary>
        /// 장치 → 서버 메시지를 반영한다. 한 메시지 안에서는 토픽 기억, 상태 보고 반영, 릴레이 읽기 판정 순서로 하며,
        /// 릴레이 읽기를 보내야 하면 그 요청을 돌려준다(돌려준 요청은 <see cref="CompleteRelayRead"/>로 끝내야 한다).
        /// 토픽 장치 키에 보낸 클라이언트의 mac이 없으면 Warning 로그만 남긴다.
        /// </summary>
        /// <param name="clientId">보낸 클라이언트의 ClientId.</param>
        /// <param name="topic">해석한 장치 → 서버 토픽.</param>
        /// <param name="envelope">해석한 본문. LWT이거나 해석하지 못했으면 null.</param>
        public RelayReadRequest? ApplyMessage(string? clientId, DeviceTopic topic, MqttEnvelope? envelope)
        {
            if (!DeviceIdentity.TryParseClientId(clientId, out string? mac)
                || !topic.DeviceKey.Split('-').Contains(mac, StringComparer.OrdinalIgnoreCase))
            {
                logger.LogWarning("Device message ignored for device state: clientId={ClientId}, prefix={Prefix}, device={DeviceKey}",
                    clientId, topic.Prefix, topic.DeviceKey);
                return null;
            }

            lock (gate)
            {
                Entry entry = GetOrAdd(mac);
                DeviceCommandTarget target = new DeviceCommandTarget(mac, topic.Prefix, topic.DeviceKey);
                DeviceState state = entry.State with { Topic = target };
                if (envelope?.Msg.Operation is MqttOperation.Notify or MqttOperation.NotifyShort)
                    state = ApplyReport(state, envelope.Msg.Elements);
                Update(mac, entry, state);

                if (state.Relay != RelayState.Unknown || entry.RelayRead != RelayReadState.None)
                    return null;
                entry.RelayRead = RelayReadState.Sending;
                return new RelayReadRequest(target, entry.Connection);
            }
        }

        /// <summary>
        /// 릴레이 읽기의 결과. 큐에 넣었으면 이번 접속에서 다시 보내지 않고, 넣지 못했으면 다음 메시지 때 다시 보낸다.
        /// </summary>
        public void CompleteRelayRead(RelayReadRequest request, bool enqueued)
        {
            lock (gate)
            {
                Entry entry = GetOrAdd(request.Target.Mac);
                if (entry.Connection == request.Connection && entry.RelayRead == RelayReadState.Sending)
                    entry.RelayRead = enqueued ? RelayReadState.Enqueued : RelayReadState.None;
            }
        }

        /// <summary>
        /// 장비에 보낸 설정을 기억한다. 설정은 상태 원소에 없으므로 구독자에게 알리지 않는다.
        /// </summary>
        public void UpdateSettings(string mac, Func<DeviceSettingsResponse, DeviceSettingsResponse> update)
        {
            lock (gate)
            {
                Entry entry = GetOrAdd(mac);
                entry.State = entry.State with { Settings = update(entry.State.Settings) };
            }
        }

        /// <summary>
        /// 저장소 밖의 값(등록 정보)이 바뀌어 장비의 상태 원소가 달라졌음을 알린다.
        /// </summary>
        public void NotifyChanged(string mac)
        {
            lock (gate)
            {
                foreach (DeviceChangeSubscription subscription in subscriptions)
                    subscription.Add(mac);
            }
        }

        public DeviceChangeSubscription Subscribe()
        {
            DeviceChangeSubscription subscription = new DeviceChangeSubscription(Unsubscribe);
            lock (gate)
                subscriptions.Add(subscription);
            return subscription;
        }

        private void Unsubscribe(DeviceChangeSubscription subscription)
        {
            lock (gate)
                subscriptions.Remove(subscription);
        }

        private Entry GetOrAdd(string mac)
        {
            if (!entries.TryGetValue(mac, out Entry? entry))
            {
                entry = new Entry();
                entries.Add(mac, entry);
            }
            return entry;
        }

        private void Update(string mac, Entry entry, DeviceState state)
        {
            if (entry.State == state)
                return;
            entry.State = state;
            foreach (DeviceChangeSubscription subscription in subscriptions)
                subscription.Add(mac);
        }

        private DeviceState ApplyReport(DeviceState state, IReadOnlyList<MqttElement> elements)
        {
            state = state with { LastReportAt = timeProvider.GetUtcNow() };
            foreach (MqttElement? element in elements)
            {
                string? value = element?.StringValue;
                state = element?.Path switch
                {
                    RelayReadPath or RelayPath => value switch
                    {
                        "true" => state with { Relay = RelayState.On },
                        "false" => state with { Relay = RelayState.Off },
                        _ => state,
                    },
                    VoltagePath => state with { Voltage = ParseNumber(value) ?? state.Voltage },
                    CurrentPath => state with { Current = ParseNumber(value) ?? state.Current },
                    PowerFactorPath => state with { PowerFactor = ParseNumber(value) ?? state.PowerFactor },
                    PowerPath => state with { Power = ParseNumber(value) ?? state.Power },
                    _ => state,
                };
            }
            return state;
        }

        /// <summary>
        /// NaN·무한대는 JSON 숫자로 내보낼 수 없으므로 숫자로 보지 않는다.
        /// </summary>
        private static double? ParseNumber(string? value)
            => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)
                ? number
                : null;
    }

    /// <summary>
    /// 바뀐 장비의 mac 알림. 읽기 전에 같은 장비의 알림이 쌓이면 하나로 합친다.
    /// </summary>
    public sealed class DeviceChangeSubscription(Action<DeviceChangeSubscription> unsubscribe) : IDisposable
    {
        private readonly Lock gate = new Lock();
        private readonly HashSet<string> pending = new HashSet<string>(StringComparer.Ordinal);
        private readonly Channel<bool> signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

        internal void Add(string mac)
        {
            lock (gate)
                pending.Add(mac);
            signal.Writer.TryWrite(true);
        }

        /// <summary>
        /// 알림이 올 때까지 기다린 뒤 그동안 바뀐 장비의 mac을 모두 꺼낸다.
        /// </summary>
        public async Task<IReadOnlyList<string>> ReadAsync(CancellationToken cancellationToken)
        {
            await signal.Reader.ReadAsync(cancellationToken);
            lock (gate)
            {
                List<string> macs = [.. pending];
                pending.Clear();
                return macs;
            }
        }

        public void Dispose() => unsubscribe(this);
    }
}
