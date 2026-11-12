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
        public const string Write = "w";
        public const string Change = "chg";
        public const string Reboot = "reb";
    }
}
