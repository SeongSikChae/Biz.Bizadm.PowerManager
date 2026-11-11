using Biz.Bizadm.PowerManager.DeviceConfig.Transport;

namespace Biz.Bizadm.PowerManager.DeviceConfig
{
    /// <summary>성공한 명령 하나의 결과. ConnectAp는 "OK (&lt;공유기 SSID&gt;)".</summary>
    public sealed record DeviceCommandResult(DeviceCommand Command, string Response);
}
