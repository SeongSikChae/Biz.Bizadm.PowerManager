namespace Biz.Bizadm.PowerManager.DeviceConfig
{
    /// <summary>
    /// 장치 응답 원문. 연결을 끝내는 명령에서 응답 없이 끊기면 <see cref="Disconnected"/>가 true다.
    /// </summary>
    public sealed record DeviceResponse(string Raw, bool Disconnected = false);

    /// <summary>
    /// 장치 응답 대기 시간.
    /// </summary>
    /// <param name="Response">일반 응답 대기.</param>
    /// <param name="ApScan">apscan 응답 대기(스캔은 일반 응답보다 오래 걸릴 수 있다).</param>
    public sealed record DeviceTimeouts(TimeSpan Response, TimeSpan ApScan)
    {
        public static DeviceTimeouts Default { get; } = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
    }
}
