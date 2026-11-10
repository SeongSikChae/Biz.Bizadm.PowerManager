using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;

namespace Biz.Bizadm.PowerManager.DeviceConfig.App
{
    /// <summary>
    /// 후보에 번호를 붙여 출력하고 표준 입력 한 줄(번호)을 읽는다. 후보가 1개여도 입력을 받는다.
    /// </summary>
    internal static class ConsoleSelection
    {
        public static int Select(string title, IReadOnlyList<string> items)
        {
            Console.WriteLine(title);
            for (int i = 0; i < items.Count; i++)
                Console.WriteLine($"  [{i + 1}] {items[i]}");
            Console.Write("번호: ");
            string? line = Console.ReadLine();
            // 리다이렉트된 표준 입력은 앞에 BOM(U+FEFF)이 붙을 수 있다
            string? text = line?.Trim().TrimStart('﻿').Trim();
            if (!int.TryParse(text, out int number) || number < 1 || number > items.Count)
                throw new InvalidOperationException($"Invalid selection: {line}");
            return number - 1;
        }
    }

    internal sealed class ConsoleWifiProfileSelector : IWifiProfileSelector
    {
        public Task<WifiProfile> SelectAsync(IReadOnlyList<WifiCandidate> candidates, CancellationToken cancellationToken)
        {
            int index = ConsoleSelection.Select("장치가 찾은 등록 Wi-Fi",
                candidates.Select(candidate => $"{candidate.Profile.Ssid} ({candidate.Rssi} dBm)").ToList());
            return Task.FromResult(candidates[index].Profile);
        }
    }

    internal sealed class ConsoleDeviceApSelector : IDeviceApSelector
    {
        public Task<HostWifiNetwork> SelectAsync(IReadOnlyList<HostWifiNetwork> candidates, CancellationToken cancellationToken)
        {
            int index = ConsoleSelection.Select("PC가 찾은 장치 AP",
                candidates.Select(candidate => $"{candidate.Ssid} (signal {candidate.Signal})").ToList());
            return Task.FromResult(candidates[index]);
        }
    }
}
