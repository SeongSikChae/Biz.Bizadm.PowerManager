using Biz.Bizadm.PowerManager.Core.Devices;
using System.Text.RegularExpressions;

namespace Biz.Bizadm.PowerManager.Core.Tests;

[TestClass]
public sealed class MqttKeyGeneratorTests
{
    [TestMethod]
    public void Create_SixteenAlphanumeric_Distinct()
    {
        HashSet<string> keys = [];
        for (int i = 0; i < 1000; i++)
        {
            string key = MqttKeyGenerator.Create();
            Assert.IsTrue(Regex.IsMatch(key, "^[A-Za-z0-9]{16}$"), key);
            keys.Add(key);
        }
        Assert.AreEqual(1000, keys.Count);
    }

    [TestMethod]
    public void Characters_AreAllInVerifyAlphabet()
    {
        Assert.AreEqual(62, MqttKeyGenerator.Characters.Distinct().Count());
        Assert.IsTrue(MqttKeyGenerator.Characters.All(c => DeviceVerify.Alphabet.Contains(c)));
    }
}
