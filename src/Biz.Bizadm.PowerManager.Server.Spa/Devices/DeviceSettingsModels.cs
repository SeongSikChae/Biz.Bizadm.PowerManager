namespace Biz.Bizadm.PowerManager.Server.Spa.Devices
{
    // 제어 요청 본문. 빠진 값을 범위 밖과 같이 400으로 거르도록 모두 nullable로 받는다(검사는 DeviceCommandService).

    public sealed record RelayRequest(bool? On);

    public sealed record PowerCycleRequest(int? Seconds);

    public sealed record ReportConditionRequest(int? MinInterval, int? MaxInterval, int? ChangeThreshold);

    public sealed record StandbyCutoffRequest(bool? Enabled, decimal? ThresholdWatts, int? Delay);

    /// <summary>
    /// <paramref name="Days"/>는 일·월·화·수·목·금·토 순서의 7개, <paramref name="Time"/>은 <c>HH:MM:SS</c>,
    /// <paramref name="Seconds"/>는 다시 켤 때까지의 초다.
    /// </summary>
    public sealed record ScheduleRequest(bool? Enabled, IReadOnlyList<bool>? Days, string? Time, int? Seconds);

    /// <summary>
    /// 서버가 장비에 마지막으로 보낸 설정. 보낸 적이 없는 항목은 null이다.
    /// </summary>
    public sealed record DeviceSettingsResponse(
        ReportConditionSettings? ReportCondition,
        StandbyCutoffSettings? StandbyCutoff,
        ScheduleSettings? Schedule);

    public sealed record ReportConditionSettings(int MinInterval, int MaxInterval, int ChangeThreshold);

    public sealed record StandbyCutoffSettings(bool Enabled, decimal ThresholdWatts, int Delay);

    public sealed record ScheduleSettings(bool Enabled, IReadOnlyList<bool> Days, string Time, int Seconds);
}
