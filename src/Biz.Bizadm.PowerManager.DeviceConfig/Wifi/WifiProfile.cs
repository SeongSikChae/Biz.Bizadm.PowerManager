namespace Biz.Bizadm.PowerManager.DeviceConfig.Wifi
{
    /// <summary>장치가 접속할 공유기 Wi-Fi(복호화된 Pass).</summary>
    public sealed record WifiProfile(string Ssid, string Pass)
    {
        public override string ToString() => Ssid; // Pass가 로그·예외·콘솔에 나가지 않게
    }

    /// <summary>장치 apscan에서 보인 등록 Wi-Fi 후보.</summary>
    public sealed record WifiProfileCandidate(WifiProfile Profile, int Signal);
}
