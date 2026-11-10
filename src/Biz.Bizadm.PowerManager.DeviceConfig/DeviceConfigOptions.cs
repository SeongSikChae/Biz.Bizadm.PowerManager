using Biz.Bizadm.PowerManager.DeviceConfig.Protocol;
using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;

namespace Biz.Bizadm.PowerManager.DeviceConfig
{
    /// <summary><see cref="DeviceConfigRunner"/> 실행 값. App이 설정 파일에서 만든다.</summary>
    public sealed record DeviceConfigOptions
    {
        /// <summary>PC Wi-Fi 미구현 OS에서 접속할 장치 주소.</summary>
        public required string Host { get; init; }
        public required int Port { get; init; }
        /// <summary>명령마다 응답 대기.</summary>
        public required TimeSpan Timeout { get; init; }
        public required string ApPrefix { get; init; }
        public required TimeSpan ApConnectTimeout { get; init; }
        public required IReadOnlyList<DeviceCommand> Commands { get; init; }
        /// <summary>복호화된 Pass. ConnectAp가 있을 때만 쓴다.</summary>
        public IReadOnlyList<WifiProfile> WifiProfiles { get; init; } = [];
        /// <summary>ConnectAp가 있을 때만 쓴다.</summary>
        public DeviceSettings? Settings { get; init; }
    }
}
