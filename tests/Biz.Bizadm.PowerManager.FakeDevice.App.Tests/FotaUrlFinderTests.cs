using Biz.Bizadm.PowerManager.FakeDevice.App.Fota;
using System.Text;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Tests;

[TestClass]
public sealed class FotaUrlFinderTests
{
    private static IReadOnlyList<string> Find(string payload) => FotaUrlFinder.Find(Encoding.UTF8.GetBytes(payload));

    [TestMethod]
    public void NestedObjectsAndArrays_InOrder()
    {
        IReadOnlyList<string> urls = Find(
            "{\"sid\":\"1\",\"msg\":{\"o\":\"w\",\"e\":[{\"n\":\"/5/0/1\",\"sv\":\"http://fw.example/b540\"},{\"n\":\"x\",\"v\":[\"https://fw.example/b.bin\",1]}]}}");

        CollectionAssert.AreEqual(new[] { "http://fw.example/b540", "https://fw.example/b.bin" }, urls.ToArray());
    }

    [TestMethod]
    public void SchemeIgnoresCase()
        => CollectionAssert.AreEqual(new[] { "HTTP://fw.example/a", "Https://fw.example/b" }, Find("[\"HTTP://fw.example/a\",\"Https://fw.example/b\"]").ToArray());

    [TestMethod]
    [DataRow("{\"u\":\"http://\"}")]
    [DataRow("{\"u\":\"http:// bad host\"}")]
    [DataRow("{\"u\":\"ftp://fw.example/a\"}")]
    [DataRow("{\"u\":\"see http://fw.example/a\"}")]
    [DataRow("{\"n\":12,\"b\":true,\"z\":null}")]
    public void NotAbsoluteHttpUrl_IsNotCandidate(string payload)
        => Assert.AreEqual(0, Find(payload).Count);

    [TestMethod]
    [DataRow("http://fw.example/a")]
    [DataRow("{not json http://fw.example/a}")]
    [DataRow("")]
    public void NotJson_IsEmpty(string payload)
        => Assert.AreEqual(0, Find(payload).Count);

    [TestMethod]
    public void SplitHostAndPort_IsEmpty()
        => Assert.AreEqual(0, Find("{\"msg\":{\"e\":[{\"n\":\"/5/0/1\",\"sv\":\"fw.example\"},{\"n\":\"/5/0/2\",\"sv\":\"8080\"},{\"n\":\"/5/0/3\",\"sv\":\"/b540/\"}]}}").Count);
}
