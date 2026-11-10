using Microsoft.Extensions.Logging;

namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi
{
    /// <summary><see cref="HostWifiLoader"/>가 리플렉션으로 찾는 팩터리.</summary>
    public static class RuntimeHostWifiFactory
    {
        public static IHostWifi Create(ILogger logger) => new WindowsHostWifi(logger);
    }
}
