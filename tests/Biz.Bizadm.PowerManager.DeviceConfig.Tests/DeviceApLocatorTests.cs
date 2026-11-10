using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Microsoft.Extensions.Logging.Abstractions;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests
{
    [TestClass]
    public sealed class DeviceApLocatorTests
    {
        private static readonly DeviceApOptions Options = new("DWD-", "192.168.244.1", TimeSpan.FromMilliseconds(300));

        private sealed class FakeHostWifi : IHostWifi
        {
            private HostWifiConnection? connected;
            private int callsAfterConnect;

            public IReadOnlyList<HostWifiNetwork> Networks { get; init; } = [];
            public Exception? ScanError { get; init; }
            public HostWifiConnection? Current { get; set; }
            public Exception? RestoreError { get; init; }
            /// <summary>접속 요청 후 현재 연결이 바뀌기 전까지 이전 연결을 돌려주는 GetCurrent 호출 수.</summary>
            public int AssociateAfterCalls { get; init; }
            public bool NeverAssociate { get; init; }
            public List<string> Calls { get; } = [];

            public Task<IReadOnlyList<HostWifiNetwork>> ScanAsync(CancellationToken cancellationToken)
            {
                Calls.Add("scan");
                return ScanError is null ? Task.FromResult(Networks) : Task.FromException<IReadOnlyList<HostWifiNetwork>>(ScanError);
            }

            public Task<HostWifiConnection?> GetCurrentAsync(CancellationToken cancellationToken)
            {
                if (connected is not null && !NeverAssociate && callsAfterConnect++ >= AssociateAfterCalls)
                    Current = connected;
                return Task.FromResult(Current);
            }

            public Task<HostWifiConnection> ConnectOpenAsync(string ssid, CancellationToken cancellationToken)
            {
                Calls.Add($"connect {ssid}");
                connected = new HostWifiConnection("wlan-if", ssid, "temp");
                return Task.FromResult(connected);
            }

            public Task RestoreAsync(HostWifiConnection previous, CancellationToken cancellationToken)
            {
                Calls.Add($"restore {previous.Ssid}");
                return RestoreError is null ? Task.CompletedTask : Task.FromException(RestoreError);
            }

            public Task RemoveTemporaryAsync(HostWifiConnection temporary, CancellationToken cancellationToken)
            {
                Calls.Add($"remove {temporary.ProfileName}");
                return Task.CompletedTask;
            }
        }

        private sealed class FakeInspector(InterfaceAddressState? state) : INetworkInterfaceInspector
        {
            public InterfaceAddressState? Inspect(string interfaceId) => state;
        }

        /// <summary>
        /// 현재 연결에 따라 주소 상태가 달라지는 인터페이스(직전 연결의 IP가 남은 상황 재현).
        /// </summary>
        private sealed class ConnectionAwareInspector(FakeHostWifi wifi, string deviceSsid) : INetworkInterfaceInspector
        {
            public InterfaceAddressState? Inspect(string interfaceId) => wifi.Current?.Ssid == deviceSsid
                ? new InterfaceAddressState(true, "192.168.244.1")
                : new InterfaceAddressState(true, "192.168.0.1");
        }

        private sealed class FirstSelector : IDeviceApSelector
        {
            public IReadOnlyList<HostWifiNetwork>? Candidates { get; private set; }

            public Task<HostWifiNetwork> SelectAsync(IReadOnlyList<HostWifiNetwork> candidates, CancellationToken cancellationToken)
            {
                Candidates = candidates;
                return Task.FromResult(candidates[0]);
            }
        }

        private static DeviceApLocator Locator(IHostWifi? wifi, InterfaceAddressState? state = null, FirstSelector? selector = null) =>
            new(wifi, selector ?? new FirstSelector(), Options, NullLogger.Instance,
                new FakeInspector(state ?? new InterfaceAddressState(true, "192.168.244.1")), TimeSpan.FromMilliseconds(20));

        [TestMethod]
        public async Task NotSupported_UsesFallback()
        {
            await using DeviceApLocator locator = Locator(null);
            Assert.AreEqual(new DeviceEndpoint("192.168.244.1", null), await locator.LocateAsync(CancellationToken.None));
        }

        [TestMethod]
        public async Task ScanFails_UsesFallback()
        {
            FakeHostWifi wifi = new() { ScanError = new HostWifiException("access denied") };
            await using DeviceApLocator locator = Locator(wifi);

            Assert.AreEqual(new DeviceEndpoint("192.168.244.1", null), await locator.LocateAsync(CancellationToken.None));
        }

        [TestMethod]
        public async Task NoDeviceAp_UsesFallbackWithoutConnecting()
        {
            FakeHostWifi wifi = new() { Networks = [new("HomeAP", 80), new("dwd-lower", 70)] };
            await using (DeviceApLocator locator = Locator(wifi))
                Assert.IsNull((await locator.LocateAsync(CancellationToken.None)).DeviceApSsid);

            CollectionAssert.AreEqual(new[] { "scan" }, wifi.Calls);
        }

        [TestMethod]
        public async Task AlreadyConnectedToDeviceAp_NoConnectNoRestore()
        {
            FakeHostWifi wifi = new()
            {
                Networks = [new("DWD-LS120_a1b2c3", 80)],
                Current = new HostWifiConnection("wlan-if", "DWD-LS120_a1b2c3", "DWD-LS120_a1b2c3"),
            };
            await using (DeviceApLocator locator = Locator(wifi, new InterfaceAddressState(true, "192.168.244.1")))
                Assert.AreEqual(new DeviceEndpoint("192.168.244.1", "DWD-LS120_a1b2c3"), await locator.LocateAsync(CancellationToken.None));

            CollectionAssert.AreEqual(new[] { "scan" }, wifi.Calls);
        }

        [TestMethod]
        public async Task SelectsConnectsUsesGatewayAndRestoresOnDispose()
        {
            FakeHostWifi wifi = new()
            {
                Networks = [new("DWD-LS120_000001", 40), new("HomeAP", 90), new("DWD-LS120_000002", 70), new("DWD-LS120_000001", 60)],
                Current = new HostWifiConnection("wlan-if", "HomeAP", "HomeAP"),
            };
            FirstSelector selector = new();

            await using (DeviceApLocator locator = Locator(wifi, new InterfaceAddressState(true, "192.168.4.254"), selector))
                Assert.AreEqual(new DeviceEndpoint("192.168.4.254", "DWD-LS120_000002"), await locator.LocateAsync(CancellationToken.None));

            CollectionAssert.AreEqual(
                new[] { new HostWifiNetwork("DWD-LS120_000002", 70), new HostWifiNetwork("DWD-LS120_000001", 60) },
                selector.Candidates!.ToArray());
            CollectionAssert.AreEqual(new[] { "scan", "connect DWD-LS120_000002", "remove temp", "restore HomeAP" }, wifi.Calls);
        }

        [TestMethod]
        public async Task NoGateway_UsesFallbackHost()
        {
            FakeHostWifi wifi = new() { Networks = [new("DWD-x", 70)] };
            await using DeviceApLocator locator = Locator(wifi, new InterfaceAddressState(true, null));

            Assert.AreEqual(new DeviceEndpoint("192.168.244.1", "DWD-x"), await locator.LocateAsync(CancellationToken.None));
        }

        [TestMethod]
        public async Task NoIPv4_TimesOut_AndStillRestores()
        {
            FakeHostWifi wifi = new()
            {
                Networks = [new("DWD-x", 70)],
                Current = new HostWifiConnection("wlan-if", "HomeAP", "HomeAP"),
            };
            DeviceApLocator locator = Locator(wifi, new InterfaceAddressState(false, null));

            await Assert.ThrowsExactlyAsync<TimeoutException>(() => locator.LocateAsync(CancellationToken.None));
            await locator.DisposeAsync();

            CollectionAssert.AreEqual(new[] { "scan", "connect DWD-x", "remove temp", "restore HomeAP" }, wifi.Calls);
        }

        [TestMethod]
        public async Task StaleAddressBeforeAssociation_WaitsForDeviceSsid()
        {
            FakeHostWifi wifi = new()
            {
                Networks = [new("DWD-x", 70)],
                Current = new HostWifiConnection("wlan-if", "HomeAP", "HomeAP"),
                AssociateAfterCalls = 3,
            };
            await using DeviceApLocator locator = new(wifi, new FirstSelector(), Options, NullLogger.Instance,
                new ConnectionAwareInspector(wifi, "DWD-x"), TimeSpan.FromMilliseconds(20));

            Assert.AreEqual(new DeviceEndpoint("192.168.244.1", "DWD-x"), await locator.LocateAsync(CancellationToken.None));
        }

        [TestMethod]
        public async Task NeverAssociates_TimesOutEvenWithIPv4()
        {
            FakeHostWifi wifi = new()
            {
                Networks = [new("DWD-x", 70)],
                Current = new HostWifiConnection("wlan-if", "HomeAP", "HomeAP"),
                NeverAssociate = true,
            };
            DeviceApLocator locator = Locator(wifi, new InterfaceAddressState(true, "192.168.0.1"));

            TimeoutException ex = await Assert.ThrowsExactlyAsync<TimeoutException>(() => locator.LocateAsync(CancellationToken.None));
            Assert.Contains("did not connect to DWD-x", ex.Message);
            await locator.DisposeAsync();
            CollectionAssert.Contains(wifi.Calls, "restore HomeAP");
        }

        [TestMethod]
        public async Task RestoreFailure_IsSwallowed()
        {
            FakeHostWifi wifi = new()
            {
                Networks = [new("DWD-x", 70)],
                Current = new HostWifiConnection("wlan-if", "HomeAP", "HomeAP"),
                RestoreError = new HostWifiException("restore failed"),
            };
            DeviceApLocator locator = Locator(wifi);
            await locator.LocateAsync(CancellationToken.None);

            await locator.DisposeAsync();

            CollectionAssert.Contains(wifi.Calls, "restore HomeAP");
        }
    }
}
