using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Protocol;
using Biz.Bizadm.PowerManager.DeviceConfig.Tests.TestSupport;
using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.Transport
{
    [TestClass]
    public sealed class DeviceClientTests
    {
        private const string Ok = "{\"mac\":\"" + MockDevice.Mac + "\",\"respone\":\"OK\"}";
        private const string ScanList = "{\"SCAN_AP_LIST\":{\"MyRouter:-50\",\"Other:-70\"}}";
        private const string Pass = "p@ss+&word";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

        private static readonly DeviceSettings Settings = new()
        {
            ApiServerAddr = "api.example.local",
            ApiServerPort = 443,
            ServerAddr = "mqtt.example.local",
            ServerPort = 8883,
            SslSupport = true,
            UserId = "user",
            Company = "DAWONDNS",
            Model = "B540_W",
            Lati = "",
            Long = "",
            Topic = "dwd",
        };

        public TestContext TestContext { get; set; } = null!;

        private static string? Default(string line) => line switch
        {
            "{\"command\":\"apscan\"}" => ScanList,
            "{\"command\":\"factoryreset\"}" => Ok,
            "{\"command\":\"reboot\"}" => "{\"mac\":\"" + MockDevice.Mac + "\",\"respone\":\"reboot\"}",
            "[DUT<-PC] SCAN_AP" => ScanList,
            "[DUT<-PC] ON_CMD" => "[DUT->PC] ON_CMD_OK#",
            "[DUT<-PC] OFF_CMD" => "[DUT->PC] OFF_CMD_OK#",
            "[DUT<-PC] MEASUREMENT" => "[DUT->PC] MEASUREMENT_W:153.95#",
            "[DUT<-PC] OVERLOAD" => "[DUT->PC] OVERLOAD_OK#",
            "[DUT<-PC] FACTORY_RESET" => "[DUT->PC] FACTORY_RESET_OK#",
            _ when line.Contains("\"command\":\"connectap\"") => Ok,
            _ when line.Contains("\"pass\"") => Ok,
            _ => null,
        };

        private static DeviceClient Client(MockDevice device, ListLogger logger) => new(device.Host, device.Port, Timeout, logger);

        private static Task<(string, string)> SelectMyRouter(IReadOnlyList<WifiNetwork> scanned, CancellationToken _) =>
            Task.FromResult(("MyRouter", Pass));

        [TestMethod]
        [DataRow(DeviceCommand.ApScan, "{\"command\":\"apscan\"}")]
        [DataRow(DeviceCommand.FactoryReset, "{\"command\":\"factoryreset\"}")]
        [DataRow(DeviceCommand.Reboot, "{\"command\":\"reboot\"}")]
        [DataRow(DeviceCommand.ScanAp, "[DUT<-PC] SCAN_AP")]
        [DataRow(DeviceCommand.OnCmd, "[DUT<-PC] ON_CMD")]
        [DataRow(DeviceCommand.OffCmd, "[DUT<-PC] OFF_CMD")]
        [DataRow(DeviceCommand.Measurement, "[DUT<-PC] MEASUREMENT")]
        [DataRow(DeviceCommand.OverLoad, "[DUT<-PC] OVERLOAD")]
        [DataRow(DeviceCommand.TestFactoryReset, "[DUT<-PC] FACTORY_RESET")]
        public async Task ExecuteAsync_SendsHandshakeThenCommand(DeviceCommand command, string expectedLine)
        {
            await using MockDevice device = new(Default);

            await Client(device, new ListLogger()).ExecuteAsync(command, TestContext.CancellationToken);

            CollectionAssert.AreEqual(
                new[] { "[DUT<-PC] START", "hello tcp SUCCESS_CONNECT", expectedLine },
                device.Received.Select(r => r.Line).ToArray());
        }

        [TestMethod]
        public async Task ExecuteAsync_ReconnectsPerCommand()
        {
            await using MockDevice device = new(Default);
            DeviceClient client = Client(device, new ListLogger());

            await client.ExecuteAsync(DeviceCommand.Measurement, TestContext.CancellationToken);
            await client.ExecuteAsync(DeviceCommand.OnCmd, TestContext.CancellationToken);
            await client.ExecuteAsync(DeviceCommand.OffCmd, TestContext.CancellationToken);

            Assert.AreEqual(3, device.ConnectionCount);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 },
                device.Received.Where(r => r.Line == "[DUT<-PC] START").Select(r => r.Connection).ToArray());
        }

        [TestMethod]
        public async Task ExecuteAsync_ConnectAp_Throws()
        {
            await using MockDevice device = new(Default);
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
                () => Client(device, new ListLogger()).ExecuteAsync(DeviceCommand.ConnectAp, TestContext.CancellationToken));
        }

        [TestMethod]
        public async Task ConnectApAsync_OneConnectionInOrder()
        {
            await using MockDevice device = new(Default);
            ListLogger logger = new();
            IReadOnlyList<WifiNetwork>? offered = null;

            await Client(device, logger).ConnectApAsync((scanned, ct) =>
            {
                offered = scanned;
                return SelectMyRouter(scanned, ct);
            }, Settings, TestContext.CancellationToken);

            Assert.AreEqual(1, device.ConnectionCount);
            CollectionAssert.AreEqual(new[] { new WifiNetwork("MyRouter", -50), new WifiNetwork("Other", -70) }, offered!.ToArray());
            string[] lines = device.Received.Select(r => r.Line).ToArray();
            Assert.AreEqual(5, lines.Length);
            Assert.AreEqual("{\"command\":\"apscan\"}", lines[2]);
            using JsonDocument settings = JsonDocument.Parse(lines[3]);
            Assert.AreEqual(MockDevice.Mac, settings.RootElement.GetProperty("mac").GetString());
            Assert.AreEqual("MyRouter", settings.RootElement.GetProperty("ssid").GetString());
            Assert.AreEqual(Pass, settings.RootElement.GetProperty("pass").GetString());
            StringAssert.Matches(settings.RootElement.GetProperty("mqtt_key").GetString(), new System.Text.RegularExpressions.Regex("^[A-Za-z0-9/]{16}$"));
            Assert.AreEqual("{\"mac\":\"" + MockDevice.Mac + "\",\"command\":\"connectap\"}", lines[4]);

            string mqttKey = settings.RootElement.GetProperty("mqtt_key").GetString()!;
            Assert.IsFalse(logger.AllText.Contains(Pass), "pass in log");
            Assert.IsFalse(logger.AllText.Contains(mqttKey), "mqtt_key in log");
        }

        [TestMethod]
        public async Task ConnectApAsync_SettingsTimeout_DoesNotLeakSecrets()
        {
            await using MockDevice device = new(line => line.Contains("\"pass\"") ? null : Default(line));
            ListLogger logger = new();

            TimeoutException ex = await Assert.ThrowsExactlyAsync<TimeoutException>(
                () => Client(device, logger).ConnectApAsync(SelectMyRouter, Settings, TestContext.CancellationToken));

            string mqttKey = JsonDocument.Parse(device.Received.Single(r => r.Line.Contains("\"pass\"")).Line)
                .RootElement.GetProperty("mqtt_key").GetString()!;
            foreach (string text in new[] { ex.ToString(), logger.AllText })
            {
                Assert.IsFalse(text.Contains(Pass), "pass leaked");
                Assert.IsFalse(text.Contains(mqttKey), "mqtt_key leaked");
            }
        }

        [TestMethod]
        public async Task ConnectApAsync_SettingsClosed_DoesNotLeakSecrets()
        {
            await using MockDevice device = new(line => line.Contains("\"pass\"") ? MockDevice.Close : Default(line));
            ListLogger logger = new();

            IOException ex = await Assert.ThrowsExactlyAsync<IOException>(
                () => Client(device, logger).ConnectApAsync(SelectMyRouter, Settings, TestContext.CancellationToken));

            Assert.IsFalse(ex.ToString().Contains(Pass));
            Assert.IsFalse(logger.AllText.Contains(Pass));
        }

        [TestMethod]
        public async Task ConnectApAsync_NotOk_Throws()
        {
            await using MockDevice device = new(line => line.Contains("connectap") ? Ok.Replace("OK", "FAIL") : Default(line));

            await Assert.ThrowsExactlyAsync<DeviceProtocolException>(
                () => Client(device, new ListLogger()).ConnectApAsync(SelectMyRouter, Settings, TestContext.CancellationToken));
        }

        [TestMethod]
        public async Task ExecuteAsync_FactoryResetNotOk_Throws()
        {
            await using MockDevice device = new(line => line.Contains("factoryreset") ? Ok.Replace("OK", "FAIL") : Default(line));

            await Assert.ThrowsExactlyAsync<DeviceProtocolException>(
                () => Client(device, new ListLogger()).ExecuteAsync(DeviceCommand.FactoryReset, TestContext.CancellationToken));
        }

        [TestMethod]
        public async Task ExecuteAsync_NoResponse_TimesOut()
        {
            await using MockDevice device = new(line => line == "[DUT<-PC] MEASUREMENT" ? null : Default(line));

            await Assert.ThrowsExactlyAsync<TimeoutException>(
                () => Client(device, new ListLogger()).ExecuteAsync(DeviceCommand.Measurement, TestContext.CancellationToken));
        }

        [TestMethod]
        public async Task ExecuteAsync_ClosedBeforeResponse_Throws()
        {
            await using MockDevice device = new(line => line == "[DUT<-PC] MEASUREMENT" ? MockDevice.Close : Default(line));

            await Assert.ThrowsExactlyAsync<IOException>(
                () => Client(device, new ListLogger()).ExecuteAsync(DeviceCommand.Measurement, TestContext.CancellationToken));
        }

        [TestMethod]
        [DataRow(DeviceCommand.OnCmd, "[DUT->PC] OFF_CMD_OK#")]
        [DataRow(DeviceCommand.OffCmd, "[DUT->PC] ON_CMD_OK#")]
        [DataRow(DeviceCommand.Measurement, "[DUT->PC] MEASUREMENT_W:abc#")]
        [DataRow(DeviceCommand.ScanAp, "[DUT->PC] SCAN_AP_OK#")]
        [DataRow(DeviceCommand.ApScan, Ok)]
        public async Task ExecuteAsync_UnexpectedResponse_Throws(DeviceCommand command, string response)
        {
            await using MockDevice device = new(_ => response);

            await Assert.ThrowsExactlyAsync<DeviceProtocolException>(
                () => Client(device, new ListLogger()).ExecuteAsync(command, TestContext.CancellationToken));
        }
    }
}
