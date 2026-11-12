using System.Diagnostics.CodeAnalysis;

namespace Biz.Bizadm.PowerManager.Core.Mqtt
{
    /// <summary>
    /// 장치 → 서버: <c>{prefix}.v1/{deviceKey}/iot-server/{suffix}</c> (suffix: json, timesync 등).
    /// LWT: 같은 접두 + <c>/lwt</c>(추정. 근거 문서는 <c>/lwt</c> 접미만 관찰했고 결합 형태는 미확인이라 빈 토큰은 무시).
    /// 서버 → 장치: <c>{prefix}.v1/iot-server/{deviceKey}</c>.
    /// </summary>
    public sealed record DeviceTopic(string Prefix, string DeviceKey, DeviceTopicKind Kind)
    {
        private const string VersionSuffix = ".v1";
        private const string ServerToken = "iot-server";

        public static bool TryParse(string topic, [NotNullWhen(true)] out DeviceTopic? result)
        {
            string[] parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
            result = null;
            // 접두 뒤 접미 토큰이 최소 1개 있어야 하고(3토큰 `x.v1/key/iot-server`는 false), 접두가 빈 `.v1/...`는 거부한다.
            if (parts.Length < 4
                || parts[0].Length <= VersionSuffix.Length
                || !parts[0].EndsWith(VersionSuffix, StringComparison.Ordinal)
                || parts[2] != ServerToken)
                return false;

            DeviceTopicKind kind = parts[^1] == "lwt" ? DeviceTopicKind.Lwt : DeviceTopicKind.Uplink;
            result = new DeviceTopic(parts[0][..^VersionSuffix.Length], parts[1], kind);
            return true;
        }

        public static string ToDevice(string prefix, string deviceKey) => $"{prefix}{VersionSuffix}/{ServerToken}/{deviceKey}";
    }
}
