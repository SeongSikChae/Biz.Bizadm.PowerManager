using Biz.Bizadm.PowerManager.DeviceConfig.Messages;
using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.DeviceConfig
{
    public sealed partial class DeviceSession
    {
        /// <summary>
        /// apscan → 등록 Wi‑Fi 대조·선택 → 설정 JSON → connectap.
        /// </summary>
        /// <exception cref="DeviceProtocolException">스캔 결과에 등록 Wi‑Fi가 없거나 설정·connectap 응답이 OK가 아니다.</exception>
        public async Task<DeviceResponse> ConnectApAsync(DeviceSettings settings, IWifiProfileSelector selector, CancellationToken cancellationToken)
        {
            string mac = string.IsNullOrEmpty(settings.Mac) ? Mac : settings.Mac;
            if (string.IsNullOrEmpty(mac))
                throw new InvalidOperationException("Device MAC is unknown. Run the handshake first or set Settings.Mac.");

            DeviceResponse scan = await SendJsonCommandAsync("apscan", timeouts.ApScan, allowDisconnect: false, cancellationToken);
            IReadOnlyList<ScannedAp> scanned = ApScanParser.Parse(scan.Raw);
            LogScanned(scanned);
            IReadOnlyList<WifiCandidate> candidates = WifiProfileMatcher.Match(scanned, settings.WifiProfiles);
            if (candidates.Count == 0)
                throw new DeviceProtocolException(
                    $"No registered Wi-Fi found in apscan result. Scanned: {string.Join(", ", scanned.Select(ap => ap.Ssid))}");

            WifiProfile chosen = await selector.SelectAsync(candidates, cancellationToken);
            logger.LogInformation("Selected Wi-Fi {Ssid}", chosen.Ssid);

            string json = DeviceMessageSerializer.Serialize(DeviceSettingsMessage.From(settings, chosen, mac));
            await channel.SendLineAsync(json, cancellationToken, DeviceMessageSerializer.Mask(json));
            EnsureOk("settings", await channel.ReceiveAsync(DeviceFrame.Json, timeouts.Response, cancellationToken));

            await channel.SendLineAsync(DeviceMessageSerializer.Serialize(new DeviceCommandMessage("connectap", mac)), cancellationToken);
            string connectResponse = await channel.ReceiveAsync(DeviceFrame.Json, timeouts.Response, cancellationToken);
            EnsureOk("connectap", connectResponse);
            return new DeviceResponse(connectResponse);
        }

        /// <summary>
        /// 실장비 응답 <c>{"mac":"&lt;mac&gt;","respone":"OK"}</c>을 확인한다. 키 이름은 펌웨어 오타 그대로다.
        /// </summary>
        private static void EnsureOk(string step, string raw)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(raw);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("respone", out JsonElement value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() == "OK")
                    return;
            }
            catch (JsonException)
            {
            }
            throw new DeviceProtocolException($"Unexpected {step} response: {raw}");
        }

        private void LogScanned(IReadOnlyList<ScannedAp> scanned)
        {
            foreach (ScannedAp ap in scanned)
                logger.LogInformation("Scanned AP {Ssid} {Rssi} dBm", ap.Ssid, ap.Rssi);
        }
    }
}
