using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.Windows;
using Microsoft.Extensions.Logging;

namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi
{
    public static class HostWifiFactory
    {
        /// <summary>
        /// 실행 OS의 구현. 지원하지 않는 OS면 null.
        /// </summary>
        public static IHostWifi? Create(ILoggerFactory loggerFactory)
        {
            if (OperatingSystem.IsWindows())
                return new WindowsHostWifi(loggerFactory.CreateLogger<WindowsHostWifi>());
            if (OperatingSystem.IsLinux())
                return new LinuxHostWifi(new ProcessRunner(), loggerFactory.CreateLogger<LinuxHostWifi>());
            if (OperatingSystem.IsMacOS())
                return new MacHostWifi(new ProcessRunner(), loggerFactory.CreateLogger<MacHostWifi>());
            return null;
        }
    }
}
