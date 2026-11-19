using System.Text.Json.Serialization;

namespace Biz.Bizadm.PowerManager.Core.Devices
{
    /// <summary>
    /// <c>POST /api/v1/devices/register/create</c> 응답. 장치는 키 순서대로 처리하며 <c>verify</c>를 먼저 받아야
    /// <c>mqtt_key</c>를 받아들이므로 선언 순서(직렬화 순서)를 바꾸지 않는다.
    /// </summary>
    public sealed record DeviceRegisterResponse(
        [property: JsonPropertyName("verify")] string Verify,
        [property: JsonPropertyName("mqtt_key")] string MqttKey);

    /// <summary>
    /// <c>GET /api/v1/device/connect/{deviceKey}</c> 응답(과거 구현과 같은 최소 응답).
    /// </summary>
    public sealed record DeviceConnectResponse(
        [property: JsonPropertyName("delay")] string Delay);
}
