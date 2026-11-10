using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Microsoft.Extensions.Logging.Abstractions;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.HostWifi
{
    [TestClass]
    public sealed class LinuxHostWifiTests
    {
        private const string DeviceStatus = "wlan0:wifi:connected:Home Conn\neth0:ethernet:unavailable:\nlo:loopback:connected (externally):lo\n";

        [TestMethod]
        public void SplitTerse_UnescapesColonAndBackslash()
        {
            CollectionAssert.AreEqual(new[] { "a:b", "c\\d", "", "*" }, NmcliParser.SplitTerse("a\\:b:c\\\\d::*").ToArray());
        }

        [TestMethod]
        public void ParseWifiList_SkipsHiddenAndMarksInUse()
        {
            IReadOnlyList<NmcliParser.WifiEntry> entries = NmcliParser.ParseWifiList("DWD-LS120_a1b2c3:82: \n:40: \nHome\\:AP:65:*\r\n");

            CollectionAssert.AreEqual(
                new[] { new NmcliParser.WifiEntry("DWD-LS120_a1b2c3", 82, false), new NmcliParser.WifiEntry("Home:AP", 65, true) },
                entries.ToArray());
        }

        [TestMethod]
        public async Task Scan_RunsRescanAndMapsSignal()
        {
            FakeProcessRunner runner = new FakeProcessRunner().On("dev|wifi|list|--rescan|yes", "DWD-LS120_a1b2c3:82:\nHome:65:*\n");
            LinuxHostWifi wifi = new(runner, NullLogger.Instance);

            IReadOnlyList<HostWifiNetwork> result = await wifi.ScanAsync(CancellationToken.None);

            CollectionAssert.AreEqual(new[] { new HostWifiNetwork("DWD-LS120_a1b2c3", 82), new HostWifiNetwork("Home", 65) }, result.ToArray());
            Assert.AreEqual("nmcli -t|-f|SSID,SIGNAL,IN-USE|dev|wifi|list|--rescan|yes", runner.Calls.Single());
        }

        [TestMethod]
        public async Task GetCurrent_UsesConnectedWifiDeviceAndInUseSsid()
        {
            FakeProcessRunner runner = new FakeProcessRunner()
                .On("device|status", DeviceStatus)
                .On("ifname|wlan0|--rescan|no", "Other:30:\nHomeAP:65:*\n");
            LinuxHostWifi wifi = new(runner, NullLogger.Instance);

            HostWifiConnection? current = await wifi.GetCurrentAsync(CancellationToken.None);

            Assert.AreEqual(new HostWifiConnection("wlan0", "HomeAP", "Home Conn"), current);
        }

        [TestMethod]
        public async Task GetCurrent_NoConnectedWifi_Null()
        {
            FakeProcessRunner runner = new FakeProcessRunner().On("device|status", "wlan0:wifi:disconnected:\n");
            Assert.IsNull(await new LinuxHostWifi(runner, NullLogger.Instance).GetCurrentAsync(CancellationToken.None));
        }

        [TestMethod]
        public async Task ConnectOpen_DeletesLeftoverThenConnectsWithTemporaryName()
        {
            FakeProcessRunner runner = new FakeProcessRunner()
                .On("device|status", DeviceStatus)
                .On("connection|delete", "", exitCode: 10, stderr: "no such connection");
            LinuxHostWifi wifi = new(runner, NullLogger.Instance);

            HostWifiConnection connection = await wifi.ConnectOpenAsync("DWD-LS120_a1b2c3", CancellationToken.None);

            Assert.AreEqual(new HostWifiConnection("wlan0", "DWD-LS120_a1b2c3", LinuxHostWifi.TemporaryConnectionName), connection);
            CollectionAssert.Contains(runner.Calls, "nmcli connection|delete|id|b540-config-temp");
            Assert.AreEqual("nmcli dev|wifi|connect|DWD-LS120_a1b2c3|ifname|wlan0|name|b540-config-temp", runner.Calls.Last());
        }

        [TestMethod]
        public async Task ConnectOpen_Failure_Throws()
        {
            FakeProcessRunner runner = new FakeProcessRunner()
                .On("device|status", DeviceStatus)
                .On("dev|wifi|connect", "", exitCode: 4, stderr: "Error: Connection activation failed");

            HostWifiException ex = await Assert.ThrowsExactlyAsync<HostWifiException>(
                () => new LinuxHostWifi(runner, NullLogger.Instance).ConnectOpenAsync("DWD-x", CancellationToken.None));
            Assert.Contains("activation failed", ex.Message);
        }

        [TestMethod]
        public async Task RestoreAndRemove_UseConnectionIds()
        {
            FakeProcessRunner runner = new();
            LinuxHostWifi wifi = new(runner, NullLogger.Instance);

            await wifi.RemoveTemporaryAsync(new HostWifiConnection("wlan0", "DWD-x", "b540-config-temp"), CancellationToken.None);
            await wifi.RestoreAsync(new HostWifiConnection("wlan0", "HomeAP", "Home Conn"), CancellationToken.None);

            CollectionAssert.AreEqual(
                new[] { "nmcli connection|delete|id|b540-config-temp", "nmcli connection|up|id|Home Conn" },
                runner.Calls);
        }
    }
}
