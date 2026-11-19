using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Protocol;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Transport
{
    /// <summary>장치 명령 실행. 명령마다 연결 → 핸드셰이크 → 명령 → 연결 종료.</summary>
    public sealed partial class DeviceClient(string host, int port, TimeSpan timeout, ILogger logger)
    {
        /// <summary>ConnectAp를 제외한 명령. 응답이 예상 형식이 아니면 DeviceProtocolException.</summary>
        public async Task<string> ExecuteAsync(DeviceCommand command, CancellationToken cancellationToken)
        {
            await using DeviceConnection connection = await DeviceConnection.OpenAsync(host, port, timeout, logger, cancellationToken);
            await connection.HandshakeAsync(cancellationToken);
            return command switch
            {
                DeviceCommand.ApScan => EnsureScanList(await connection.RequestAsync(DeviceMessages.Command("apscan"), cancellationToken)),
                DeviceCommand.FactoryReset => DeviceResponseParser.EnsureOk(await connection.RequestAsync(DeviceMessages.Command("factoryreset"), cancellationToken)),
                DeviceCommand.Reboot => await RequestOrWarnAsync(connection, command, DeviceMessages.Command("reboot"), cancellationToken),
                DeviceCommand.ScanAp => EnsureScanList(await connection.RequestAsync(DeviceMessages.Test("SCAN_AP"), cancellationToken)),
                DeviceCommand.OnCmd => Expect(await connection.RequestAsync(DeviceMessages.Test("ON_CMD"), cancellationToken), OnCmdOkRegex()),
                DeviceCommand.OffCmd => Expect(await connection.RequestAsync(DeviceMessages.Test("OFF_CMD"), cancellationToken), OffCmdOkRegex()),
                DeviceCommand.Measurement => Expect(await connection.RequestAsync(DeviceMessages.Test("MEASUREMENT"), cancellationToken), MeasurementRegex()),
                DeviceCommand.OverLoad => Expect(await connection.RequestAsync(DeviceMessages.Test("OVERLOAD"), cancellationToken), TestFrameRegex()),
                DeviceCommand.TestFactoryReset => await RequestOrWarnAsync(connection, command, DeviceMessages.Test("FACTORY_RESET"), cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Use ConnectApAsync for ConnectAp"),
            };
        }

        /// <summary>
        /// 한 연결에서 핸드셰이크 → apscan → Wi-Fi 선택 → 설정 JSON → 흐름(settings.Protocol)별 마무리(#13).
        /// V1: 설정 송신 후 정상 종료 / V2: 설정 → 응답 1개(검사 없음) → {"delay":"0"} → 정상 종료 /
        /// V3: 설정 → OK → connectap → OK / V4: 핸드셰이크에서 START_OK 앞 프레임 최대 2개 건너뜀, 설정 → connectap → 정상 종료.
        /// </summary>
        /// <exception cref="InvalidOperationException">V1인데 MqttKey가 비었다(장치에 접속하기 전에 던진다).</exception>
        public async Task ConnectApAsync(
            Func<IReadOnlyList<WifiNetwork>, CancellationToken, Task<(string Ssid, string Pass)>> selectWifi,
            DeviceSettings settings,
            CancellationToken cancellationToken)
        {
            if (settings.Protocol == DeviceProtocol.V1 && string.IsNullOrEmpty(settings.MqttKey))
                throw new InvalidOperationException("MqttKey is required for Protocol V1");

            await using DeviceConnection connection = await DeviceConnection.OpenAsync(host, port, timeout, logger, cancellationToken);
            string mac = settings.Protocol == DeviceProtocol.V4
                ? await connection.HandshakeAsync(V4MaxSkippedFrames, cancellationToken)
                : await connection.HandshakeAsync(cancellationToken);
            IReadOnlyList<WifiNetwork> scanned = DeviceResponseParser.ParseScanApList(
                await connection.RequestAsync(DeviceMessages.Command("apscan"), cancellationToken));
            (string ssid, string pass) = await selectWifi(scanned, cancellationToken);
            logger.LogInformation("TX {Message} (protocol {Protocol})", settings.ToJson(mac, ssid, pass, masked: true).TrimEnd('\n'), settings.Protocol);
            string json = settings.ToJson(mac, ssid, pass);

            switch (settings.Protocol)
            {
                case DeviceProtocol.V1:
                    await connection.SendAsync(json, logSend: false, cancellationToken);
                    await connection.CloseGracefullyAsync(cancellationToken);
                    break;
                case DeviceProtocol.V2:
                    await connection.RequestAsync(json, cancellationToken, logSend: false); // 응답 내용은 검사하지 않는다(참고 구현과 같음)
                    await connection.SendAsync(DeviceMessages.Delay, logSend: true, cancellationToken);
                    await connection.CloseGracefullyAsync(cancellationToken);
                    break;
                case DeviceProtocol.V4:
                    await connection.SendAsync(json, logSend: false, cancellationToken);
                    await connection.SendAsync(DeviceMessages.ConnectAp(mac), logSend: true, cancellationToken);
                    await connection.CloseGracefullyAsync(cancellationToken);
                    break;
                default:
                    DeviceResponseParser.EnsureOk(await connection.RequestAsync(json, cancellationToken, logSend: false));
                    DeviceResponseParser.EnsureOk(await connection.RequestAsync(DeviceMessages.ConnectAp(mac), cancellationToken));
                    break;
            }
        }

        /// <summary>참고 구현 ConfigratorV4: hello 뒤 프레임 1개를 버리고 BUTTON_이면 1개 더 버린다.</summary>
        private const int V4MaxSkippedFrames = 2;

        /// <summary>
        /// 장치가 응답·연결 종료 없이 재부팅·초기화하는 명령(Reboot, TestFactoryReset).
        /// 응답이 오면 그대로, 대기 시간 초과나 연결 종료면 경고를 남기고 성공으로 본다(U8 실장비: 두 명령 모두 응답 없이 타임아웃).
        /// </summary>
        private async Task<string> RequestOrWarnAsync(DeviceConnection connection, DeviceCommand command, string message, CancellationToken cancellationToken)
        {
            try
            {
                return await connection.RequestAsync(message, cancellationToken);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                logger.LogWarning("{Command}: no response ({Reason}); treated as sent", command, ex.Message);
                return "(no response)";
            }
        }

        private static string EnsureScanList(string frame)
        {
            DeviceResponseParser.ParseScanApList(frame);
            return frame;
        }

        private static string Expect(string frame, Regex pattern) =>
            pattern.IsMatch(frame) ? frame : throw new DeviceProtocolException($"Unexpected response: {frame}");

        [GeneratedRegex(@"^\[DUT->PC\] ON_CMD_OK#$")]
        private static partial Regex OnCmdOkRegex();

        [GeneratedRegex(@"^\[DUT->PC\] OFF_CMD_OK#$")]
        private static partial Regex OffCmdOkRegex();

        [GeneratedRegex(@"^\[DUT->PC\] MEASUREMENT_W:\d+(\.\d+)?#$")]
        private static partial Regex MeasurementRegex();

        [GeneratedRegex("#$")]
        private static partial Regex TestFrameRegex();
    }
}
