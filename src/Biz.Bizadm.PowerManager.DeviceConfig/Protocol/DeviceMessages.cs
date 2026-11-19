namespace Biz.Bizadm.PowerManager.DeviceConfig.Protocol
{
    /// <summary>PC → 장치 송신 메시지. 메시지 경계는 '\n'이다.</summary>
    public static class DeviceMessages
    {
        public const string Start = "[DUT<-PC] START\n";
        public const string Hello = "hello tcp SUCCESS_CONNECT\n";

        /// <summary>공장검사 명령(SCAN_AP, ON_CMD, OFF_CMD, MEASUREMENT, OVERLOAD, FACTORY_RESET).</summary>
        public static string Test(string name) => $"[DUT<-PC] {name}\n";

        /// <summary>JSON 명령(apscan, reboot, factoryreset).</summary>
        public static string Command(string name) => $"{{\"command\":\"{name}\"}}\n";

        public static string ConnectAp(string mac) => $"{{\"mac\":\"{mac}\",\"command\":\"connectap\"}}\n";

        /// <summary>V2 흐름에서 설정 응답 뒤에 보내는 메시지(참고 구현 DelayMessage).</summary>
        public const string Delay = "{\"delay\":\"0\"}\n";
    }
}
