using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;

namespace Biz.Bizadm.PowerManager.DeviceConfig.App
{
    /// <summary>
    /// 콘솔 번호 선택. 표준 입력이 닫히면(EOF) InvalidOperationException(종료 코드 3).
    /// 입력 대기 중 Ctrl+C는 별도 처리하지 않는다.
    /// </summary>
    internal sealed class ConsoleDeviceConfigSelector : IDeviceConfigSelector
    {
        public Task<WifiAdapter> SelectAdapterAsync(IReadOnlyList<WifiAdapter> adapters, CancellationToken cancellationToken) =>
            Task.FromResult(Select("wireless adapter", adapters, a => a.Name));

        public Task<WifiNetwork> SelectDeviceApAsync(IReadOnlyList<WifiNetwork> deviceAps, CancellationToken cancellationToken) =>
            Task.FromResult(Select("device AP", deviceAps, n => $"{n.Ssid} (signal {n.Signal})"));

        // 같은 SSID가 여러 개여도 RSSI로 구분한다
        public Task<WifiProfile> SelectWifiProfileAsync(IReadOnlyList<WifiProfileCandidate> candidates, CancellationToken cancellationToken) =>
            Task.FromResult(Select("router Wi-Fi", candidates, c => $"{c.Profile.Ssid} ({c.Signal} dBm)").Profile);

        private static T Select<T>(string title, IReadOnlyList<T> items, Func<T, string> label)
        {
            if (items.Count == 0)
                throw new InvalidOperationException($"No {title} found");
            Console.WriteLine($"Select {title}:");
            for (int i = 0; i < items.Count; i++)
                Console.WriteLine($"  [{i + 1}] {label(items[i])}");
            while (true)
            {
                Console.Write($"Select {title} (1-{items.Count}): ");
                string line = Console.ReadLine() ?? throw new InvalidOperationException("Console input closed");
                if (int.TryParse(line, out int n) && n >= 1 && n <= items.Count)
                    return items[n - 1];
            }
        }
    }
}
