namespace Biz.Bizadm.PowerManager.DeviceConfig.Transport
{
    /// <summary>설정 파일 Commands에 쓰는 장치 명령.</summary>
    public enum DeviceCommand
    {
        ConnectAp,
        ApScan,
        Reboot,
        FactoryReset,
        ScanAp,
        OnCmd,
        OffCmd,
        Measurement,
        OverLoad,
        TestFactoryReset,
    }
}
