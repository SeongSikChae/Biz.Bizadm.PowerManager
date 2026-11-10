namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi
{
    /// <summary>PC 무선 어댑터.</summary>
    /// <param name="Id">OS 식별자(Windows는 인터페이스 GUID "{...}").</param>
    /// <param name="Name">사용자에게 보여 줄 이름.</param>
    public sealed record WifiAdapter(string Id, string Name);

    /// <summary>스캔된 Wi-Fi(PC 스캔 또는 장치 apscan).</summary>
    /// <param name="Ssid">SSID.</param>
    /// <param name="Signal">신호 세기(장치 apscan은 RSSI dBm).</param>
    public sealed record WifiNetwork(string Ssid, int Signal);
}
