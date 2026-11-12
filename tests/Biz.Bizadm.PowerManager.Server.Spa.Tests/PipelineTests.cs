using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Net.Http.Headers;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

[TestClass]
public sealed class PipelineTests
{
    private static string tempRoot = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.Server.Spa.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "ClientApp"));
        File.WriteAllText(Path.Combine(tempRoot, "ClientApp", "index.html"), "<html>index</html>");
        File.WriteAllText(Path.Combine(tempRoot, "ClientApp", "app.js"), "console.log('app');");
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    private static Task<WebApplication> StartAsync(Action<WebApplication>? configureApp = null)
        => TestApp.StartAsync(TestApp.Configuration(tempRoot), Path.Combine(tempRoot, "log"), configureApp: configureApp);

    private static async Task<HttpResponseMessage> GetAsync(WebApplication app, string path, string accept)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return await app.GetTestClient().SendAsync(request);
    }

    [TestMethod]
    public async Task StaticFile_Returns200()
    {
        await using WebApplication app = await StartAsync();
        HttpResponseMessage response = await GetAsync(app, "/app.js", "*/*");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("console.log('app');", await response.Content.ReadAsStringAsync());
        await app.StopAsync();
    }

    [TestMethod]
    public async Task HtmlFallback_ReturnsIndex()
    {
        await using WebApplication app = await StartAsync();
        HttpResponseMessage response = await GetAsync(app, "/some/client/route", "text/html");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("<html>index</html>", await response.Content.ReadAsStringAsync());
        await app.StopAsync();
    }

    [TestMethod]
    public async Task NonHtmlMissing_Returns404ProblemDetails()
    {
        await using WebApplication app = await StartAsync();
        HttpResponseMessage response = await GetAsync(app, "/missing", "application/json");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        await app.StopAsync();
    }

    [TestMethod]
    public async Task Health_Returns200()
    {
        await using WebApplication app = await StartAsync();
        HttpResponseMessage response = await app.GetTestClient().GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        await app.StopAsync();
    }

    [TestMethod]
    public async Task UnhandledException_Returns500ProblemDetailsWithoutStackTrace()
    {
        await using WebApplication app = await StartAsync(a =>
            a.MapGet("/test-throw", (Func<string>)(() => throw new InvalidOperationException("test-secret-detail"))));

        HttpResponseMessage response = await app.GetTestClient().GetAsync("/test-throw");
        string body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("test-secret-detail", body);
        Assert.DoesNotContain("   at ", body);
        await app.StopAsync();
    }
}
