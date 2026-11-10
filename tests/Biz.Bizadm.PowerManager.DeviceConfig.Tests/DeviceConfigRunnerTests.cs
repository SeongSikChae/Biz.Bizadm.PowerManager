using Biz.Bizadm.PowerManager.DeviceConfig.Protocol;
using Biz.Bizadm.PowerManager.DeviceConfig.Tests.TestSupport;
using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests
{
    [TestClass]
    public sealed class DeviceConfigRunnerTests
    {
        private const string Ok = "{\"mac\":\"" + MockDevice.Mac + "\",\"respone\":\"OK\"}";
        private const string Fail = "{\"mac\":\"" + MockDevice.Mac + "\",\"respone\":\"FAIL\"}";
        private const string UnreachableHost = "192.0.2.1"; // TEST-NET-1: 쓰이면 연결 실패
        private static readonly TimeSpan ApConnectTimeout = TimeSpan.FromSeconds(30);

        private static readonly DeviceSettings Settings = new()
        {
            ApiServerAddr = "api.example.local",
            ApiServerPort = 443,
            ServerAddr = "mqtt.example.local",
            ServerPort = 8883,
            SslSupport = false,
            UserId = "user",
            Company = "DAWONDNS",
            Model = "B540_W",
            Lati = "",
            Long = "",
            Topic = "dwd",
        };

        public TestContext TestContext { get; set; } = null!;

        private static string? Respond(string line) => line switch
        {
            "{\"command\":\"apscan\"}" => "{\"SCAN_AP_LIST\":{\"MyRouter:-60\",\"Other:-40\"}}",
            "[DUT<-PC] MEASUREMENT" => "[DUT->PC] MEASUREMENT_W:0.0#",
            "[DUT<-PC] ON_CMD" => "[DUT->PC] ON_CMD_OK#",
            "[DUT<-PC] OFF_CMD" => "[DUT->PC] OFF_CMD_OK#",
            _ when line.Contains("connectap") || line.Contains("\"pass\"") => Ok,
            _ => null,
        };

        private static DeviceConfigOptions Options(MockDevice device, string host, params DeviceCommand[] commands) => new()
        {
            Host = host,
            Port = device.Port,
            Timeout = TimeSpan.FromSeconds(1),
            ApPrefix = "DWD-",
            ApConnectTimeout = ApConnectTimeout,
            Commands = commands,
            WifiProfiles = [new WifiProfile("MyRouter", "secret")],
            Settings = Settings,
        };

        private static DeviceConfigRunner Runner(FakeHostWifi hostWifi, FakeSelector selector, DeviceConfigOptions options, ListLogger<DeviceConfigRunner> logger) =>
            new(hostWifi, selector, options, logger);

        [TestMethod]
        public async Task Run_UsesGateway_ExecutesCommands_Restores()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new();
            FakeSelector selector = new();

            await Runner(hostWifi, selector, Options(device, UnreachableHost, DeviceCommand.Measurement, DeviceCommand.ConnectAp), new())
                .RunAsync(TestContext.CancellationToken);

            CollectionAssert.AreEqual(new[] { "DWD-LS120_000001" }, selector.OfferedDeviceAps!.Select(n => n.Ssid).ToArray());
            Assert.AreEqual((FakeHostWifi.Adapter, "DWD-LS120_000001", ApConnectTimeout), hostWifi.Connected);
            Assert.AreEqual(2, device.ConnectionCount); // 게이트웨이(127.0.0.1)로 접속, 명령마다 재연결
            Assert.AreEqual((FakeHostWifi.Adapter, (string?)"HomeRouter"), hostWifi.Restored);
            string settingsLine = device.Received.Single(r => r.Line.Contains("\"pass\"")).Line;
            Assert.AreEqual("MyRouter", JsonDocument.Parse(settingsLine).RootElement.GetProperty("ssid").GetString());
        }

        [TestMethod]
        public async Task Run_NotImplemented_FallsBackToHost_NoRestore()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new() { ThrowOnListAdapters = new NotImplementedException() };
            ListLogger<DeviceConfigRunner> logger = new();

            await Runner(hostWifi, new FakeSelector(), Options(device, device.Host, DeviceCommand.Measurement), logger)
                .RunAsync(TestContext.CancellationToken);

            Assert.AreEqual(1, device.ConnectionCount);
            Assert.IsNull(hostWifi.Restored);
            StringAssert.Contains(logger.AllText, "Warning: PC Wi-Fi is not implemented");
        }

        [TestMethod]
        public async Task Run_OtherPcWifiFailureBeforeConnect_Propagates_NoRestore()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new() { ThrowOnScan = new InvalidOperationException("scan failed") };

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => Runner(hostWifi, new FakeSelector(), Options(device, device.Host, DeviceCommand.Measurement), new()).RunAsync(TestContext.CancellationToken));

            Assert.AreEqual(0, device.ConnectionCount);
            Assert.IsNull(hostWifi.Restored);
        }

        [TestMethod]
        public async Task Run_ConnectTimeout_Propagates_Restores()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new() { ThrowOnConnect = new TimeoutException("connect timed out") };

            await Assert.ThrowsExactlyAsync<TimeoutException>(
                () => Runner(hostWifi, new FakeSelector(), Options(device, device.Host, DeviceCommand.Measurement), new()).RunAsync(TestContext.CancellationToken));

            Assert.AreEqual(0, device.ConnectionCount);
            Assert.AreEqual((FakeHostWifi.Adapter, (string?)"HomeRouter"), hostWifi.Restored);
        }

        [TestMethod]
        public async Task Run_CanceledDuringConnect_Restores()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new() { ThrowOnConnect = new OperationCanceledException() };

            await Assert.ThrowsExactlyAsync<OperationCanceledException>(
                () => Runner(hostWifi, new FakeSelector(), Options(device, device.Host, DeviceCommand.Measurement), new()).RunAsync(TestContext.CancellationToken));

            Assert.IsNotNull(hostWifi.Restored);
        }

        [TestMethod]
        public async Task Run_GatewayNull_Throws_Restores()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new() { Gateway = null };

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => Runner(hostWifi, new FakeSelector(), Options(device, device.Host, DeviceCommand.Measurement), new()).RunAsync(TestContext.CancellationToken));

            Assert.AreEqual(0, device.ConnectionCount);
            Assert.IsNotNull(hostWifi.Restored);
        }

        [TestMethod]
        public async Task Run_CommandFails_StopsRemaining_Restores()
        {
            await using MockDevice device = new(line => line.Contains("\"pass\"") ? Fail : Respond(line));
            FakeHostWifi hostWifi = new();

            await Assert.ThrowsExactlyAsync<DeviceProtocolException>(
                () => Runner(hostWifi, new FakeSelector(), Options(device, UnreachableHost, DeviceCommand.ConnectAp, DeviceCommand.Measurement), new())
                    .RunAsync(TestContext.CancellationToken));

            Assert.AreEqual(1, device.ConnectionCount);
            Assert.IsFalse(device.Received.Any(r => r.Line == "[DUT<-PC] MEASUREMENT"));
            Assert.IsNotNull(hostWifi.Restored);
        }

        [TestMethod]
        public async Task Run_CommandFailsAndRestoreFails_PropagatesOriginal_LogsRestore()
        {
            await using MockDevice device = new(line => line == "[DUT<-PC] MEASUREMENT" ? null : Respond(line));
            FakeHostWifi hostWifi = new() { ThrowOnRestore = new InvalidOperationException("restore failed") };
            ListLogger<DeviceConfigRunner> logger = new();

            await Assert.ThrowsExactlyAsync<TimeoutException>(
                () => Runner(hostWifi, new FakeSelector(), Options(device, UnreachableHost, DeviceCommand.Measurement), logger).RunAsync(TestContext.CancellationToken));

            StringAssert.Contains(logger.AllText, "Error: PC Wi-Fi restore failed");
            StringAssert.Contains(logger.AllText, "restore failed");
        }

        [TestMethod]
        public async Task Run_CommandSucceedsAndRestoreFails_PropagatesRestore()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new() { ThrowOnRestore = new InvalidOperationException("restore failed") };

            InvalidOperationException ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => Runner(hostWifi, new FakeSelector(), Options(device, UnreachableHost, DeviceCommand.Measurement), new()).RunAsync(TestContext.CancellationToken));

            Assert.AreEqual("restore failed", ex.Message);
        }

        [TestMethod]
        public async Task Run_NoRegisteredWifiInApScan_DoesNotSendSettings()
        {
            await using MockDevice device = new(line => line == "{\"command\":\"apscan\"}" ? "{\"SCAN_AP_LIST\":{\"Other:-40\"}}" : Respond(line));
            FakeHostWifi hostWifi = new();

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => Runner(hostWifi, new FakeSelector(), Options(device, UnreachableHost, DeviceCommand.ConnectAp), new()).RunAsync(TestContext.CancellationToken));

            Assert.IsFalse(device.Received.Any(r => r.Line.Contains("\"pass\"")));
            Assert.IsNotNull(hostWifi.Restored);
        }
    }
}
