using Biz.Bizadm.PowerManager.DeviceConfig.App.Security;
using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Biz.Bizadm.PowerManager.DeviceConfig.App
{
    /// <summary>
    /// 장치 AP 탐색·접속 → TCP 연결 → 핸드셰이크 → Commands 순서 실행 → 연결 종료 → PC Wi‑Fi 복구 → 호스트 종료.
    /// 예외는 템플릿 기본 동작대로 호스트가 기록하고 정지한다(종료 코드 0).
    /// </summary>
    internal sealed class DeviceConfigService(
        AppConfiguration config,
        DecryptedSecrets secrets,
        IWifiProfileSelector wifiSelector,
        IDeviceApSelector apSelector,
        ILoggerFactory loggerFactory,
        IHostApplicationLifetime lifetime,
        ILogger<DeviceConfigService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            DeviceSection device = config.Device;
            DeviceApOptions apOptions = new(device.ApSsidPrefix, device.Host, TimeSpan.FromSeconds(device.ApConnectTimeoutSeconds));
            DeviceTimeouts timeouts = new(TimeSpan.FromSeconds(device.TimeoutSeconds), TimeSpan.FromSeconds(device.ApScanTimeoutSeconds));

            await using (DeviceApLocator locator = new(HostWifiFactory.Create(loggerFactory), apSelector, apOptions, logger))
            {
                DeviceEndpoint endpoint = await locator.LocateAsync(stoppingToken);
                logger.LogInformation("Device endpoint {Host}:{Port} (device AP: {Ssid})", endpoint.Host, device.Port, endpoint.DeviceApSsid ?? "none");

                await using TcpDeviceChannel channel = await TcpDeviceChannel.ConnectAsync(endpoint.Host, device.Port, timeouts.Response, logger, stoppingToken);
                DeviceSession session = new(channel, timeouts, logger);
                string mac = await session.HandshakeAsync(stoppingToken);
                logger.LogInformation("Device MAC {Mac}", mac);

                foreach (DeviceCommand command in config.Commands)
                {
                    DeviceResponse response = command == DeviceCommand.ConnectAp
                        ? await session.ConnectApAsync(CreateDeviceSettings(), wifiSelector, stoppingToken)
                        : await session.ExecuteAsync(command, stoppingToken);
                    // 응답 없이 끊긴 경우(Disconnected)는 DeviceSession이 경고로 남긴다
                    logger.LogInformation("{Command} response: {Response}", command, response.Raw);
                }
            }

            logger.LogInformation("Device configuration completed");
            lifetime.StopApplication();
        }

        private DeviceSettings CreateDeviceSettings()
        {
            SettingsSection settings = config.Settings!;
            return new DeviceSettings
            {
                Mac = settings.Mac,
                UserId = settings.UserId,
                Company = settings.Company,
                Model = settings.Model,
                Latitude = settings.Latitude,
                Longitude = settings.Longitude,
                ApiServerAddr = settings.ApiServerAddr,
                ApiServerPort = settings.ApiServerPort,
                MqttServerAddr = settings.MqttServerAddr,
                MqttServerPort = settings.MqttServerPort,
                SslSupport = settings.SslSupport,
                Topic = settings.Topic,
                MqttKey = secrets.MqttKey,
                Fota = settings.Fota is null ? null : new DeviceFotaSettings(settings.Fota.Url, settings.Fota.Addr, settings.Fota.Port, settings.Fota.Version),
                WifiProfiles = config.WifiProfiles
                    .Where(profile => secrets.PassBySsid.ContainsKey(profile.Ssid))
                    .Select(profile => new WifiProfile(profile.Ssid, secrets.PassBySsid[profile.Ssid]))
                    .ToList(),
            };
        }
    }
}
