using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Microsoft.Extensions.Logging;

namespace Biz.Bizadm.PowerManager.DeviceConfig
{
    /// <param name="ApSsidPrefix">장치 AP로 볼 SSID 접두사(기본 <c>DWD-</c>).</param>
    /// <param name="FallbackHost">장치 AP를 찾지 못했거나 게이트웨이를 얻지 못할 때 쓸 주소.</param>
    /// <param name="ConnectTimeout">장치 AP 접속 후 IPv4를 기다리는 시간.</param>
    public sealed record DeviceApOptions(string ApSsidPrefix, string FallbackHost, TimeSpan ConnectTimeout);

    /// <param name="Host">장치 TCP 접속 주소.</param>
    /// <param name="DeviceApSsid">PC가 접속한 장치 AP. 설정 주소를 쓰면 null.</param>
    public sealed record DeviceEndpoint(string Host, string? DeviceApSsid);

    /// <summary>
    /// PC Wi‑Fi로 찾은 장치 AP 후보에서 하나를 고른다. 콘솔·MCP 등 구현을 바꿔 끼우는 지점이다.
    /// </summary>
    public interface IDeviceApSelector
    {
        /// <param name="candidates">신호 세기 내림차순, 1개 이상.</param>
        Task<HostWifiNetwork> SelectAsync(IReadOnlyList<HostWifiNetwork> candidates, CancellationToken cancellationToken);
    }

    /// <summary>
    /// PC Wi‑Fi로 장치 AP를 찾아 접속하고 장치 주소(게이트웨이)를 정한다. Dispose 시 임시 프로필을 지우고 원래 연결로 복구한다.
    /// </summary>
    public sealed class DeviceApLocator(
        IHostWifi? hostWifi,
        IDeviceApSelector selector,
        DeviceApOptions options,
        ILogger logger,
        INetworkInterfaceInspector? inspector = null,
        TimeSpan? pollInterval = null) : IAsyncDisposable
    {
        private readonly INetworkInterfaceInspector inspector = inspector ?? new SystemNetworkInterfaceInspector();
        private readonly TimeSpan pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);
        private bool connectAttempted;
        private HostWifiConnection? previous;
        private HostWifiConnection? temporary;

        public async Task<DeviceEndpoint> LocateAsync(CancellationToken cancellationToken)
        {
            if (hostWifi is null)
                return Fallback("host Wi-Fi control is not supported on this OS");

            IReadOnlyList<HostWifiNetwork> found;
            try
            {
                found = await hostWifi.ScanAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Host Wi-Fi scan failed");
                return Fallback("host Wi-Fi scan failed");
            }

            List<HostWifiNetwork> candidates = found
                .Where(network => network.Ssid.StartsWith(options.ApSsidPrefix, StringComparison.Ordinal))
                .GroupBy(network => network.Ssid, StringComparer.Ordinal)
                .Select(group => group.MaxBy(network => network.Signal)!)
                .OrderByDescending(network => network.Signal)
                .ToList();
            if (candidates.Count == 0)
                return Fallback($"no device AP starting with '{options.ApSsidPrefix}'");

            HostWifiConnection? current = await GetCurrentOrNullAsync(cancellationToken);
            if (current is not null && candidates.Any(candidate => candidate.Ssid == current.Ssid))
            {
                logger.LogInformation("Already connected to device AP {Ssid}", current.Ssid);
                return await ResolveAsync(current, cancellationToken);
            }

            HostWifiNetwork chosen = await selector.SelectAsync(candidates, cancellationToken);
            previous = current;
            connectAttempted = true;
            logger.LogInformation("Connecting to device AP {Ssid} (previous: {Previous})", chosen.Ssid, previous?.Ssid ?? "none");
            temporary = await hostWifi.ConnectOpenAsync(chosen.Ssid, cancellationToken);
            return await ResolveAsync(temporary, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (hostWifi is null || !connectAttempted)
                return;
            try
            {
                if (temporary is not null)
                    await hostWifi.RemoveTemporaryAsync(temporary, CancellationToken.None);
                if (previous is not null)
                {
                    await hostWifi.RestoreAsync(previous, CancellationToken.None);
                    logger.LogInformation("Restored Wi-Fi {Ssid}", previous.Ssid);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Host Wi-Fi restore failed");
            }
        }

        private async Task<HostWifiConnection?> GetCurrentOrNullAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await hostWifi!.GetCurrentAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to read the current Wi-Fi connection; it will not be restored");
                return null;
            }
        }

        /// <summary>
        /// PC의 현재 연결 SSID가 대상과 같아지고 IPv4를 받으면 게이트웨이를 장치 주소로 쓴다.
        /// SSID를 먼저 확인하지 않으면 직전 연결의 남은 IP·게이트웨이를 접속 완료로 오판한다(실장비 확인).
        /// </summary>
        private async Task<DeviceEndpoint> ResolveAsync(HostWifiConnection connection, CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow + options.ConnectTimeout;
            HostWifiConnection? current = null;
            while (true)
            {
                current = await TryGetCurrentAsync(cancellationToken);
                InterfaceAddressState? state = current?.Ssid == connection.Ssid ? inspector.Inspect(connection.InterfaceId) : null;
                if (state is { HasIPv4: true })
                {
                    if (state.Gateway is null)
                    {
                        logger.LogWarning("No gateway on {Interface}; using {Host}", connection.InterfaceId, options.FallbackHost);
                        return new DeviceEndpoint(options.FallbackHost, connection.Ssid);
                    }
                    logger.LogInformation("Device AP {Ssid} gateway {Gateway}", connection.Ssid, state.Gateway);
                    return new DeviceEndpoint(state.Gateway, connection.Ssid);
                }
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException(current?.Ssid == connection.Ssid
                        ? $"No IPv4 address on {connection.InterfaceId} after connecting to {connection.Ssid}"
                        : $"PC Wi-Fi did not connect to {connection.Ssid} (current: {current?.Ssid ?? "none"})");
                await Task.Delay(pollInterval, cancellationToken);
            }
        }

        private async Task<HostWifiConnection?> TryGetCurrentAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await hostWifi!.GetCurrentAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Failed to read the current Wi-Fi connection while waiting");
                return null;
            }
        }

        private DeviceEndpoint Fallback(string reason)
        {
            logger.LogInformation("Using configured device host {Host}: {Reason}", options.FallbackHost, reason);
            return new DeviceEndpoint(options.FallbackHost, null);
        }
    }
}
