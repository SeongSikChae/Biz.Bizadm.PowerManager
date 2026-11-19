using Biz.Bizadm.PowerManager.Core.Devices;

namespace Biz.Bizadm.PowerManager.Core.Tests;

[TestClass]
public sealed class DeviceIdentityTests
{
    [TestMethod]
    [DataRow("aabbccddeeff", "aabbccddeeff")]
    [DataRow("AABBCCDDEEFF", "aabbccddeeff")]
    [DataRow("1A2B3c4d5e6f", "1a2b3c4d5e6f")]
    public void TryNormalizeMac_Valid(string value, string expected)
    {
        Assert.IsTrue(DeviceIdentity.TryNormalizeMac(value, out string? mac));
        Assert.AreEqual(expected, mac);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("aabbccddeef")]
    [DataRow("aabbccddeeff0")]
    [DataRow("zzbbccddeeff")]
    [DataRow("aa:bb:cc:dd:ee")]
    public void TryNormalizeMac_Invalid(string? value)
    {
        Assert.IsFalse(DeviceIdentity.TryNormalizeMac(value, out string? mac));
        Assert.IsNull(mac);
    }

    [TestMethod]
    [DataRow("DAWONDNS-B540_W-aabbccddeeff", "aabbccddeeff")]
    [DataRow("DAWONDNS-M130-W-AABBCCDDEEFF", "aabbccddeeff")]
    public void TryParseDeviceKey_Valid(string key, string expected)
    {
        Assert.IsTrue(DeviceIdentity.TryParseDeviceKey(key, out string? mac));
        Assert.AreEqual(expected, mac);
    }

    [TestMethod]
    [DataRow("DAWONDNS--aabbccddeeff")]
    [DataRow("DAWONDNS-aabbccddeeff")]
    [DataRow("X-B540_W-aabbccddeeff")]
    [DataRow("DAWONDNS-B540_W-aabbccddeef")]
    [DataRow("dawondns-B540_W-aabbccddeeff")]
    [DataRow(null)]
    public void TryParseDeviceKey_Invalid(string? key)
    {
        Assert.IsFalse(DeviceIdentity.TryParseDeviceKey(key, out string? mac));
        Assert.IsNull(mac);
    }

    [TestMethod]
    [DataRow("DAWONDNS-aabbccddeeff", "aabbccddeeff")]
    [DataRow("DAWONDNS-AABBCCDDEEFF", "aabbccddeeff")]
    public void TryParseClientId_Valid(string clientId, string expected)
    {
        Assert.IsTrue(DeviceIdentity.TryParseClientId(clientId, out string? mac));
        Assert.AreEqual(expected, mac);
    }

    [TestMethod]
    [DataRow("DAWONDNS-B540_W-aabbccddeeff")]
    [DataRow("client-1")]
    [DataRow("")]
    [DataRow(null)]
    public void TryParseClientId_Invalid(string? clientId)
    {
        Assert.IsFalse(DeviceIdentity.TryParseClientId(clientId, out string? mac));
        Assert.IsNull(mac);
    }
}
