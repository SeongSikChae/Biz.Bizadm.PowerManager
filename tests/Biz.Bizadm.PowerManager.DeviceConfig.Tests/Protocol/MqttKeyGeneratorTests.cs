using Biz.Bizadm.PowerManager.DeviceConfig.Protocol;
using System.Text.RegularExpressions;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.Protocol
{
    [TestClass]
    public sealed class MqttKeyGeneratorTests
    {
        [TestMethod]
        public void Create_16CharsFromAlphabet()
        {
            for (int i = 0; i < 100; i++)
                Assert.IsTrue(Regex.IsMatch(MqttKeyGenerator.Create(), "^[A-Za-z0-9/]{16}$"));
        }

        [TestMethod]
        public void Create_Differs()
        {
            Assert.AreNotEqual(MqttKeyGenerator.Create(), MqttKeyGenerator.Create());
        }
    }
}
