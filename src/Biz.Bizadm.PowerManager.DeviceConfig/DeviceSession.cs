using Biz.Bizadm.PowerManager.DeviceConfig.Messages;
using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Biz.Bizadm.PowerManager.DeviceConfig
{
    /// <summary>
    /// 장치 SoftAP TCP 채널 위의 핸드셰이크와 명령 송수신.
    /// </summary>
    public sealed partial class DeviceSession(IDeviceChannel channel, DeviceTimeouts timeouts, ILogger logger)
    {
        /// <summary>
        /// 핸드셰이크 START_OK 응답의 MAC. 핸드셰이크 전에는 빈 문자열이다.
        /// </summary>
        public string Mac { get; private set; } = string.Empty;

        /// <summary>
        /// <c>[DUT&lt;-PC] START</c>, <c>hello tcp SUCCESS_CONNECT</c>를 보내고 <c>[DUT-&gt;PC] START_OK:&lt;mac&gt;#</c>를 받는다.
        /// </summary>
        /// <returns>장치 MAC.</returns>
        public async Task<string> HandshakeAsync(CancellationToken cancellationToken)
        {
            await channel.SendLineAsync("[DUT<-PC] START", cancellationToken);
            await channel.SendLineAsync("hello tcp SUCCESS_CONNECT", cancellationToken);
            string response = await channel.ReceiveAsync(DeviceFrame.Test, timeouts.Response, cancellationToken);
            Match match = StartOkRegex().Match(response);
            if (!match.Success)
                throw new DeviceProtocolException($"Unexpected handshake response: {response}");
            Mac = match.Groups["Mac"].Value;
            return Mac;
        }

        /// <summary>
        /// ConnectAp 외의 명령을 실행한다. ConnectAp는 <see cref="ConnectApAsync"/>를 쓴다.
        /// </summary>
        public Task<DeviceResponse> ExecuteAsync(DeviceCommand command, CancellationToken cancellationToken) => command switch
        {
            DeviceCommand.ApScan => ScanApsAsync(DeviceMessageSerializer.Serialize(new DeviceCommandMessage("apscan")), cancellationToken),
            DeviceCommand.Reboot => SendJsonCommandAsync("reboot", timeouts.Response, allowDisconnect: true, cancellationToken),
            DeviceCommand.FactoryReset => SendJsonCommandAsync("factoryreset", timeouts.Response, allowDisconnect: true, cancellationToken),
            // 실장비 확인: 공장검사 SCAN_AP도 '#' 프레임이 아니라 apscan과 같은 JSON으로 응답한다
            DeviceCommand.ScanAp => ScanApsAsync("[DUT<-PC] SCAN_AP", cancellationToken),
            DeviceCommand.OnCmd => SendTestCommandAsync("ON_CMD", allowDisconnect: false, cancellationToken),
            DeviceCommand.OffCmd => SendTestCommandAsync("OFF_CMD", allowDisconnect: false, cancellationToken),
            DeviceCommand.Measurement => SendTestCommandAsync("MEASUREMENT", allowDisconnect: false, cancellationToken),
            DeviceCommand.OverLoad => SendTestCommandAsync("OVERLOAD", allowDisconnect: false, cancellationToken),
            DeviceCommand.TestFactoryReset => SendTestCommandAsync("FACTORY_RESET", allowDisconnect: true, cancellationToken),
            DeviceCommand.ConnectAp => throw new InvalidOperationException($"Use {nameof(ConnectApAsync)} for {nameof(DeviceCommand.ConnectAp)}"),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };

        /// <summary>
        /// apscan·공장검사 SCAN_AP: JSON <c>{"SCAN_AP_LIST":{...}}</c> 응답의 SSID·RSSI를 로그로 남긴다. 형식이 달라도 원문은 돌려준다.
        /// </summary>
        private async Task<DeviceResponse> ScanApsAsync(string line, CancellationToken cancellationToken)
        {
            DeviceResponse response = await SendAndReceiveAsync(line, DeviceFrame.Json, timeouts.ApScan, allowDisconnect: false, cancellationToken);
            try
            {
                LogScanned(ApScanParser.Parse(response.Raw));
            }
            catch (DeviceProtocolException ex)
            {
                logger.LogWarning("{Message}", ex.Message);
            }
            return response;
        }

        private Task<DeviceResponse> SendJsonCommandAsync(string command, TimeSpan timeout, bool allowDisconnect, CancellationToken cancellationToken) =>
            SendAndReceiveAsync(DeviceMessageSerializer.Serialize(new DeviceCommandMessage(command)), DeviceFrame.Json, timeout, allowDisconnect, cancellationToken);

        private Task<DeviceResponse> SendTestCommandAsync(string command, bool allowDisconnect, CancellationToken cancellationToken) =>
            SendAndReceiveAsync($"[DUT<-PC] {command}", DeviceFrame.Test, timeouts.Response, allowDisconnect, cancellationToken);

        private async Task<DeviceResponse> SendAndReceiveAsync(string line, DeviceFrame frame, TimeSpan timeout, bool allowDisconnect, CancellationToken cancellationToken)
        {
            await channel.SendLineAsync(line, cancellationToken);
            try
            {
                return new DeviceResponse(await channel.ReceiveAsync(frame, timeout, cancellationToken));
            }
            catch (DeviceDisconnectedException ex) when (allowDisconnect)
            {
                logger.LogWarning("Device closed the connection after {Command} without a complete response. Received: {Received}", line, ex.Received);
                return new DeviceResponse(ex.Received, Disconnected: true);
            }
        }

        [GeneratedRegex(@"\[DUT->PC\] START_OK:(?<Mac>[^#\s]+)#")]
        private static partial Regex StartOkRegex();
    }
}
