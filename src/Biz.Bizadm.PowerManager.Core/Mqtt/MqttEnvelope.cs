using System.Text.Json.Serialization;

namespace Biz.Bizadm.PowerManager.Core.Mqtt
{
    /// <summary>
    /// 장치 MQTT 메시지(SenML 유사 JSON). <c>sid</c>는 로그용이며 없어도 해석 실패로 보지 않는다.
    /// </summary>
    public sealed record MqttEnvelope(
        [property: JsonPropertyName("sid")] string? Sid,
        [property: JsonPropertyName("msg")] MqttMessage Msg);

    public sealed record MqttMessage(
        [property: JsonPropertyName("o")] string Operation,
        [property: JsonPropertyName("e")] IReadOnlyList<MqttElement> Elements);

    public sealed record MqttElement(
        [property: JsonPropertyName("n")] string Path,
        [property: JsonPropertyName("sv")] string? StringValue);
}
