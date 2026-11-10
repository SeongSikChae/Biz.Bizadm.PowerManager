using Biz.Bizadm.PowerManager.DeviceConfig.Tests.TestSupport;
using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests
{
    [TestClass]
    public sealed class DeviceSessionConnectApTests
    {
        private static readonly DeviceTimeouts Timeouts = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        private sealed class FixedSelector(int index) : IWifiProfileSelector
        {
            public IReadOnlyList<WifiCandidate>? Candidates { get; private set; }

            public Task<WifiProfile> SelectAsync(IReadOnlyList<WifiCandidate> candidates, CancellationToken cancellationToken)
            {
                Candidates = candidates;
                return Task.FromResult(candidates[index].Profile);
            }
        }

        private static DeviceSettings Settings(string? mac = null) => new()
        {
            Mac = mac,
            UserId = "user@example.com",
            Company = "DAWONDNS",
            Model = "B540_W",
            Latitude = "0",
            Longitude = "0",
            ApiServerAddr = "api.local",
            ApiServerPort = "18443",
            MqttServerAddr = "mqtt.local",
            MqttServerPort = "8883",
            SslSupport = "yes",
            Topic = "dwd",
            MqttKey = "KEY",
            WifiProfiles = [new WifiProfile("HomeAP", "home-pass"), new WifiProfile("OfficeAP", "office-pass")],
        };

        private static async Task<(TcpDeviceChannel Channel, DeviceSession Session)> HandshakeAsync(MockDevice device)
        {
            TcpDeviceChannel channel = await TcpDeviceChannel.ConnectAsync(
                device.Host, device.Port, TimeSpan.FromSeconds(5), NullLogger.Instance, CancellationToken.None);
            DeviceSession session = new(channel, Timeouts, NullLogger.Instance);
            Task<string> handshake = session.HandshakeAsync(CancellationToken.None);
            await device.ReadLineAsync();
            await device.ReadLineAsync();
            await device.SendAsync("[DUT->PC] START_OK:aabbccddeeff#");
            await handshake;
            return (channel, session);
        }

        [TestMethod]
        public async Task ConnectAp_ScanSelectSettingsConnect()
        {
            await using MockDevice device = MockDevice.Start();
            (TcpDeviceChannel channel, DeviceSession session) = await HandshakeAsync(device);
            await using TcpDeviceChannel ownedChannel = channel;
            FixedSelector selector = new(1);

            Task<DeviceResponse> connect = session.ConnectApAsync(Settings(), selector, CancellationToken.None);

            Assert.AreEqual("{\"command\":\"apscan\"}", await device.ReadLineAsync());
            await device.SendAsync("{\"SCAN_AP_LIST\":{\"HomeAP:-40\",\"Other:-30\",\"OfficeAP:-60\"}}");

            using JsonDocument settingsJson = JsonDocument.Parse(await device.ReadLineAsync());
            await device.SendAsync("{\"respone\":\"OK\"}");
            Assert.AreEqual("{\"mac\":\"aabbccddeeff\",\"command\":\"connectap\"}", await device.ReadLineAsync());
            await device.SendAsync("{\"mac\":\"aabbccddeeff\",\"respone\":\"OK\"}");

            DeviceResponse result = await connect;
            Assert.AreEqual("{\"mac\":\"aabbccddeeff\",\"respone\":\"OK\"}", result.Raw);

            CollectionAssert.AreEqual(new[] { "HomeAP", "OfficeAP" }, selector.Candidates!.Select(c => c.Profile.Ssid).ToArray());
            JsonElement root = settingsJson.RootElement;
            Assert.AreEqual("OfficeAP", root.GetProperty("ssid").GetString());
            Assert.AreEqual("office-pass", root.GetProperty("pass").GetString());
            Assert.AreEqual("aabbccddeeff", root.GetProperty("mac").GetString());
            Assert.AreEqual("KEY", root.GetProperty("mqtt_key").GetString());
            Assert.IsFalse(root.TryGetProperty("command", out _));
        }

        [TestMethod]
        public async Task ConnectAp_SettingsMacOverridesHandshake_EmptyMacUsesHandshake()
        {
            await using MockDevice device = MockDevice.Start();
            (TcpDeviceChannel channel, DeviceSession session) = await HandshakeAsync(device);
            await using TcpDeviceChannel ownedChannel = channel;

            Task<DeviceResponse> connect = session.ConnectApAsync(Settings(mac: ""), new FixedSelector(0), CancellationToken.None);
            await device.ReadLineAsync();
            await device.SendAsync("{\"SCAN_AP_LIST\":{\"HomeAP:-40\"}}");
            using JsonDocument settingsJson = JsonDocument.Parse(await device.ReadLineAsync());
            await device.SendAsync("{\"respone\":\"OK\"}");
            Assert.AreEqual("{\"mac\":\"aabbccddeeff\",\"command\":\"connectap\"}", await device.ReadLineAsync());
            await device.SendAsync("{\"respone\":\"OK\"}");
            await connect;

            Assert.AreEqual("aabbccddeeff", settingsJson.RootElement.GetProperty("mac").GetString());
        }

        [TestMethod]
        public async Task ConnectAp_SettingsNotOk_ThrowsWithoutConnectAp()
        {
            await using MockDevice device = MockDevice.Start();
            (TcpDeviceChannel channel, DeviceSession session) = await HandshakeAsync(device);
            await using TcpDeviceChannel ownedChannel = channel;

            Task<DeviceResponse> connect = session.ConnectApAsync(Settings(), new FixedSelector(0), CancellationToken.None);
            await device.ReadLineAsync();
            await device.SendAsync("{\"SCAN_AP_LIST\":{\"HomeAP:-40\"}}");
            await device.ReadLineAsync();
            await device.SendAsync("{\"mac\":\"aabbccddeeff\",\"respone\":\"FAIL\"}");

            DeviceProtocolException ex = await Assert.ThrowsExactlyAsync<DeviceProtocolException>(() => connect);
            Assert.Contains("settings", ex.Message);
            // connectap을 보내지 않았으면 PC가 연결을 닫을 때까지 받은 줄이 없다
            await ownedChannel.DisposeAsync();
            await Assert.ThrowsExactlyAsync<IOException>(device.ReadLineAsync);
        }

        [TestMethod]
        [DataRow("{\"mac\":\"aabbccddeeff\",\"respone\":\"FAIL\"}")]
        [DataRow("{\"mac\":\"aabbccddeeff\"}")]
        [DataRow("{not json}")]
        public async Task ConnectAp_ConnectApNotOk_Throws(string response)
        {
            await using MockDevice device = MockDevice.Start();
            (TcpDeviceChannel channel, DeviceSession session) = await HandshakeAsync(device);
            await using TcpDeviceChannel ownedChannel = channel;

            Task<DeviceResponse> connect = session.ConnectApAsync(Settings(), new FixedSelector(0), CancellationToken.None);
            await device.ReadLineAsync();
            await device.SendAsync("{\"SCAN_AP_LIST\":{\"HomeAP:-40\"}}");
            await device.ReadLineAsync();
            await device.SendAsync("{\"respone\":\"OK\"}");
            await device.ReadLineAsync();
            await device.SendAsync(response);

            DeviceProtocolException ex = await Assert.ThrowsExactlyAsync<DeviceProtocolException>(() => connect);
            Assert.Contains("connectap", ex.Message);
        }

        [TestMethod]
        public async Task ConnectAp_NoRegisteredWifi_ThrowsWithScannedList()
        {
            await using MockDevice device = MockDevice.Start();
            (TcpDeviceChannel channel, DeviceSession session) = await HandshakeAsync(device);
            await using TcpDeviceChannel ownedChannel = channel;

            Task<DeviceResponse> connect = session.ConnectApAsync(Settings(), new FixedSelector(0), CancellationToken.None);
            await device.ReadLineAsync();
            await device.SendAsync("{\"SCAN_AP_LIST\":{\"Other:-30\"}}");

            DeviceProtocolException ex = await Assert.ThrowsExactlyAsync<DeviceProtocolException>(() => connect);
            Assert.Contains("Other", ex.Message);
        }

        [TestMethod]
        public async Task ApScan_UnexpectedFormat_ReturnsRaw()
        {
            await using MockDevice device = MockDevice.Start();
            (TcpDeviceChannel channel, DeviceSession session) = await HandshakeAsync(device);
            await using TcpDeviceChannel ownedChannel = channel;

            Task<DeviceResponse> scan = session.ExecuteAsync(DeviceCommand.ApScan, CancellationToken.None);
            await device.ReadLineAsync();
            await device.SendAsync("{\"AP\":[]}");

            Assert.AreEqual("{\"AP\":[]}", (await scan).Raw);
        }
    }
}
