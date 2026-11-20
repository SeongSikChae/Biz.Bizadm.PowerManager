using Biz.Bizadm.PowerManager.FakeDevice.App.Fota;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Tests;

[TestClass]
public sealed class FotaDownloaderTests
{
    [TestMethod]
    // 헤더를 형식화하지 않는 NonValidated 보기로 넣은 순서와 원래 값을 본다(형식화하면 "; q=" 처럼 바뀐다)
    public void Request_HasFixedHeadersInFirmwareOrder()
    {
        using HttpRequestMessage request = FotaDownloader.CreateRequest(new Uri("http://fw.example/b540"), "Agent/1.0");

        CollectionAssert.AreEqual(
            new[] { "Connection", "Cache-Control", "User-Agent", "Accept", "Accept-Encoding", "Accept-Language" },
            request.Headers.NonValidated.Select(h => h.Key).ToArray());
        Assert.AreEqual("keep-alive", string.Join(",", request.Headers.NonValidated["Connection"]));
        Assert.AreEqual("no-cache", string.Join(",", request.Headers.NonValidated["Cache-Control"]));
        Assert.AreEqual("Agent/1.0", string.Join(" ", request.Headers.NonValidated["User-Agent"]));
        Assert.AreEqual("*/*", string.Join(",", request.Headers.NonValidated["Accept"]));
        Assert.AreEqual("gzip,deflate", string.Join(",", request.Headers.NonValidated["Accept-Encoding"]));
        Assert.AreEqual("zh-CN,eb-US;q=0.8", string.Join(",", request.Headers.NonValidated["Accept-Language"]));
        Assert.AreEqual(HttpMethod.Get, request.Method);
    }

    [TestMethod]
    [DataRow("http://fw.example/b540", "http://fw.example/b540/version")]
    [DataRow("http://fw.example/b540/", "http://fw.example/b540/version")]
    [DataRow("http://fw.example/b540//", "http://fw.example/b540/version")]
    [DataRow("https://fw.example:8443/", "https://fw.example:8443/version")]
    public void VersionUrl_NormalizesTrailingSlash(string url, string expected)
        => Assert.AreEqual(expected, FotaDownloader.VersionUrl(url));
}
