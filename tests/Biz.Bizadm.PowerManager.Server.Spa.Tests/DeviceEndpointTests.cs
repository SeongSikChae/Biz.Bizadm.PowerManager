using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Text;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

[TestClass]
public sealed class DeviceEndpointTests
{
    private static string tempRoot = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.Server.Spa.Tests." + Guid.NewGuid().ToString("N"));
        // fallback이 있는 운영 모드 상태로 띄운다(HEAD가 fallback으로 빠지지 않는지 확인).
        Directory.CreateDirectory(Path.Combine(tempRoot, "ClientApp"));
        File.WriteAllText(Path.Combine(tempRoot, "ClientApp", "index.html"), "<html>index</html>");
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    private static Task<WebApplication> StartAsync()
        => TestApp.StartAsync(TestApp.Configuration(tempRoot), Path.Combine(tempRoot, "log"));

    private static void AssertNotImplemented(HttpResponseMessage response, bool hasBody = true)
    {
        Assert.AreEqual(HttpStatusCode.NotImplemented, response.StatusCode);
        if (hasBody)
            Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static StringContent Json(string json) => new StringContent(json, Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task Register_DocumentBody_Returns501()
    {
        await using WebApplication app = await StartAsync();
        HttpResponseMessage response = await app.GetTestClient().PostAsync("/api/v1/devices/register/create", Json(
            """{"account":{"user_id":"user@example.com/kakao"},"device":{"mac":"aabbccddeeff","model_id":"B540_W","company":"DAWONDNS","lati":"37.5","long":"127.0","verify":"true"}}"""));
        AssertNotImplemented(response);
        await app.StopAsync();
    }

    [TestMethod]
    public async Task Register_PartialBody_Returns501()
    {
        await using WebApplication app = await StartAsync();
        HttpResponseMessage response = await app.GetTestClient().PostAsync("/api/v1/devices/register/create", Json("""{"device":{"mac":"aabbccddeeff"}}"""));
        AssertNotImplemented(response);
        await app.StopAsync();
    }

    [TestMethod]
    public async Task Connect_Returns501()
    {
        await using WebApplication app = await StartAsync();
        HttpResponseMessage response = await app.GetTestClient().GetAsync("/api/v1/device/connect/DAWONDNS-B540_W-aabbccddeeff");
        AssertNotImplemented(response);
        await app.StopAsync();
    }

    [TestMethod]
    [DataRow("/fota/b540/version")]
    [DataRow("/fota/b540/user2.bin")]
    [DataRow("/fota/")]
    public async Task Fota_Get_Returns501(string path)
    {
        await using WebApplication app = await StartAsync();
        HttpResponseMessage response = await app.GetTestClient().GetAsync(path);
        AssertNotImplemented(response);
        await app.StopAsync();
    }

    [TestMethod]
    [DataRow("/fota/b540/version")]
    [DataRow("/fota/b540/user2.bin")]
    public async Task Fota_Head_Returns501(string path)
    {
        await using WebApplication app = await StartAsync();
        HttpResponseMessage response = await app.GetTestClient().SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
        AssertNotImplemented(response, hasBody: false);
        await app.StopAsync();
    }
}
