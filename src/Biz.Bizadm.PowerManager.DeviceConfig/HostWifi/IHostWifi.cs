namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi
{
    /// <summary>
    /// PC Wi‑Fi로 스캔한 네트워크. <see cref="Signal"/>은 클수록 강하다(단위는 OS마다 다르다: Windows·Linux 0~100, macOS dBm).
    /// </summary>
    public sealed record HostWifiNetwork(string Ssid, int Signal);

    /// <summary>
    /// PC Wi‑Fi 연결.
    /// </summary>
    /// <param name="InterfaceId">무선 인터페이스. Windows는 GUID(<c>{...}</c>), Linux·macOS는 이름(<c>wlan0</c>, <c>en0</c>). <see cref="System.Net.NetworkInformation.NetworkInterface.Id"/>와 같다.</param>
    /// <param name="Ssid">SSID.</param>
    /// <param name="ProfileName">OS 연결 프로필 이름(재접속·삭제에 쓴다). macOS는 SSID.</param>
    public sealed record HostWifiConnection(string InterfaceId, string Ssid, string? ProfileName);

    /// <summary>
    /// PC Wi‑Fi 제어 실패.
    /// </summary>
    public sealed class HostWifiException(string message, Exception? innerException = null) : Exception(message, innerException)
    {
    }

    /// <summary>
    /// PC Wi‑Fi 스캔·접속·복구. 실행 OS별 구현을 <see cref="HostWifiFactory"/>가 고른다.
    /// </summary>
    public interface IHostWifi
    {
        Task<IReadOnlyList<HostWifiNetwork>> ScanAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 현재 Wi‑Fi 연결. 연결되어 있지 않으면 null.
        /// </summary>
        Task<HostWifiConnection?> GetCurrentAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 개방형(비밀번호 없음) 임시 프로필로 접속한다. 접속 요청까지만 하며 IP 할당은 호출자가 기다린다.
        /// </summary>
        Task<HostWifiConnection> ConnectOpenAsync(string ssid, CancellationToken cancellationToken);

        /// <summary>
        /// 이전 연결로 다시 접속한다.
        /// </summary>
        Task RestoreAsync(HostWifiConnection previous, CancellationToken cancellationToken);

        /// <summary>
        /// <see cref="ConnectOpenAsync"/>가 만든 임시 프로필을 지운다.
        /// </summary>
        Task RemoveTemporaryAsync(HostWifiConnection temporary, CancellationToken cancellationToken);
    }
}
