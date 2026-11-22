namespace Biz.Bizadm.PowerManager.Server.Spa.Devices
{
    /// <summary>
    /// 장비명 저장 요청. 앞뒤 공백을 없앤 값이 빈 문자열이면 장비명을 지운다.
    /// </summary>
    public sealed record DeviceNameRequest(string? Name);
}
