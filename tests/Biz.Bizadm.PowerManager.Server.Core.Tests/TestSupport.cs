using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Biz.Bizadm.PowerManager.Server.Core.Tests;

internal sealed class ListLogger : ILogger
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

internal static class TestCertificates
{
    public static X509Certificate2 CreateSelfSigned(string commonName)
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    /// <summary>임시 PFX 파일을 쓰고 지문을 반환한다.</summary>
    public static string WritePfx(string path, string password, string commonName = "localhost")
    {
        using X509Certificate2 certificate = CreateSelfSigned(commonName);
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return certificate.Thumbprint;
    }
}

internal static class FreePort
{
    /// <summary>TCP와 UDP([::] 듀얼 모드)가 모두 비어 있는 포트. HTTP/3(QUIC)가 같은 UDP 포트를 쓰기 때문이다.</summary>
    public static ushort TcpAndUdp()
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            TcpListener tcp = new TcpListener(IPAddress.IPv6Any, 0);
            tcp.Server.DualMode = true;
            tcp.Start();
            int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            try
            {
                using Socket udp = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
                udp.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
                return (ushort)port;
            }
            catch (SocketException)
            {
            }
            finally
            {
                tcp.Stop();
            }
        }
        throw new InvalidOperationException("No free TCP+UDP port.");
    }

    public static ushort Tcp()
    {
        TcpListener tcp = new TcpListener(IPAddress.IPv6Any, 0);
        tcp.Server.DualMode = true;
        tcp.Start();
        int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return (ushort)port;
    }
}
