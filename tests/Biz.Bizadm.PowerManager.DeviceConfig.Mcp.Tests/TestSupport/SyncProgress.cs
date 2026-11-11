using ModelContextProtocol;
using System.Collections.Concurrent;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests.TestSupport
{
    /// <summary>Report를 호출한 스레드에서 바로 기록한다(Progress&lt;T&gt;처럼 SynchronizationContext로 미루지 않음).</summary>
    internal sealed class SyncProgress : IProgress<ProgressNotificationValue>
    {
        public ConcurrentQueue<ProgressNotificationValue> Reports { get; } = new();

        public void Report(ProgressNotificationValue value) => Reports.Enqueue(value);
    }
}
