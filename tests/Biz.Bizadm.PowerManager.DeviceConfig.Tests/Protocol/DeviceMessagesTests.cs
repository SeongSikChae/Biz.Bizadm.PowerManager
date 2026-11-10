using Biz.Bizadm.PowerManager.DeviceConfig.Protocol;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.Protocol
{
    [TestClass]
    public sealed class DeviceMessagesTests
    {
        [TestMethod]
        public void Messages()
        {
            Assert.AreEqual("[DUT<-PC] START\n", DeviceMessages.Start);
            Assert.AreEqual("hello tcp SUCCESS_CONNECT\n", DeviceMessages.Hello);
            Assert.AreEqual("[DUT<-PC] SCAN_AP\n", DeviceMessages.Test("SCAN_AP"));
            Assert.AreEqual("{\"command\":\"apscan\"}\n", DeviceMessages.Command("apscan"));
            Assert.AreEqual("{\"mac\":\"a1b2c3d4e5f6\",\"command\":\"connectap\"}\n", DeviceMessages.ConnectAp("a1b2c3d4e5f6"));
        }
    }
}
