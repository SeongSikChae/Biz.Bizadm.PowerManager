namespace Biz.Bizadm.PowerManager.DeviceConfig.Protocol
{
    /// <summary>
    /// ConnectAp 흐름(참고 구현 PowerManagerConfig의 Configrator 버전).
    /// V1: M130, B540 V1.0.26 / V2: B540 v1.0.28 / V3: B540 v1.0.30·v1.0.32 / V4: B550 v1.0.10.
    /// </summary>
    public enum DeviceProtocol
    {
        V1,
        V2,
        V3,
        V4,
    }

    public static class DeviceProtocols
    {
        /// <summary>이름과 대소문자까지 정확히 같을 때만 성공한다(숫자 값·공백·소문자는 실패).</summary>
        public static bool TryParse(string? value, out DeviceProtocol protocol)
        {
            foreach (DeviceProtocol candidate in Enum.GetValues<DeviceProtocol>())
            {
                if (string.Equals(value, candidate.ToString(), StringComparison.Ordinal))
                {
                    protocol = candidate;
                    return true;
                }
            }
            protocol = default;
            return false;
        }
    }
}
