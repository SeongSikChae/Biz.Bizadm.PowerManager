namespace Biz.Bizadm.PowerManager.DeviceConfig.Protocol
{
    /// <summary>장치 응답이 예상 형식이 아니거나 OK가 아닐 때.</summary>
    public sealed class DeviceProtocolException(string message) : Exception(message);
}
