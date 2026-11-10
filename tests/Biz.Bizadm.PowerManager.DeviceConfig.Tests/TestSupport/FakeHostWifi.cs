using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using System.Net;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.TestSupport
{
    /// <summary>호출을 기록하는 모의 PC Wi-Fi. Throw* 속성이 있으면 그 메서드에서 던진다.</summary>
    internal sealed class FakeHostWifi : IHostWifi
    {
        public static readonly WifiAdapter Adapter = new("{00000000-0000-0000-0000-000000000001}", "Test Wi-Fi");

        public IReadOnlyList<WifiAdapter> Adapters { get; init; } = [Adapter];
        public IReadOnlyList<WifiNetwork> Scanned { get; init; } = [new("DWD-LS120_000001", -40), new("HomeRouter", -50)];
        public string? CurrentSsid { get; init; } = "HomeRouter";
        public IPAddress? Gateway { get; init; } = IPAddress.Loopback;

        public Exception? ThrowOnListAdapters { get; init; }
        public Exception? ThrowOnScan { get; init; }
        public Exception? ThrowOnConnect { get; init; }
        public Exception? ThrowOnRestore { get; init; }

        public List<string> Calls { get; } = [];
        public (WifiAdapter Adapter, string Ssid, TimeSpan Timeout)? Connected { get; private set; }
        public (WifiAdapter Adapter, string? PreviousSsid)? Restored { get; private set; }

        public Task<IReadOnlyList<WifiAdapter>> ListAdaptersAsync(CancellationToken cancellationToken)
        {
            Calls.Add(nameof(ListAdaptersAsync));
            return ThrowOnListAdapters is null ? Task.FromResult(Adapters) : throw ThrowOnListAdapters;
        }

        public Task<IReadOnlyList<WifiNetwork>> ScanAsync(WifiAdapter adapter, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(ScanAsync));
            return ThrowOnScan is null ? Task.FromResult(Scanned) : throw ThrowOnScan;
        }

        public Task<string?> GetCurrentSsidAsync(WifiAdapter adapter, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(GetCurrentSsidAsync));
            return Task.FromResult(CurrentSsid);
        }

        public Task ConnectOpenAsync(WifiAdapter adapter, string ssid, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(ConnectOpenAsync));
            Connected = (adapter, ssid, timeout);
            return ThrowOnConnect is null ? Task.CompletedTask : throw ThrowOnConnect;
        }

        public Task<IPAddress?> GetGatewayAsync(WifiAdapter adapter, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(GetGatewayAsync));
            return Task.FromResult(Gateway);
        }

        public Task RestoreAsync(WifiAdapter adapter, string? previousSsid, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(RestoreAsync));
            Restored = (adapter, previousSsid);
            return ThrowOnRestore is null ? Task.CompletedTask : throw ThrowOnRestore;
        }
    }
}
