using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Tests.Fakes;

/// <summary>
/// 테스트용 자체 서명 서버 인증서(localhost). 키를 보존하도록 PFX로 내보냈다가 다시 불러온다.
/// </summary>
internal static class TestCertificate
{
    public static X509Certificate2 CreateLocalhost()
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }
}
