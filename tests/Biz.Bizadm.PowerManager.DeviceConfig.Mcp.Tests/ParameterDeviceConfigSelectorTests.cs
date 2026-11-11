using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests
{
    [TestClass]
    public sealed class ParameterDeviceConfigSelectorTests
    {
        private static readonly WifiAdapter[] Adapters = [new("{AAAA-1111}", "Wi-Fi"), new("{BBBB-2222}", "Wi-Fi 2")];
        private static readonly WifiNetwork[] DeviceAps = [new("DWD-LS120_000001", -40), new("DWD-LS120_000002", -60)];
        private static readonly WifiProfileCandidate[] Candidates =
            [new(new WifiProfile("MyRouter", "a"), -50), new(new WifiProfile("Other", "b"), -70)];

        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public async Task SelectAdapter_MatchesIdIgnoringCase()
        {
            WifiAdapter adapter = await new ParameterDeviceConfigSelector("{bbbb-2222}", "", null)
                .SelectAdapterAsync(Adapters, TestContext.CancellationToken);

            Assert.AreEqual(Adapters[1], adapter);
        }

        [TestMethod]
        public async Task SelectAdapter_NotFound_Throws()
        {
            ArgumentException ex = await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => new ParameterDeviceConfigSelector("{CCCC-3333}", "", null).SelectAdapterAsync(Adapters, TestContext.CancellationToken));

            Assert.AreEqual("Adapter not found: {CCCC-3333}", ex.Message);
        }

        [TestMethod]
        public async Task SelectDeviceAp_Matches()
        {
            WifiNetwork ap = await new ParameterDeviceConfigSelector("", "DWD-LS120_000002", null)
                .SelectDeviceApAsync(DeviceAps, TestContext.CancellationToken);

            Assert.AreEqual(DeviceAps[1], ap);
        }

        [TestMethod]
        public async Task SelectDeviceAp_CaseMismatch_Throws()
        {
            ArgumentException ex = await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => new ParameterDeviceConfigSelector("", "dwd-ls120_000001", null).SelectDeviceApAsync(DeviceAps, TestContext.CancellationToken));

            Assert.AreEqual("Device AP not found in scan: dwd-ls120_000001", ex.Message);
        }

        [TestMethod]
        public async Task SelectWifiProfile_Matches()
        {
            WifiProfile profile = await new ParameterDeviceConfigSelector("", "", "Other")
                .SelectWifiProfileAsync(Candidates, TestContext.CancellationToken);

            Assert.AreEqual(Candidates[1].Profile, profile);
        }

        [TestMethod]
        public async Task SelectWifiProfile_NotInCandidates_Throws()
        {
            ArgumentException ex = await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => new ParameterDeviceConfigSelector("", "", "myrouter").SelectWifiProfileAsync(Candidates, TestContext.CancellationToken));

            Assert.AreEqual("Wi-Fi not in device apscan result: myrouter", ex.Message);
        }

        [TestMethod]
        public async Task SelectWifiProfile_WifiSsidNull_Throws()
        {
            ArgumentException ex = await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => new ParameterDeviceConfigSelector("", "", null).SelectWifiProfileAsync(Candidates, TestContext.CancellationToken));

            Assert.AreEqual("wifiSsid is required for ConnectAp", ex.Message);
        }
    }
}
