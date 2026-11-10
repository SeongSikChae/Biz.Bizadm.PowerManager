using System.Security.Cryptography;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Protocol
{
    /// <summary>
    /// 서버(#1) 발급 전까지 실행마다 쓰는 임의 mqtt_key.
    /// 덤프 설정 섹터 0x7c062·0x7d062의 값이 16자 [A-Za-z0-9/]였다.
    /// </summary>
    public static class MqttKeyGenerator
    {
        internal const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789/";
        internal const int Length = 16;

        public static string Create() => RandomNumberGenerator.GetString(Alphabet, Length);
    }
}
