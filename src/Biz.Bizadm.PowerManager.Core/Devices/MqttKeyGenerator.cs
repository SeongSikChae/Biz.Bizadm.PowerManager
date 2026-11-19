using System.Security.Cryptography;

namespace Biz.Bizadm.PowerManager.Core.Devices
{
    /// <summary>
    /// 장치 MQTT 접속 키 생성. 펌웨어는 16자 이상만 받으며, 문자는 <see cref="DeviceVerify.Alphabet"/> 안의 영문·숫자로 둔다.
    /// </summary>
    public static class MqttKeyGenerator
    {
        public const int Length = 16;

        /// <summary>
        /// 고를 문자 목록(대문자·소문자·숫자 62자). <c>GetString</c>은 이 문자열의 각 문자 중에서 고른다.
        /// </summary>
        public const string Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        public static string Create() => RandomNumberGenerator.GetString(Characters, Length);
    }
}
