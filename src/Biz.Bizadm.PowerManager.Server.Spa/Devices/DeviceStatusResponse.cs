namespace Biz.Bizadm.PowerManager.Server.Spa.Devices
{
    /// <summary>
    /// 관리 화면용 장비 상태 원소. MQTT 키는 넣지 않는다. <paramref name="Relay"/>는 <c>on</c>·<c>off</c>·<c>unknown</c>이다.
    /// </summary>
    public sealed record DeviceStatusResponse(
        string Mac,
        string ModelId,
        bool Online,
        string Relay,
        double? Voltage,
        double? Current,
        double? PowerFactor,
        double? Power,
        DateTimeOffset? LastReportAt,
        bool Controllable);
}
