using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.TestSupport
{
    /// <summary>항상 첫 항목을 고르고 받은 목록을 기록하는 모의 선택기.</summary>
    internal sealed class FakeSelector : IDeviceConfigSelector
    {
        public IReadOnlyList<WifiNetwork>? OfferedDeviceAps { get; private set; }
        public IReadOnlyList<WifiProfileCandidate>? OfferedCandidates { get; private set; }

        public Task<WifiAdapter> SelectAdapterAsync(IReadOnlyList<WifiAdapter> adapters, CancellationToken cancellationToken) =>
            Task.FromResult(adapters[0]);

        public Task<WifiNetwork> SelectDeviceApAsync(IReadOnlyList<WifiNetwork> deviceAps, CancellationToken cancellationToken)
        {
            OfferedDeviceAps = deviceAps;
            return Task.FromResult(deviceAps[0]);
        }

        public Task<WifiProfile> SelectWifiProfileAsync(IReadOnlyList<WifiProfileCandidate> candidates, CancellationToken cancellationToken)
        {
            OfferedCandidates = candidates;
            return Task.FromResult(candidates[0].Profile);
        }
    }
}
