using Biz.Bizadm.PowerManager.Core.Mqtt;

namespace Biz.Bizadm.PowerManager.Core.Tests;

[TestClass]
public sealed class DeviceTopicTests
{
    private const string Key = "DAWONDNS-B540_W-aabbccddeeff";

    [TestMethod]
    [DataRow("dwd.v1/" + Key + "/iot-server/json", DeviceTopicKind.Uplink)]
    [DataRow("dwd.v1/" + Key + "/iot-server//json", DeviceTopicKind.Uplink)]
    [DataRow("dwd.v1/" + Key + "/iot-server/timesync", DeviceTopicKind.Uplink)]
    [DataRow("dwd.v1/" + Key + "/iot-server/lwt", DeviceTopicKind.Lwt)]
    [DataRow("dwd.v1/" + Key + "/iot-server//lwt", DeviceTopicKind.Lwt)]
    public void TryParse_DeviceTopic(string topic, DeviceTopicKind kind)
    {
        Assert.IsTrue(DeviceTopic.TryParse(topic, out DeviceTopic? result));
        Assert.AreEqual(new DeviceTopic("dwd", Key, kind), result);
    }

    [TestMethod]
    [DataRow("dwd.v1/iot-server/" + Key)]
    [DataRow("dwd.v1/" + Key + "/iot-server")]
    [DataRow(".v1/" + Key + "/iot-server/json")]
    [DataRow("dwd/" + Key + "/iot-server/json")]
    public void TryParse_NotDeviceTopic_ReturnsFalse(string topic)
    {
        Assert.IsFalse(DeviceTopic.TryParse(topic, out DeviceTopic? result));
        Assert.IsNull(result);
    }

    [TestMethod]
    [DataRow("dwd.v1/iot-server/" + Key)]
    [DataRow("dwd.v1/iot-server/" + Key + "/write/json")]
    [DataRow("/dwd.v1//iot-server/" + Key)]
    public void IsToDevice_ServerToDeviceTopic_ReturnsTrue(string topic)
        => Assert.IsTrue(DeviceTopic.IsToDevice(topic));

    [TestMethod]
    [DataRow(".v1/iot-server/" + Key)]
    [DataRow("dwd.v1/iot-server")]
    [DataRow("dwd.v1/" + Key + "/iot-server/json")]
    [DataRow("/lwt")]
    [DataRow("dwd/iot-server/" + Key)]
    public void IsToDevice_OtherTopic_ReturnsFalse(string topic)
        => Assert.IsFalse(DeviceTopic.IsToDevice(topic));

    [TestMethod]
    public void ToDevice_BuildsServerToDeviceTopic()
        => Assert.AreEqual("dwd.v1/iot-server/" + Key, DeviceTopic.ToDevice("dwd", Key));
}
