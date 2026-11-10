namespace Biz.Bizadm.PowerManager.DeviceConfig.Transport
{
    /// <summary>
    /// 장치 응답 프레임 종류.
    /// </summary>
    public enum DeviceFrame
    {
        /// <summary>공장검사 응답 <c>[DUT-&gt;PC] ...#</c>: '#'에서 끝난다.</summary>
        Test,
        /// <summary>JSON 응답: 중괄호 깊이가 0이 되는 '}' 또는 개행에서 끝난다.</summary>
        Json,
    }
}
