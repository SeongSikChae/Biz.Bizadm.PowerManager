using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi
{
    /// <summary>
    /// 실행 프로그램이 참조한 HostWifi.Runtime.&lt;os&gt; 어셈블리(이름은 OS와 무관하게 같음)에서
    /// 팩터리를 찾아 <see cref="IHostWifi"/>를 만든다(Biz.Bizadm.KMS.Credentials와 같은 방식).
    /// </summary>
    public static class HostWifiLoader
    {
        private const string RuntimeAssemblyName = "Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.Runtime";
        private const string FactoryTypeName = "Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.RuntimeHostWifiFactory";

        /// <exception cref="InvalidOperationException">런타임 어셈블리나 팩터리를 찾지 못한 경우.</exception>
        public static IHostWifi CreateForCurrentOs(ILogger logger)
        {
            Type type = Type.GetType($"{FactoryTypeName}, {RuntimeAssemblyName}", throwOnError: false)
                ?? throw new InvalidOperationException($"Runtime implementation '{RuntimeAssemblyName}' is not available.");
            MethodInfo create = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, [typeof(ILogger)])
                ?? throw new InvalidOperationException($"Create method not found on '{FactoryTypeName}'.");
            return (IHostWifi)create.Invoke(null, [logger])!;
        }
    }
}
