using Biz.Bizadm.PowerManager.DeviceConfig.Tests.TestSupport;
using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests
{
    [TestClass]
    public sealed class DeviceSessionTests
    {
        private static readonly DeviceTimeouts Timeouts = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        private static async Task<(TcpDeviceChannel Channel, DeviceSession Session)> ConnectAsync(MockDevice device)
        {
            TcpDeviceChannel channel = await TcpDeviceChannel.ConnectAsync(
                device.Host, device.Port, TimeSpan.FromSeconds(5), NullLogger.Instance, CancellationToken.None);
            return (channel, new DeviceSession(channel, Timeouts, NullLogger.Instance));
        }

        [TestMethod]
        public async Task Handshake_SendsStartAndHello_ReturnsMac()
        {
            await using MockDevice device = MockDevice.Start();
            (TcpDeviceChannel channel, DeviceSession session) = await ConnectAsync(device);
            await using TcpDeviceChannel _ = channel;

            Task<string> handshake = session.HandshakeAsync(CancellationToken.None);
            Assert.AreEqual("[DUT<-PC] START", await device.ReadLineAsync());
            Assert.AreEqual("hello tcp SUCCESS_CONNECT", await device.ReadLineAsync());
            await device.SendAsync("[DUT->PC] START_OK:aabbccddeeff#");

            Assert.AreEqual("aabbccddeeff", await handshake);
            Assert.AreEqual("aabbccddeeff", session.Mac);
        }

        [TestMethod]
        public async Task Handshake_UnexpectedResponse_Throws()
        {
            await using MockDevice device = MockDevice.Start();
            (TcpDeviceChannel channel, DeviceSession session) = await ConnectAsync(device);
            await using TcpDeviceChannel _ = channel;

            Task<string> handshake = session.HandshakeAsync(CancellationToken.None);
            await device.SendAsync("[DUT->PC] ERROR#");

            await Assert.ThrowsExactlyAsync<DeviceProtocolException>(() => handshake);
        }

        [TestMethod]
        [DataRow(DeviceCommand.ScanAp, "[DUT<-PC] SCAN_AP", "[DUT->PC] SCAN_AP_LIST:a:-40#")]
        [DataRow(DeviceCommand.OnCmd, "[DUT<-PC] ON_CMD", "[DUT->PC] ON_CMD_OK#")]
        [DataRow(DeviceCommand.OffCmd, "[DUT<-PC] OFF_CMD", "[DUT->PC] OFF_CMD_OK#")]
        [DataRow(DeviceCommand.Measurement, "[DUT<-PC] MEASUREMENT", "[DUT->PC] MEASUREMENT_W:1.5#")]
        [DataRow(DeviceCommand.OverLoad, "[DUT<-PC] OVERLOAD", "[DUT->PC] OVERLOAD_A:0#")]
        [DataRow(DeviceCommand.TestFactoryReset, "[DUT<-PC] FACTORY_RESET", "[DUT->PC] FACTORY_RESET_OK#")]
        [DataRow(DeviceCommand.ApScan, "{\"command\":\"apscan\"}", "{\"SCAN_AP_LIST\":{\"a:-40\"}}")]
        [DataRow(DeviceCommand.Reboot, "{\"command\":\"reboot\"}", "{\"mac\":\"aabbccddeeff\",\"respone\":\"reboot\"}")]
        [DataRow(DeviceCommand.FactoryReset, "{\"command\":\"factoryreset\"}", "{\"respone\":\"OK\"}")]
        public async Task Execute_SendsCommandAndReturnsResponse(DeviceCommand command, string expectedLine, string response)
        {
            await using MockDevice device = MockDevice.Start();
            (TcpDeviceChannel channel, DeviceSession session) = await ConnectAsync(device);
            await using TcpDeviceChannel _ = channel;

            Task<DeviceResponse> execute = session.ExecuteAsync(command, CancellationToken.None);
            Assert.AreEqual(expectedLine, await device.ReadLineAsync());
            await device.SendAsync(response);

            DeviceResponse result = await execute;
            Assert.AreEqual(response, result.Raw);
            Assert.IsFalse(result.Disconnected);
        }

        [TestMethod]
        [DataRow(DeviceCommand.Reboot)]
        [DataRow(DeviceCommand.FactoryReset)]
        [DataRow(DeviceCommand.TestFactoryReset)]
        public async Task Execute_TerminalCommand_DisconnectAllowed(DeviceCommand command)
        {
            await using MockDevice device = MockDevice.Start();
            (TcpDeviceChannel channel, DeviceSession session) = await ConnectAsync(device);
            await using TcpDeviceChannel _ = channel;

            Task<DeviceResponse> execute = session.ExecuteAsync(command, CancellationToken.None);
            await device.ReadLineAsync();
            await device.CloseAsync();

            DeviceResponse result = await execute;
            Assert.IsTrue(result.Disconnected);
        }

        [TestMethod]
        public async Task Execute_NonTerminalCommand_DisconnectThrows()
        {
            await using MockDevice device = MockDevice.Start();
            (TcpDeviceChannel channel, DeviceSession session) = await ConnectAsync(device);
            await using TcpDeviceChannel _ = channel;

            Task<DeviceResponse> execute = session.ExecuteAsync(DeviceCommand.OnCmd, CancellationToken.None);
            await device.ReadLineAsync();
            await device.CloseAsync();

            await Assert.ThrowsExactlyAsync<DeviceDisconnectedException>(() => execute);
        }

        [TestMethod]
        public async Task Execute_ConnectAp_Throws()
        {
            await using MockDevice device = MockDevice.Start();
            (TcpDeviceChannel channel, DeviceSession session) = await ConnectAsync(device);
            await using TcpDeviceChannel _ = channel;

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => session.ExecuteAsync(DeviceCommand.ConnectAp, CancellationToken.None));
        }
    }
}
