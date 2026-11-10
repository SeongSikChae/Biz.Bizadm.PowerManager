using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Tests.TestSupport;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.HostWifi
{
    [TestClass]
    public sealed class HostWifiLoaderTests
    {
        // 테스트 프로젝트는 Windows에서만 HostWifi.Runtime.win을 참조한다.
        [TestMethod]
        [OSCondition(OperatingSystems.Windows)]
        public void CreateForCurrentOs_LoadsRuntimeImplementation()
        {
            IHostWifi hostWifi = HostWifiLoader.CreateForCurrentOs(new ListLogger());

            Assert.IsNotNull(hostWifi);
            Assert.AreEqual("Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.Runtime", hostWifi.GetType().Assembly.GetName().Name);
        }
    }
}
