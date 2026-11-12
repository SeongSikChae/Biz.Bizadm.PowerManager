using Biz.Bizadm.PowerManager.Core.Devices;
using Biz.Bizadm.PowerManager.Core.Mqtt;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Core.Tests;

[TestClass]
public sealed class SerializationTests
{
    // ASP.NET Core 웹 기본값(camelCase, 대소문자 무시)으로도 snake_case 이름이 매핑되는지 확인한다.
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void MqttEnvelope_RegisterMessage()
    {
        const string json = """
            {"sid":"0","msg":{"o":"rg","e":[
             {"n":"/3/0/0","sv":"DAWONDNS"},{"n":"/3/0/1","sv":"B540_W"},{"n":"/3/0/2","sv":"aabbccddeeff"},
             {"n":"/3/0/3","sv":"1.01.34"},
             {"n":"/3/0/997","sv":"1"},{"n":"/3/0/998","sv":"1.0"},{"n":"/3/0/999","sv":"1"}]}}
            """;

        MqttEnvelope envelope = JsonSerializer.Deserialize<MqttEnvelope>(json)!;

        Assert.AreEqual("0", envelope.Sid);
        Assert.AreEqual(MqttOperation.Register, envelope.Msg.Operation);
        Assert.HasCount(7, envelope.Msg.Elements);
        Assert.AreEqual("1.01.34", envelope.Msg.Elements.Single(e => e.Path == "/3/0/3").StringValue);
    }

    [TestMethod]
    public void DeviceRegisterRequest_Format1()
    {
        const string json = """
            {"account":{"user_id":"user@example.com/kakao"},"device":{"mac":"aabbccddeeff","model_id":"B540_W","company":"DAWONDNS","lati":"37.5","long":"127.0","verify":"true"}}
            """;

        DeviceRegisterRequest request = JsonSerializer.Deserialize<DeviceRegisterRequest>(json, WebOptions)!;

        Assert.AreEqual("user@example.com/kakao", request.Account?.UserId);
        Assert.AreEqual("B540_W", request.Device?.ModelId);
        Assert.AreEqual("127.0", request.Device?.Longitude);
        Assert.AreEqual("true", request.Device?.Verify);
        Assert.IsNull(request.Device?.Status);
    }

    [TestMethod]
    public void DeviceRegisterRequest_Format2WithStatus()
    {
        const string json = """
            {"account":{"user_id":"user@example.com/kakao"},"device":{"mac":"aabbccddeeff","model_id":"B540_W","company":"DAWONDNS","lati":"37.5","long":"127.0","status":"1","verify":"true"}}
            """;

        DeviceRegisterRequest request = JsonSerializer.Deserialize<DeviceRegisterRequest>(json, WebOptions)!;

        Assert.AreEqual("B540_W", request.Device?.ModelId);
        Assert.AreEqual("user@example.com/kakao", request.Account?.UserId);
        Assert.AreEqual("127.0", request.Device?.Longitude);
        Assert.AreEqual("1", request.Device?.Status);
        Assert.AreEqual("true", request.Device?.Verify);
    }
}
