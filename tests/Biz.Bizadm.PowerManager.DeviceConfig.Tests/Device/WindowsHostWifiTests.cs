using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Tests.TestSupport;
using System.Diagnostics;
using System.Net;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.Device
{
    /// <summary>
    /// 실제 PC Wi-Fi·장치 AP를 쓰는 Device 카테고리 테스트(기본 dotnet test에서 제외).
    /// 환경 변수 DEVICE_AP_SSID(장치 SoftAP SSID)가 필요하다. 어댑터는 첫 번째를 쓴다.
    /// 실행: dotnet test -p:TestingPlatformCommandLineArguments= --filter "TestCategory=Device"
    /// </summary>
    [TestClass]
    [TestCategory("Device")]
    [OSCondition(OperatingSystems.Windows)]
    public sealed class WindowsHostWifiTests
    {
        private const string TemporaryProfileName = "B540-Config-Temp";
        private static readonly TimeSpan ApConnectTimeout = TimeSpan.FromSeconds(30);

        public TestContext TestContext { get; set; } = null!;

        private static string DeviceApSsid =>
            Environment.GetEnvironmentVariable("DEVICE_AP_SSID") ?? throw new AssertInconclusiveException("DEVICE_AP_SSID not set");

        private static IHostWifi HostWifi(ListLogger logger) => HostWifiLoader.CreateForCurrentOs(logger);

        private static string ShowProfiles()
        {
            using Process process = Process.Start(new ProcessStartInfo("netsh", "wlan show profiles") { RedirectStandardOutput = true })!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }

        private async Task<WifiAdapter> FirstAdapterAsync(IHostWifi hostWifi)
        {
            IReadOnlyList<WifiAdapter> adapters = await hostWifi.ListAdaptersAsync(TestContext.CancellationToken);
            Assert.IsTrue(adapters.Count > 0, "no wireless adapter");
            TestContext.WriteLine($"adapter: {adapters[0].Name} {adapters[0].Id}");
            return adapters[0];
        }

        [TestMethod]
        public async Task ScanFindsDeviceAp()
        {
            IHostWifi hostWifi = HostWifi(new ListLogger());
            WifiAdapter adapter = await FirstAdapterAsync(hostWifi);

            IReadOnlyList<WifiNetwork> networks = await hostWifi.ScanAsync(adapter, TestContext.CancellationToken);

            Assert.IsTrue(networks.Any(n => n.Ssid == DeviceApSsid), "device AP not found by PC scan");
        }

        [TestMethod]
        public async Task ConnectGetsDeviceGateway_ThenRestores()
        {
            IHostWifi hostWifi = HostWifi(new ListLogger());
            WifiAdapter adapter = await FirstAdapterAsync(hostWifi);
            string? previous = await hostWifi.GetCurrentSsidAsync(adapter, TestContext.CancellationToken);
            TestContext.WriteLine($"previous: {previous ?? "(disconnected)"}");
            await hostWifi.ScanAsync(adapter, TestContext.CancellationToken);

            try
            {
                await hostWifi.ConnectOpenAsync(adapter, DeviceApSsid, ApConnectTimeout, TestContext.CancellationToken);
                Assert.AreEqual(DeviceApSsid, await hostWifi.GetCurrentSsidAsync(adapter, TestContext.CancellationToken));
                Assert.AreEqual(IPAddress.Parse("192.168.244.1"), await hostWifi.GetGatewayAsync(adapter, TestContext.CancellationToken));
            }
            finally
            {
                await hostWifi.RestoreAsync(adapter, previous, TestContext.CancellationToken);
            }

            Assert.IsFalse(ShowProfiles().Contains(TemporaryProfileName), "temporary profile not deleted");
            await AssertRestoredAsync(hostWifi, adapter, previous);
        }

        [TestMethod]
        public async Task ConnectUnknownSsid_Fails_ThenRestores()
        {
            IHostWifi hostWifi = HostWifi(new ListLogger());
            WifiAdapter adapter = await FirstAdapterAsync(hostWifi);
            string? previous = await hostWifi.GetCurrentSsidAsync(adapter, TestContext.CancellationToken);

            // 실측(2026-11-11): WlanConnect는 바로 성공을 돌려주고 대기 시간 초과로 TimeoutException
            await Assert.ThrowsExactlyAsync<TimeoutException>(
                () => hostWifi.ConnectOpenAsync(adapter, "DWD-NOT-EXIST-000000", TimeSpan.FromSeconds(5), TestContext.CancellationToken));
            await hostWifi.RestoreAsync(adapter, previous, TestContext.CancellationToken);

            Assert.IsFalse(ShowProfiles().Contains(TemporaryProfileName), "temporary profile not deleted");
            await AssertRestoredAsync(hostWifi, adapter, previous);
        }

        private async Task AssertRestoredAsync(IHostWifi hostWifi, WifiAdapter adapter, string? previous)
        {
            // 재연결은 비동기이므로 잠시 기다린다
            string? current = null;
            for (int i = 0; i < 30; i++)
            {
                current = await hostWifi.GetCurrentSsidAsync(adapter, TestContext.CancellationToken);
                if (current == previous)
                    break;
                await Task.Delay(1000, TestContext.CancellationToken);
            }
            Assert.AreEqual(previous, current, "PC Wi-Fi not restored");
        }
    }
}
