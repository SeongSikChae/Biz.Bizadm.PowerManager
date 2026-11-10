using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Protocol;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.Protocol
{
    [TestClass]
    public sealed class DeviceResponseParserTests
    {
        [TestMethod]
        public void ParseStartOk_ReturnsMac()
        {
            Assert.AreEqual("a1b2c3d4e5f6", DeviceResponseParser.ParseStartOk("[DUT->PC] START_OK:a1b2c3d4e5f6#"));
        }

        [TestMethod]
        [DataRow("START_OK:a1b2c3d4e5f6#")]
        [DataRow("[DUT->PC] START_OK:a1b2c3#")]
        [DataRow("[DUT->PC] START_OK:a1b2c3d4e5f6")]
        public void ParseStartOk_Invalid_Throws(string frame)
        {
            Assert.ThrowsExactly<DeviceProtocolException>(() => DeviceResponseParser.ParseStartOk(frame));
        }

        [TestMethod]
        public void EnsureOk_Ok_ReturnsFrame()
        {
            const string frame = "{\"mac\":\"a1b2c3d4e5f6\",\"respone\":\"OK\"}";
            Assert.AreEqual(frame, DeviceResponseParser.EnsureOk(frame));
        }

        [TestMethod]
        [DataRow("{\"mac\":\"a1b2c3d4e5f6\",\"respone\":\"FAIL\"}")]
        [DataRow("{\"mac\":\"a1b2c3d4e5f6\"}")]
        [DataRow("{\"respone\":1}")]
        [DataRow("[\"OK\"]")]
        [DataRow("[DUT->PC] ON_CMD_OK#")]
        public void EnsureOk_NotOk_Throws(string frame)
        {
            Assert.ThrowsExactly<DeviceProtocolException>(() => DeviceResponseParser.EnsureOk(frame));
        }

        [TestMethod]
        public void ParseScanApList_RealDeviceShape()
        {
            IReadOnlyList<WifiNetwork> networks = DeviceResponseParser.ParseScanApList(
                "{\"SCAN_AP_LIST\":{\"RouterA:-44\",\"Router:B:-70\",\"Far:-93\"}}");

            CollectionAssert.AreEqual(
                new[] { new WifiNetwork("RouterA", -44), new WifiNetwork("Router:B", -70), new WifiNetwork("Far", -93) },
                networks.ToArray());
        }

        [TestMethod]
        public void ParseScanApList_Empty()
        {
            Assert.AreEqual(0, DeviceResponseParser.ParseScanApList("{\"SCAN_AP_LIST\":{}}").Count);
        }

        [TestMethod]
        [DataRow("{\"mac\":\"a1b2c3d4e5f6\",\"respone\":\"OK\"}")]
        [DataRow("{\"SCAN_AP_LIST\":[]}")]
        [DataRow("{\"SCAN_AP_LIST\":{\"A:-1\"}")]
        public void ParseScanApList_Invalid_Throws(string frame)
        {
            Assert.ThrowsExactly<DeviceProtocolException>(() => DeviceResponseParser.ParseScanApList(frame));
        }
    }
}
