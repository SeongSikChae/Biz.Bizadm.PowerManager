using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Microsoft.Extensions.Logging.Abstractions;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.HostWifi
{
    [TestClass]
    public sealed class MacHostWifiTests
    {
        private const string HardwarePorts =
            "Hardware Port: Ethernet\nDevice: en1\nEthernet Address: 00:00:00:00:00:01\n\n" +
            "Hardware Port: Wi-Fi\nDevice: en0\nEthernet Address: 00:00:00:00:00:02\n";

        private const string AirPortJson = """
            {
              "SPAirPortDataType": [
                {
                  "spairport_airport_interfaces": [
                    {
                      "_name": "en0",
                      "spairport_current_network_information": { "_name": "HomeAP", "spairport_signal_noise": "-50 dBm / -92 dBm" },
                      "spairport_airport_other_local_wireless_networks": [
                        { "_name": "DWD-LS120_a1b2c3", "spairport_signal_noise": "-41 dBm / -90 dBm" },
                        { "_name": "NoSignal" }
                      ]
                    },
                    { "_name": "awdl0" }
                  ]
                }
              ]
            }
            """;

        private static FakeProcessRunner Runner() => new FakeProcessRunner()
            .On("-listallhardwareports", HardwarePorts)
            .On("SPAirPortDataType", AirPortJson);

        [TestMethod]
        public void FindWifiDevice_FromHardwarePorts()
        {
            Assert.AreEqual("en0", SystemProfilerParser.FindWifiDevice(HardwarePorts));
            Assert.IsNull(SystemProfilerParser.FindWifiDevice("Hardware Port: Ethernet\nDevice: en1\n"));
        }

        [TestMethod]
        public void ParseAirPort_CurrentAndOthers()
        {
            SystemProfilerParser.AirPortInterface en0 = SystemProfilerParser.ParseAirPort(AirPortJson).First(i => i.Name == "en0");

            Assert.AreEqual(new HostWifiNetwork("HomeAP", -50), en0.Current);
            CollectionAssert.AreEqual(
                new[] { new HostWifiNetwork("DWD-LS120_a1b2c3", -41), new HostWifiNetwork("NoSignal", int.MinValue) },
                en0.Others.ToArray());
        }

        [TestMethod]
        public async Task Scan_IncludesCurrentAndOthers()
        {
            IReadOnlyList<HostWifiNetwork> result = await new MacHostWifi(Runner(), NullLogger.Instance).ScanAsync(CancellationToken.None);

            CollectionAssert.AreEqual(new[] { "HomeAP", "DWD-LS120_a1b2c3", "NoSignal" }, result.Select(n => n.Ssid).ToArray());
        }

        [TestMethod]
        public async Task GetCurrent_UsesSsidAsProfile()
        {
            HostWifiConnection? current = await new MacHostWifi(Runner(), NullLogger.Instance).GetCurrentAsync(CancellationToken.None);

            Assert.AreEqual(new HostWifiConnection("en0", "HomeAP", "HomeAP"), current);
        }

        [TestMethod]
        public async Task ConnectRestoreRemove_Arguments()
        {
            FakeProcessRunner runner = Runner();
            MacHostWifi wifi = new(runner, NullLogger.Instance);

            HostWifiConnection temporary = await wifi.ConnectOpenAsync("DWD-LS120_a1b2c3", CancellationToken.None);
            await wifi.RemoveTemporaryAsync(temporary, CancellationToken.None);
            await wifi.RestoreAsync(new HostWifiConnection("en0", "HomeAP", "HomeAP"), CancellationToken.None);

            CollectionAssert.AreEqual(
                new[]
                {
                    "networksetup -listallhardwareports",
                    "networksetup -setairportnetwork|en0|DWD-LS120_a1b2c3",
                    "networksetup -removepreferredwirelessnetwork|en0|DWD-LS120_a1b2c3",
                    "networksetup -setairportnetwork|en0|HomeAP",
                },
                runner.Calls);
        }

        [TestMethod]
        public async Task Connect_ErrorMessageWithExitZero_Throws()
        {
            FakeProcessRunner runner = new FakeProcessRunner()
                .On("-listallhardwareports", HardwarePorts)
                .On("-setairportnetwork", "Could not find network DWD-x.");

            await Assert.ThrowsExactlyAsync<HostWifiException>(
                () => new MacHostWifi(runner, NullLogger.Instance).ConnectOpenAsync("DWD-x", CancellationToken.None));
        }
    }
}
