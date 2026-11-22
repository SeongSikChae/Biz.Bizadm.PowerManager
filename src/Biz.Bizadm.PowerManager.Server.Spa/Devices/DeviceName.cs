using System.Diagnostics.CodeAnalysis;

namespace Biz.Bizadm.PowerManager.Server.Spa.Devices
{
    /// <summary>
    /// 사용자가 정한 장비명의 규칙. 앞뒤 공백을 먼저 없애고, 없앤 값의 길이(UTF-16 코드 단위)와 문자를 검사한다.
    /// </summary>
    public static class DeviceName
    {
        public const int MaxLength = 30;

        // 문자 범주가 제어 문자가 아니지만 줄을 나누는 문자.
        private const char LineSeparator = (char)0x2028;
        private const char ParagraphSeparator = (char)0x2029;

        /// <summary>
        /// 저장할 장비명을 만든다. 빈 문자열은 장비명을 지운다는 뜻이다. null이거나 길거나 쓸 수 없는 문자가 있으면 false.
        /// </summary>
        public static bool TryNormalize(string? value, [NotNullWhen(true)] out string? name)
        {
            name = value?.Trim();
            if (name is not null && name.Length <= MaxLength
                && !name.Any(c => char.IsControl(c) || c is LineSeparator or ParagraphSeparator))
                return true;
            name = null;
            return false;
        }
    }
}
