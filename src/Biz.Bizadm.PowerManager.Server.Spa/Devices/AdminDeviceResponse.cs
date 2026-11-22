namespace Biz.Bizadm.PowerManager.Server.Spa.Devices
{
    /// <summary>
    /// 관리 화면용 장비 목록 원소. MQTT 키는 넣지 않는다.
    /// </summary>
    public sealed record AdminDeviceResponse(
        string Mac,
        string Name,
        string UserId,
        string ModelId,
        string Company,
        DateTimeOffset RegisteredAt,
        DateTimeOffset LastRequestedAt);
}
