using Biz.Bizadm.PowerManager.FakeDevice.App.Mqtt;
using System.Text;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Tests;

[TestClass]
public sealed class DevicePayloadsTests
{
    [TestMethod]
    public void Register_IsExactSpecFormat()
    {
        string payload = Encoding.UTF8.GetString(DevicePayloads.Register("DAWONDNS", "B540_W", "a1b2c3d4e5f6", "1.01.36"));

        Assert.AreEqual(
            "{\"sid\":\"0\",\"msg\":{\"o\":\"rg\",\"e\":[" +
            "{\"n\":\"/3/0/0\",\"sv\":\"DAWONDNS\"},{\"n\":\"/3/0/1\",\"sv\":\"B540_W\"},{\"n\":\"/3/0/2\",\"sv\":\"a1b2c3d4e5f6\"}," +
            "{\"n\":\"/3/0/3\",\"sv\":\"1.01.36\"},{\"n\":\"/3/0/997\",\"sv\":\"1\"},{\"n\":\"/3/0/998\",\"sv\":\"1.0\"},{\"n\":\"/3/0/999\",\"sv\":\"1\"}]}}",
            payload);
    }

    [TestMethod]
    public void TimeSync_IsExactSpecFormat()
        => Assert.AreEqual("{\"sid\":\"0\",\"msg\":{\"o\":\"tisyn\",\"e\":[]}}", Encoding.UTF8.GetString(DevicePayloads.TimeSync()));

    [TestMethod]
    public void Notify_IsExactSpecFormat()
    {
        string payload = Encoding.UTF8.GetString(DevicePayloads.Notify([new NotifyElement("/100/0/31", "false"), new NotifyElement("/100/0/32", "+1")]));

        Assert.AreEqual(
            "{\"sid\":\"1\",\"msg\":{\"o\":\"n\",\"e\":[{\"n\":\"/100/0/31\",\"sv\":\"false\",\"ti\":\"\"},{\"n\":\"/100/0/32\",\"sv\":\"+1\",\"ti\":\"\"}]}}",
            payload);
    }
}
