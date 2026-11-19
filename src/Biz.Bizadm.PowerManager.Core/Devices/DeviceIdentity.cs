using System.Diagnostics.CodeAnalysis;

namespace Biz.Bizadm.PowerManager.Core.Devices
{
    /// <summary>
    /// 장치 식별 문자열 해석. mac은 12자리 16진수이며 소문자로 정규화한다.
    /// </summary>
    public static class DeviceIdentity
    {
        private const string Prefix = "DAWONDNS-";
        private const int MacLength = 12;

        /// <summary>
        /// 12자리 16진수(대소문자 무관)이면 소문자로 바꿔 돌려준다.
        /// </summary>
        public static bool TryNormalizeMac(string? value, [NotNullWhen(true)] out string? mac)
        {
            mac = null;
            if (value is null || value.Length != MacLength || !value.All(char.IsAsciiHexDigit))
                return false;
            mac = value.ToLowerInvariant();
            return true;
        }

        /// <summary>
        /// 장치 키 <c>DAWONDNS-{모델}-{mac}</c>. 모델은 비어 있지 않으며 <c>-</c>를 포함할 수 있고, mac은 마지막 <c>-</c> 뒤다.
        /// </summary>
        public static bool TryParseDeviceKey(string? deviceKey, [NotNullWhen(true)] out string? mac)
        {
            mac = null;
            if (deviceKey is null || !deviceKey.StartsWith(Prefix, StringComparison.Ordinal))
                return false;
            int last = deviceKey.LastIndexOf('-');
            int modelLength = last - Prefix.Length;
            if (modelLength <= 0)
                return false;
            return TryNormalizeMac(deviceKey[(last + 1)..], out mac);
        }

        /// <summary>
        /// MQTT ClientId <c>DAWONDNS-{mac}</c>.
        /// </summary>
        public static bool TryParseClientId(string? clientId, [NotNullWhen(true)] out string? mac)
        {
            mac = null;
            if (clientId is null || !clientId.StartsWith(Prefix, StringComparison.Ordinal))
                return false;
            return TryNormalizeMac(clientId[Prefix.Length..], out mac);
        }
    }
}
