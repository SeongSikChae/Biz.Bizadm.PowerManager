using Microsoft.AspNetCore.Builder;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Biz.Bizadm.PowerManager.Server.Core.Tests;

[TestClass]
public sealed class KestrelTlsTests
{
    private const string Password = "test-password";
    private static string tempRoot = null!;
    private static string thumbprint = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.Server.Core.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        thumbprint = TestCertificates.WritePfx(Path.Combine(tempRoot, "server.pfx"), Password);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    private static TlsConfiguration Tls() => new() { UseTls = true, ServerCertificate = "server.pfx", ServerCertificatePassword = Password };

    private static async Task<WebApplication> StartAsync(ushort[] httpPorts, ushort[] httpsPorts)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = tempRoot });
        builder.WebHost.ConfigureServerKestrel(httpPorts, httpsPorts, Tls(), tempRoot);
        WebApplication app = builder.Build();
        app.MapGet("/", () => "ok");
        await app.StartAsync();
        return app;
    }

    private static async Task<string> HandshakeAsync(ushort port, SslProtocols protocol)
    {
        using TcpClient client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using SslStream ssl = new SslStream(client.GetStream(), false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost", EnabledSslProtocols = protocol });
        Assert.AreEqual(protocol, ssl.SslProtocol);
        return ssl.RemoteCertificate!.GetCertHashString();
    }

    [TestMethod]
    [DataRow(SslProtocols.Tls12)]
    [DataRow(SslProtocols.Tls13)]
    public async Task HttpsPort_NegotiatesTlsWithServerCertificate(SslProtocols protocol)
    {
        ushort port = FreePort.TcpAndUdp();
        await using WebApplication app = await StartAsync([], [port]);

        Assert.AreEqual(thumbprint, await HandshakeAsync(port, protocol));

        await app.StopAsync();
    }

    [TestMethod]
    public async Task HttpPort_StaysPlainHttpWhenTlsIsEnabled()
    {
        ushort httpPort = FreePort.Tcp();
        ushort httpsPort = FreePort.TcpAndUdp();
        await using WebApplication app = await StartAsync([httpPort], [httpsPort]);

        using HttpClient client = new HttpClient();
        HttpResponseMessage response = await client.GetAsync($"http://localhost:{httpPort}/");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("ok", await response.Content.ReadAsStringAsync());
        await app.StopAsync();
    }

    [TestMethod]
    public void TlsCertificateLoader_ResolvesAbsolutePathAndLoads()
    {
        string path = TlsCertificateLoader.ResolvePath(Tls(), tempRoot);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(tempRoot, "server.pfx")), path);

        using X509Certificate2 certificate = TlsCertificateLoader.Load(Tls(), tempRoot);
        Assert.AreEqual(thumbprint, certificate.Thumbprint);
        Assert.IsTrue(certificate.HasPrivateKey);
    }
}
