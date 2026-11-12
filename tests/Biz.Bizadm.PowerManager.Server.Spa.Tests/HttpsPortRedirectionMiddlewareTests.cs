using Microsoft.AspNetCore.Http;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

[TestClass]
public sealed class HttpsPortRedirectionMiddlewareTests
{
    private static readonly IReadOnlyDictionary<int, int> PortMap = new Dictionary<int, int> { [80] = 443, [18446] = 18443 };

    private static async Task<(bool nextCalled, HttpContext context)> InvokeAsync(
        int localPort, string method, string path, string? host = "device.local", bool isHttps = false, string query = "")
    {
        bool nextCalled = false;
        HttpsPortRedirectionMiddleware middleware = new HttpsPortRedirectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, PortMap);

        DefaultHttpContext context = new DefaultHttpContext();
        context.Connection.LocalPort = localPort;
        context.Request.Method = method;
        context.Request.Scheme = isHttps ? "https" : "http";
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        if (host is not null)
            context.Request.Host = new HostString(host, localPort);

        await middleware.InvokeAsync(context);
        return (nextCalled, context);
    }

    [TestMethod]
    public async Task DevicePort_Post_Redirects307ToDeviceHttpsPort()
    {
        (bool nextCalled, HttpContext context) = await InvokeAsync(18446, "POST", "/api/v1/devices/register/create", query: "?a=1");

        Assert.IsFalse(nextCalled);
        Assert.AreEqual(StatusCodes.Status307TemporaryRedirect, context.Response.StatusCode);
        Assert.AreEqual("https://device.local:18443/api/v1/devices/register/create?a=1", context.Response.Headers.Location.ToString());
    }

    [TestMethod]
    public async Task HttpPort_Get_Redirects307ToHttpsPort()
    {
        (bool nextCalled, HttpContext context) = await InvokeAsync(80, "GET", "/");

        Assert.IsFalse(nextCalled);
        Assert.AreEqual(StatusCodes.Status307TemporaryRedirect, context.Response.StatusCode);
        Assert.AreEqual("https://device.local:443/", context.Response.Headers.Location.ToString());
    }

    [TestMethod]
    [DataRow(80, "/fota/b540/version", false, "device.local")]
    [DataRow(80, "/fota", false, "device.local")]
    [DataRow(18446, "/FOTA/b540/user2.bin", false, "device.local")]
    [DataRow(18446, "/api/v1/device/connect/x", true, "device.local")]
    [DataRow(5000, "/api/v1/device/connect/x", false, "device.local")]
    [DataRow(18446, "/api/v1/device/connect/x", false, null)]
    public async Task PassesThrough(int localPort, string path, bool isHttps, string? host)
    {
        (bool nextCalled, HttpContext context) = await InvokeAsync(localPort, "GET", path, host, isHttps);

        Assert.IsTrue(nextCalled);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task FotaPrefixWithoutSegment_Redirects()
    {
        (bool nextCalled, HttpContext context) = await InvokeAsync(80, "GET", "/fotax");

        Assert.IsFalse(nextCalled);
        Assert.AreEqual(StatusCodes.Status307TemporaryRedirect, context.Response.StatusCode);
    }
}
