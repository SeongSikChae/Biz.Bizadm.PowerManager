using Biz.Bizadm.PowerManager.Server.Core;
using Biz.Bizadm.PowerManager.Server.Spa.Mqtt;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using MQTTnet.Server;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

/// <summary>
/// MQTTS 엔드포인트의 TLS 버전과 암호 목록(mqtts-tls-policy C1~C4).
/// 암호 목록 제한은 Windows가 아닌 OS에서만 걸리므로, 실제 제한과 TLS 1.0·1.1 거절은 리눅스 서버의 openssl로 확인한다(C6).
/// </summary>
[TestClass]
public sealed class MqttTlsPolicyTests
{
    private const string PfxPassword = "test-password";
    private const string WindowsWarning = "IncludeCipherSuites is not supported on Windows and is ignored; OS default cipher suites are used.";
    private const string DeviceSuites = "TLS_RSA_WITH_AES_256_CBC_SHA256,TLS_RSA_WITH_AES_128_CBC_SHA256";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private string tempRoot = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.MqttTlsPolicy.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "ClientApp"));
    }

    [TestCleanup]
    public void TestCleanup() => TestApp.DeleteTempRoot(tempRoot);

    private X509Certificate2 LoadCertificate()
    {
        string pfx = Path.Combine(tempRoot, "server.pfx");
        TestApp.WritePfx(pfx, PfxPassword);
        return X509CertificateLoader.LoadPkcs12FromFile(pfx, PfxPassword);
    }

    private AppConfiguration ConfigurationWith(string? includeCipherSuites)
    {
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        configuration.Tls = new TlsConfiguration
        {
            UseTls = true,
            ServerCertificate = "server.pfx",
            ServerCertificatePassword = PfxPassword,
            IncludeCipherSuites = includeCipherSuites,
        };
        return configuration;
    }

    // C1: TLS 엔드포인트는 TLS 1.2와 1.3을 받는다(TLS 1.0·1.1은 열거값에 없음).
    [TestMethod]
    public void BuildServerOptions_TlsEndpoint_AllowsTls12AndTls13()
    {
        using X509Certificate2 certificate = LoadCertificate();
        ListLogger logger = new ListLogger();

        MqttServerOptions options = MqttBrokerHostedService.BuildServerOptions(ConfigurationWith(null), certificate, logger);

        Assert.IsTrue(options.TlsEndpointOptions.IsEnabled);
        Assert.AreEqual(SslProtocols.Tls12 | SslProtocols.Tls13, options.TlsEndpointOptions.SslProtocol);
    }

    // C3: 목록이 없거나 비었거나 공백뿐이면 OS와 상관없이 제한하지 않고 경고도 없다.
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void BuildServerOptions_BlankCipherSuites_NoPolicyNoWarning(string? value)
    {
        using X509Certificate2 certificate = LoadCertificate();
        ListLogger logger = new ListLogger();

        MqttServerOptions options = MqttBrokerHostedService.BuildServerOptions(ConfigurationWith(value), certificate, logger);

        Assert.IsNull(options.TlsEndpointOptions.CipherSuitesPolicy);
        Assert.IsEmpty(logger.Warnings);
    }

    // C2·C4: Windows면 목록을 무시하고 HTTPS와 같은 문구로 경고하며 엔드포인트는 연다. Windows가 아니면 정책을 넣는다.
    [TestMethod]
    public void BuildServerOptions_CipherSuites_OsBranch()
    {
        using X509Certificate2 certificate = LoadCertificate();
        ListLogger logger = new ListLogger();

        MqttServerOptions options = MqttBrokerHostedService.BuildServerOptions(ConfigurationWith(DeviceSuites), certificate, logger);

        Assert.IsTrue(options.TlsEndpointOptions.IsEnabled);
        if (OperatingSystem.IsWindows())
        {
            Assert.IsNull(options.TlsEndpointOptions.CipherSuitesPolicy);
            CollectionAssert.AreEqual(new[] { WindowsWarning }, logger.Warnings);
        }
        else
        {
            Assert.IsNotNull(options.TlsEndpointOptions.CipherSuitesPolicy);
            // TLS 1.2 암호만 든 목록이지만 MQTTS는 HTTP/3 경고를 남기지 않는다(D005).
            Assert.IsEmpty(logger.Warnings);
        }
    }

    // TLS를 끄면(인증서 없음) 목록이 있어도 해석하지 않아 경고가 없다.
    [TestMethod]
    public void BuildServerOptions_NoCertificate_DoesNotResolveCipherSuites()
    {
        ListLogger logger = new ListLogger();

        MqttServerOptions options = MqttBrokerHostedService.BuildServerOptions(ConfigurationWith(DeviceSuites), null, logger);

        Assert.IsFalse(options.TlsEndpointOptions.IsEnabled);
        Assert.IsNull(options.TlsEndpointOptions.CipherSuitesPolicy);
        Assert.IsEmpty(logger.Warnings);
    }

    // C1: TLS를 켠 서버의 MQTTS 포트에 TLS 1.2·1.3 핸드셰이크가 성공한다.
    [TestMethod]
    [DataRow(SslProtocols.Tls12)]
    [DataRow(SslProtocols.Tls13)]
    public async Task Mqtts_Handshake_Succeeds(SslProtocols protocol)
    {
        string thumbprint = TestApp.WritePfx(Path.Combine(tempRoot, "server.pfx"), PfxPassword);
        AppConfiguration configuration = ConfigurationWith(null);
        using CancellationTokenSource cts = new CancellationTokenSource(Timeout);

        await using (WebApplication app = await TestApp.StartAsync(configuration, Path.Combine(tempRoot, "log")))
        {
            using TcpClient tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", configuration.MqttsPort!.Value, cts.Token);
            await using SslStream ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
                (_, cert, _, _) => cert is not null && cert.GetCertHashString() == thumbprint);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                EnabledSslProtocols = protocol,
            }, cts.Token);

            Assert.AreEqual(protocol, ssl.SslProtocol);
            await app.StopAsync();
        }
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
