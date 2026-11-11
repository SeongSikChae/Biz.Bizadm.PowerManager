using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using System.Net;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests.TestSupport
{
    /// <summary>
    /// IHostWifi 래퍼. ConnectOpenAsync에 들어오면 <see cref="Entered"/>를 알리고 <see cref="Release"/>가 끝날 때까지 기다린다.
    /// 동시에 ConnectOpenAsync 안에 있던 호출 수의 최댓값을 기록한다.
    /// </summary>
    internal sealed class GatedHostWifi(IHostWifi inner) : IHostWifi
    {
        private int inside;
        private int maxInside;
        private int enteredCount;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxConcurrent => Volatile.Read(ref maxInside);

        public int EnteredCount => Volatile.Read(ref enteredCount);

        public async Task ConnectOpenAsync(WifiAdapter adapter, string ssid, TimeSpan timeout, CancellationToken cancellationToken)
        {
            int now = Interlocked.Increment(ref inside);
            Interlocked.Increment(ref enteredCount);
            int seen;
            while (now > (seen = Volatile.Read(ref maxInside)) && Interlocked.CompareExchange(ref maxInside, now, seen) != seen)
            {
            }
            Entered.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                await inner.ConnectOpenAsync(adapter, ssid, timeout, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref inside);
            }
        }

        public Task<IReadOnlyList<WifiAdapter>> ListAdaptersAsync(CancellationToken cancellationToken) => inner.ListAdaptersAsync(cancellationToken);

        public Task<IReadOnlyList<WifiNetwork>> ScanAsync(WifiAdapter adapter, CancellationToken cancellationToken) => inner.ScanAsync(adapter, cancellationToken);

        public Task<string?> GetCurrentSsidAsync(WifiAdapter adapter, CancellationToken cancellationToken) => inner.GetCurrentSsidAsync(adapter, cancellationToken);

        public Task<IPAddress?> GetGatewayAsync(WifiAdapter adapter, CancellationToken cancellationToken) => inner.GetGatewayAsync(adapter, cancellationToken);

        public Task RestoreAsync(WifiAdapter adapter, string? previousSsid, CancellationToken cancellationToken) => inner.RestoreAsync(adapter, previousSsid, cancellationToken);
    }
}
