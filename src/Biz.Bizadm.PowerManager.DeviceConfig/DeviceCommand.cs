namespace Biz.Bizadm.PowerManager.DeviceConfig
{
    /// <summary>
    /// 장치 SoftAP TCP 채널로 실행하는 명령.
    /// </summary>
    public enum DeviceCommand
    {
        /// <summary>apscan → Wi‑Fi 선택 → 설정 JSON → connectap</summary>
        ConnectAp,
        /// <summary>{"command":"apscan"}</summary>
        ApScan,
        /// <summary>{"command":"reboot"}</summary>
        Reboot,
        /// <summary>{"command":"factoryreset"}</summary>
        FactoryReset,
        /// <summary>[DUT&lt;-PC] SCAN_AP</summary>
        ScanAp,
        /// <summary>[DUT&lt;-PC] ON_CMD</summary>
        OnCmd,
        /// <summary>[DUT&lt;-PC] OFF_CMD</summary>
        OffCmd,
        /// <summary>[DUT&lt;-PC] MEASUREMENT</summary>
        Measurement,
        /// <summary>[DUT&lt;-PC] OVERLOAD</summary>
        OverLoad,
        /// <summary>[DUT&lt;-PC] FACTORY_RESET</summary>
        TestFactoryReset,
    }

    public static class DeviceCommandExtensions
    {
        /// <summary>
        /// 장치가 SoftAP 연결을 끝내는(공유기 접속·재부팅·초기화) 명령인지.
        /// </summary>
        public static bool IsTerminal(this DeviceCommand command) => command is
            DeviceCommand.ConnectAp or DeviceCommand.Reboot or DeviceCommand.FactoryReset or DeviceCommand.TestFactoryReset;
    }
}
