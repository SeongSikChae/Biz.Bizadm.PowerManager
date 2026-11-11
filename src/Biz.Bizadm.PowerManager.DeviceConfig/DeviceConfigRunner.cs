using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;
using Microsoft.Extensions.Logging;

namespace Biz.Bizadm.PowerManager.DeviceConfig
{
    /// <summary>
    /// 어댑터 선택 → 장치 AP 선택·접속 → Commands 순서대로 실행(명령마다 재연결) → PC Wi-Fi 복구.
    /// PC Wi-Fi가 미구현(NotImplementedException)이면 경고 후 Host로 접속한다. 그 밖의 실패는 그대로 전파한다.
    /// ConnectOpenAsync를 호출했으면 성공·실패·취소와 관계없이 복구한다.
    /// </summary>
    public sealed class DeviceConfigRunner(IHostWifi hostWifi, IDeviceConfigSelector selector, DeviceConfigOptions options, ILogger<DeviceConfigRunner> logger)
    {
        public Task RunAsync(CancellationToken cancellationToken) => RunAsync(null, cancellationToken);

        /// <param name="onCommandCompleted">명령이 성공할 때마다 호출된다. 실패한 명령은 통보하지 않는다.</param>
        public async Task RunAsync(Action<DeviceCommandResult>? onCommandCompleted, CancellationToken cancellationToken)
        {
            WifiAdapter? connectedAdapter = null; // ConnectOpenAsync를 호출한 어댑터(복구 대상)
            string? previousSsid = null;
            bool failed = false;
            try
            {
                string host;
                try
                {
                    WifiAdapter adapter = await selector.SelectAdapterAsync(await hostWifi.ListAdaptersAsync(cancellationToken), cancellationToken);
                    previousSsid = await hostWifi.GetCurrentSsidAsync(adapter, cancellationToken);
                    IReadOnlyList<WifiNetwork> deviceAps = (await hostWifi.ScanAsync(adapter, cancellationToken))
                        .Where(n => n.Ssid.StartsWith(options.ApPrefix, StringComparison.Ordinal))
                        .ToList();
                    WifiNetwork deviceAp = await selector.SelectDeviceApAsync(deviceAps, cancellationToken);
                    connectedAdapter = adapter; // 이 시점부터는 실패·취소에도 복구한다
                    await hostWifi.ConnectOpenAsync(adapter, deviceAp.Ssid, options.ApConnectTimeout, cancellationToken);
                    host = (await hostWifi.GetGatewayAsync(adapter, cancellationToken))?.ToString()
                        ?? throw new InvalidOperationException("Device AP gateway not found"); // ConnectOpenAsync가 게이트웨이까지 기다리므로 정상 경로에서는 오지 않음
                    logger.LogInformation("Connected to device AP {Ssid}; device host {Host}", deviceAp.Ssid, host);
                }
                catch (NotImplementedException)
                {
                    logger.LogWarning("PC Wi-Fi is not implemented on this OS; connecting to configured host {Host}", options.Host);
                    host = options.Host;
                }

                DeviceClient client = new(host, options.Port, options.Timeout, logger);
                foreach (DeviceCommand command in options.Commands)
                {
                    logger.LogInformation("Command {Command}", command);
                    if (command == DeviceCommand.ConnectAp)
                    {
                        string? selectedSsid = null;
                        await client.ConnectApAsync(async (scanned, ct) =>
                            {
                                (string Ssid, string Pass) selected = await SelectWifiAsync(scanned, ct);
                                selectedSsid = selected.Ssid;
                                return selected;
                            },
                            options.Settings ?? throw new InvalidOperationException("Settings is required for ConnectAp"), cancellationToken);
                        onCommandCompleted?.Invoke(new DeviceCommandResult(command, $"OK ({selectedSsid})"));
                    }
                    else
                    {
                        string response = await client.ExecuteAsync(command, cancellationToken);
                        logger.LogInformation("{Command} response: {Response}", command, response);
                        onCommandCompleted?.Invoke(new DeviceCommandResult(command, response));
                    }
                }
                logger.LogInformation("Device configuration completed");
            }
            catch
            {
                failed = true;
                throw;
            }
            finally
            {
                if (connectedAdapter is not null)
                {
                    try
                    {
                        await hostWifi.RestoreAsync(connectedAdapter, previousSsid, CancellationToken.None);
                        logger.LogInformation("PC Wi-Fi restored (previous: {Ssid})", previousSsid ?? "disconnected");
                    }
                    catch (Exception ex) when (failed)
                    {
                        // 원래 실패 원인을 덮지 않도록 복구 실패는 로그만 남긴다
                        logger.LogError(ex, "PC Wi-Fi restore failed");
                    }
                }
            }
        }

        private async Task<(string Ssid, string Pass)> SelectWifiAsync(IReadOnlyList<WifiNetwork> scanned, CancellationToken cancellationToken)
        {
            IReadOnlyList<WifiProfileCandidate> candidates = WifiProfileMatcher.Match(options.WifiProfiles, scanned);
            if (candidates.Count == 0)
                throw new InvalidOperationException(
                    "No registered Wi-Fi found in device apscan result (if the router shares the device SoftAP channel, change the router channel and retry)");
            WifiProfile profile = await selector.SelectWifiProfileAsync(candidates, cancellationToken);
            return (profile.Ssid, profile.Pass);
        }
    }
}
