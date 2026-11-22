using Biz.Bizadm.PowerManager.Core.Devices;
using Biz.Bizadm.PowerManager.Core.Mqtt;
using System.Collections.Concurrent;
using System.Globalization;

namespace Biz.Bizadm.PowerManager.Server.Spa.Devices
{
    /// <summary>
    /// 제어 결과. 실패면 HTTP 상태 코드와 제목을 담는다.
    /// </summary>
    public sealed record DeviceCommandResult(int StatusCode, string? Title)
    {
        public static DeviceCommandResult Success { get; } = new DeviceCommandResult(StatusCodes.Status204NoContent, null);

        public static DeviceCommandResult InvalidMac { get; } = new DeviceCommandResult(StatusCodes.Status400BadRequest, "Invalid mac");

        public static DeviceCommandResult NotFound { get; } = new DeviceCommandResult(StatusCodes.Status404NotFound, "Device not found");

        public static DeviceCommandResult OutOfRange { get; } = new DeviceCommandResult(StatusCodes.Status400BadRequest, "Value out of range");

        public static DeviceCommandResult UnsupportedModel { get; } = new DeviceCommandResult(StatusCodes.Status400BadRequest, "Unsupported model");

        public static DeviceCommandResult NotControllable { get; } = new DeviceCommandResult(StatusCodes.Status409Conflict, "Device not controllable");

        public static DeviceCommandResult NotEnqueued { get; } = new DeviceCommandResult(StatusCodes.Status409Conflict, "Device message not enqueued");
    }

    /// <summary>
    /// 장비 제어. 입력을 검사하고 제어 메시지를 보내며, 성공한 설정을 <see cref="DeviceStateStore"/>에 기억한다.
    /// 판정 순서는 mac 형식(400) → 등록(404) → 본문 범위·모델(400) → 제어 가능(409) → 큐에 넣음(409)이다.
    /// 경로와 값의 근거는 docs/reference/mqtt-device-control.md다.
    /// </summary>
    public sealed class DeviceCommandService(DeviceStatusService statuses, DeviceStateStore store, IDeviceCommandPublisher publisher)
    {
        private const string RebootModelPrefix = "B540";
        private const string PowerCyclePath = "/100/0/68";
        private const string MinIntervalPath = "/99/0/2";
        private const string MaxIntervalPath = "/99/0/3";
        private const string ChangeThresholdPath = "/99/0/4";
        private const string StandbyEnabledPath = "/100/0/40";
        private const string StandbyThresholdPath = "/100/0/41";
        private const string StandbyDelayPath = "/100/0/42";
        private const string SchedulePath = "/100/0/58";
        private const string ScheduleDeleteValue = "1,D,F,0,1111111,00:00:00,F,0";
        private const string ScheduleTimeFormat = "HH:mm:ss";
        private const int DaysPerWeek = 7;

        // 장비마다 발행과 설정 기록을 차례로 해, 나중에 보낸 값이 앞선 값으로 덮이지 않게 한다.
        private readonly ConcurrentDictionary<string, SemaphoreSlim> sendGates = new(StringComparer.Ordinal);

        public Task<DeviceCommandResult> SetRelayAsync(string mac, RelayRequest request, CancellationToken cancellationToken)
            => SendAsync(mac, _ => request.On is bool on
                ? new Command(MqttOperation.Execute, [Element(DeviceStateStore.RelayPath, Bool(on))])
                : null, cancellationToken);

        public Task<DeviceCommandResult> PowerCycleAsync(string mac, PowerCycleRequest request, CancellationToken cancellationToken)
            => SendAsync(mac, _ => request.Seconds is int seconds and >= 1 and <= 999
                ? new Command(MqttOperation.Execute, [Element(DeviceStateStore.RelayPath, Bool(false)), Element(PowerCyclePath, Int(seconds))])
                : null, cancellationToken);

        /// <summary>
        /// B540만 받는다(M130-W는 <c>reb</c>를 읽기로 처리하는 것으로 추정된다).
        /// </summary>
        public Task<DeviceCommandResult> RebootAsync(string mac, CancellationToken cancellationToken)
            => SendAsync(mac, modelId => modelId.StartsWith(RebootModelPrefix, StringComparison.Ordinal)
                ? new Command(MqttOperation.Reboot, [])
                : null, cancellationToken, DeviceCommandResult.UnsupportedModel);

        public Task<DeviceCommandResult> SetReportConditionAsync(string mac, ReportConditionRequest request, CancellationToken cancellationToken)
            => SendAsync(mac, _ =>
            {
                if (request is not { MinInterval: int min and >= 1 and <= 3600, MaxInterval: int max and >= 11 and <= 14400, ChangeThreshold: int change and >= 1 and <= 100 }
                    || min > max)
                    return null;
                ReportConditionSettings settings = new ReportConditionSettings(min, max, change);
                return new Command(MqttOperation.Write,
                    [Element(MinIntervalPath, Int(min)), Element(MaxIntervalPath, Int(max)), Element(ChangeThresholdPath, Int(change))],
                    s => s with { ReportCondition = settings });
            }, cancellationToken);

        public Task<DeviceCommandResult> SetStandbyCutoffAsync(string mac, StandbyCutoffRequest request, CancellationToken cancellationToken)
            => SendAsync(mac, _ =>
            {
                if (request is not { Enabled: bool enabled, ThresholdWatts: decimal watts and >= 0m and <= 9999.99m, Delay: int delay and >= 0 and <= 9999 }
                    || decimal.Round(watts, 2) != watts)
                    return null;
                StandbyCutoffSettings settings = new StandbyCutoffSettings(enabled, watts, delay);
                return new Command(MqttOperation.Write,
                    [
                        Element(StandbyEnabledPath, Bool(enabled)),
                        Element(StandbyThresholdPath, watts.ToString("0.00", CultureInfo.InvariantCulture)),
                        Element(StandbyDelayPath, Int(delay)),
                    ],
                    s => s with { StandbyCutoff = settings });
            }, cancellationToken);

        /// <summary>
        /// 동작은 항상 <c>U</c>다(항목이 없으면 장치가 추가로 처리한다). 시각의 초와 다시 켤 때까지의 초의 합이 60 이상이면
        /// 장치의 다시 켜는 조건이 맞지 않으므로 받지 않는다.
        /// </summary>
        public Task<DeviceCommandResult> SetScheduleAsync(string mac, ScheduleRequest request, CancellationToken cancellationToken)
            => SendAsync(mac, _ =>
            {
                if (request is not { Enabled: bool enabled, Days: { Count: DaysPerWeek } days, Time: string time, Seconds: int seconds and >= 1 and < 60 }
                    || !days.Contains(true)
                    || !TimeOnly.TryParseExact(time, ScheduleTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly at)
                    || at.Second + seconds >= 60)
                    return null;
                ScheduleSettings settings = new ScheduleSettings(enabled, [.. days], time, seconds);
                return new Command(MqttOperation.Write,
                    [Element(SchedulePath, $"1,U,{(enabled ? "T" : "F")},0,{DayBits(days)},{time},F,{Int(seconds)}")],
                    s => s with { Schedule = settings });
            }, cancellationToken);

        /// <summary>
        /// 서버가 보낸 예약이 있는지와 상관없이 보낸다.
        /// </summary>
        public Task<DeviceCommandResult> DeleteScheduleAsync(string mac, CancellationToken cancellationToken)
            => SendAsync(mac, _ => new Command(MqttOperation.Write, [Element(SchedulePath, ScheduleDeleteValue)], s => s with { Schedule = null }),
                cancellationToken);

        /// <summary>
        /// 서버가 마지막으로 보낸 설정. 실패면 설정이 null이다.
        /// </summary>
        public async Task<(DeviceCommandResult Result, DeviceSettingsResponse? Settings)> GetSettingsAsync(string mac, CancellationToken cancellationToken)
        {
            (DeviceCommandResult? failure, _) = await FindAsync(mac, cancellationToken);
            return failure is null ? (DeviceCommandResult.Success, store.Get(mac).Settings) : (failure, null);
        }

        private sealed record Command(string Operation, IReadOnlyList<MqttElement> Elements,
            Func<DeviceSettingsResponse, DeviceSettingsResponse>? Remember = null);

        /// <param name="mac">대상 장비.</param>
        /// <param name="create">모델을 받아 보낼 명령을 만든다. 받을 수 없는 입력이면 null.</param>
        /// <param name="cancellationToken">등록 조회의 취소. 발행은 결과와 로그를 남기도록 취소하지 않는다.</param>
        /// <param name="rejected">명령을 만들지 못했을 때의 결과(기본 <see cref="DeviceCommandResult.OutOfRange"/>).</param>
        private async Task<DeviceCommandResult> SendAsync(string mac, Func<string, Command?> create, CancellationToken cancellationToken,
            DeviceCommandResult? rejected = null)
        {
            (DeviceCommandResult? failure, string? modelId) = await FindAsync(mac, cancellationToken);
            if (failure is not null)
                return failure;
            if (create(modelId!) is not Command command)
                return rejected ?? DeviceCommandResult.OutOfRange;

            SemaphoreSlim gate = sendGates.GetOrAdd(mac, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                DeviceState state = store.Get(mac);
                if (!state.Controllable)
                    return DeviceCommandResult.NotControllable;
                DevicePublishResult result = await publisher.PublishAsync(state.Topic!, command.Operation, command.Elements, CancellationToken.None);
                if (!result.Enqueued)
                    return DeviceCommandResult.NotEnqueued;
                if (command.Remember is not null)
                    store.UpdateSettings(mac, command.Remember);
                return DeviceCommandResult.Success;
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<(DeviceCommandResult? Failure, string? ModelId)> FindAsync(string mac, CancellationToken cancellationToken)
        {
            // 관리 API의 mac은 소문자 12자리다. 대문자는 형식 오류로 본다.
            if (!DeviceIdentity.TryNormalizeMac(mac, out string? normalized) || normalized != mac)
                return (DeviceCommandResult.InvalidMac, null);
            DeviceStatusResponse? status = await statuses.FindAsync(mac, cancellationToken);
            return status is null ? (DeviceCommandResult.NotFound, null) : (null, status.ModelId);
        }

        private static MqttElement Element(string path, string value) => new MqttElement(path, value);

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// 일~토 순서의 선택을 장치의 요일 7글자로 바꾼다. 장치는 첫 글자(일)를 저장하지 않고 글자가 <c>0</c>·<c>1</c>인지 검사하지 않으므로,
        /// 첫 글자는 늘 <c>0</c>, 둘째 글자는 월 1과 일 2의 합, 나머지는 화~토의 <c>0</c>·<c>1</c>이다.
        /// </summary>
        private static string DayBits(IReadOnlyList<bool> days)
        {
            int mondayAndSunday = (days[1] ? 1 : 0) + (days[0] ? 2 : 0);
            return $"0{Int(mondayAndSunday)}{string.Concat(days.Skip(2).Select(d => d ? '1' : '0'))}";
        }
    }
}
