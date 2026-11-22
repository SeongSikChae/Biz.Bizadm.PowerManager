namespace Biz.Bizadm.PowerManager.Core.Mqtt
{
    /// <summary>
    /// 서버 → 장치 명령의 대상. <paramref name="Mac"/>은 소문자 12자리이고, <paramref name="Prefix"/>·<paramref name="DeviceKey"/>는
    /// 그 장비가 보낸 메시지의 토픽에서 기억한 값이다.
    /// </summary>
    public sealed record DeviceCommandTarget(string Mac, string Prefix, string DeviceKey);
}
