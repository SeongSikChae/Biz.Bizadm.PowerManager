namespace Biz.Bizadm.PowerManager.DeviceConfig.Transport
{
    /// <summary>
    /// 장치 SoftAP TCP 채널.
    /// </summary>
    public interface IDeviceChannel : IAsyncDisposable
    {
        /// <summary>
        /// 한 줄을 보낸다(끝에 '\n'을 붙인다).
        /// </summary>
        /// <param name="line">보낼 내용.</param>
        /// <param name="logText">로그에 남길 내용. 민감 값을 가린 문자열을 넘긴다. null이면 <paramref name="line"/>.</param>
        Task SendLineAsync(string line, CancellationToken cancellationToken, string? logText = null);

        /// <summary>
        /// 프레임 하나를 받는다. 비요청 버튼 이벤트는 로그로 남기고 건너뛴다.
        /// </summary>
        /// <exception cref="TimeoutException"><paramref name="timeout"/> 안에 프레임이 완성되지 않았다.</exception>
        /// <exception cref="DeviceDisconnectedException">장치가 연결을 끊었다.</exception>
        Task<string> ReceiveAsync(DeviceFrame frame, TimeSpan timeout, CancellationToken cancellationToken);
    }
}
