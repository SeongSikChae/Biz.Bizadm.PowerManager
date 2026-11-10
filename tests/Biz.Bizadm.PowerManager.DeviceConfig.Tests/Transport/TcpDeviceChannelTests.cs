using Biz.Bizadm.PowerManager.DeviceConfig.Tests.TestSupport;
using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.Transport
{
    [TestClass]
    public sealed class TcpDeviceChannelTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        private static Task<TcpDeviceChannel> ConnectAsync(MockDevice device) =>
            TcpDeviceChannel.ConnectAsync(device.Host, device.Port, Timeout, NullLogger.Instance, CancellationToken.None);

        [TestMethod]
        public async Task SendLine_AppendsNewline()
        {
            await using MockDevice device = MockDevice.Start();
            await using TcpDeviceChannel channel = await ConnectAsync(device);

            await channel.SendLineAsync("[DUT<-PC] START", CancellationToken.None);
            await channel.SendLineAsync("hello tcp SUCCESS_CONNECT", CancellationToken.None);

            Assert.AreEqual("[DUT<-PC] START", await device.ReadLineAsync());
            Assert.AreEqual("hello tcp SUCCESS_CONNECT", await device.ReadLineAsync());
        }

        [TestMethod]
        public async Task Receive_FragmentsAndSkipsButtonEvent()
        {
            await using MockDevice device = MockDevice.Start();
            await using TcpDeviceChannel channel = await ConnectAsync(device);

            Task<string> receive = channel.ReceiveAsync(DeviceFrame.Test, Timeout, CancellationToken.None);
            await device.SendAsync("[DUT->PC] BUTTON_ON_EVT#[DUT->PC] START_");
            await Task.Delay(50);
            await device.SendAsync("OK:aabbccddeeff#");

            Assert.AreEqual("[DUT->PC] START_OK:aabbccddeeff#", await receive);
        }

        [TestMethod]
        public async Task Receive_KeepsRemainderForNextFrame()
        {
            await using MockDevice device = MockDevice.Start();
            await using TcpDeviceChannel channel = await ConnectAsync(device);

            await device.SendAsync("{\"a\":1}\n{\"b\":{\"c\":2}}");

            Assert.AreEqual("{\"a\":1}", await channel.ReceiveAsync(DeviceFrame.Json, Timeout, CancellationToken.None));
            Assert.AreEqual("{\"b\":{\"c\":2}}", await channel.ReceiveAsync(DeviceFrame.Json, Timeout, CancellationToken.None));
        }

        [TestMethod]
        public async Task Receive_Timeout_ThrowsWithPartial()
        {
            await using MockDevice device = MockDevice.Start();
            await using TcpDeviceChannel channel = await ConnectAsync(device);

            await device.SendAsync("{\"SCAN_AP_LIST\":{");

            TimeoutException ex = await Assert.ThrowsExactlyAsync<TimeoutException>(
                () => channel.ReceiveAsync(DeviceFrame.Json, TimeSpan.FromMilliseconds(300), CancellationToken.None));
            Assert.Contains("SCAN_AP_LIST", ex.Message);
        }

        [TestMethod]
        public async Task Receive_DeviceClosed_ThrowsDisconnected()
        {
            await using MockDevice device = MockDevice.Start();
            await using TcpDeviceChannel channel = await ConnectAsync(device);

            await device.SendAsync("[DUT->PC] PART");
            await device.CloseAsync();

            DeviceDisconnectedException ex = await Assert.ThrowsExactlyAsync<DeviceDisconnectedException>(
                () => channel.ReceiveAsync(DeviceFrame.Test, Timeout, CancellationToken.None));
            Assert.AreEqual("[DUT->PC] PART", ex.Received);
        }

        [TestMethod]
        public async Task Receive_CallerCancellation_IsNotTimeout()
        {
            await using MockDevice device = MockDevice.Start();
            await using TcpDeviceChannel channel = await ConnectAsync(device);
            using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(200));

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => channel.ReceiveAsync(DeviceFrame.Test, Timeout, cts.Token));
        }
    }
}
