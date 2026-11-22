namespace Biz.Bizadm.PowerManager.Core.Mqtt
{
    /// <summary>
    /// <see cref="MqttMessage.Operation"/>(<c>msg.o</c>) 값.
    /// </summary>
    public static class MqttOperation
    {
        public const string Register = "rg";
        public const string Deregister = "drg";
        public const string TimeSync = "tisyn";
        public const string Notify = "notify";
        /// <summary>
        /// <see cref="Notify"/>의 짧은 형태. M130-W 실장비(펌웨어 2.01.58)가 상태 보고에 쓰는 것을 관찰했다(토픽 접미 <c>notify/json</c>).
        /// </summary>
        public const string NotifyShort = "n";
        public const string Write = "w";
        public const string Change = "chg";
        public const string Reboot = "reb";
        public const string Execute = "e";
        public const string Read = "r";
    }
}
