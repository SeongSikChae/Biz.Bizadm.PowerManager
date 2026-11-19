using System.Text;

namespace Biz.Bizadm.PowerManager.Core.Devices
{
    /// <summary>
    /// 장치 등록 응답의 <c>verify</c> 계산. 장치(B540 펌웨어 <c>0x4021c5ec</c>)는 응답의 <c>verify</c>가
    /// 이 값과 44자까지 같을 때만 <c>mqtt_key</c>를 받아들인다. 실제 다원 응답과 일치함을 확인했다(2026-11-19).
    /// </summary>
    public static class DeviceVerify
    {
        /// <summary>
        /// 펌웨어의 치환표(67자). 계산에 쓰는 모든 문자는 이 표 안에 있어야 한다.
        /// </summary>
        public const string Alphabet = "XGAqW.Ibj3xdCRFzf60oMKNDYPaTQHLUhls4v1mei5S9w-r8n=kJtV/O+7ZEu2cgypB";

        public const int Length = 44;

        /// <summary>
        /// <paramref name="serverAddr"/>(장치에 설정된 MQTT 서버 주소)와 소문자 <paramref name="mac"/>을 이은 문자열 s,
        /// <paramref name="mqttKey"/> k에 대해 i = 0..43마다
        /// A[(pos(s[i mod |s|])·sum(k) + pos(k[i mod |k|])·sum(s)) mod 67]을 구한다.
        /// </summary>
        public static string Compute(string serverAddr, string mac, string mqttKey)
        {
            ArgumentException.ThrowIfNullOrEmpty(serverAddr);
            ArgumentException.ThrowIfNullOrEmpty(mac);
            ArgumentException.ThrowIfNullOrEmpty(mqttKey);

            string s = serverAddr + mac;
            EnsureAlphabet(s, nameof(serverAddr));
            EnsureAlphabet(mqttKey, nameof(mqttKey));

            // 모두 치환표 문자(ASCII)이므로 문자 값이 펌웨어의 바이트 값과 같다.
            long sumS = Sum(s);
            long sumK = Sum(mqttKey);

            StringBuilder result = new StringBuilder(Length);
            for (int i = 0; i < Length; i++)
            {
                long value = Alphabet.IndexOf(s[i % s.Length]) * sumK + Alphabet.IndexOf(mqttKey[i % mqttKey.Length]) * sumS;
                result.Append(Alphabet[(int)(value % Alphabet.Length)]);
            }
            return result.ToString();
        }

        /// <summary>
        /// 비어 있지 않고 치환표 문자로만 이뤄졌으면 true. 표 밖 문자는 펌웨어 계산과 결과가 달라질 수 있다.
        /// </summary>
        public static bool IsValidServerAddr(string? serverAddr)
            => !string.IsNullOrEmpty(serverAddr) && serverAddr.All(c => Alphabet.Contains(c));

        private static long Sum(string value)
        {
            long sum = 0;
            foreach (char c in value)
                sum += c;
            return sum;
        }

        private static void EnsureAlphabet(string value, string paramName)
        {
            if (!value.All(c => Alphabet.Contains(c)))
                throw new ArgumentException("Value contains characters outside the verify alphabet.", paramName);
        }
    }
}
