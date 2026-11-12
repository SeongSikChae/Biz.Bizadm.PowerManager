using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Biz.Bizadm.PowerManager.Server.Core.Tests;

[TestClass]
public sealed class ServerKestrelExtensionsTests
{
    private static X509Certificate2 PublicOnly(string commonName)
    {
        using X509Certificate2 certificate = TestCertificates.CreateSelfSigned(commonName);
        return X509CertificateLoader.LoadCertificate(certificate.RawData);
    }

    [TestMethod]
    public void BuildCertificateChain_OrdersByNameAndSkipsPrivateKeyCertificates()
    {
        using X509Certificate2 leaf = TestCertificates.CreateSelfSigned("leaf");
        X509Certificate2Collection collection = [leaf, PublicOnly("root"), PublicOnly("intermediate")];
        ListLogger logger = new ListLogger();

        X509Certificate2Collection chain = ServerKestrelExtensions.BuildCertificateChain(collection, "intermediate, root, leaf, missing", logger);

        CollectionAssert.AreEqual(new[] { "intermediate", "root" },
            chain.Select(c => c.GetNameInfo(X509NameType.SimpleName, false)).ToArray());
        Assert.IsEmpty(logger.Warnings);
    }

    [TestMethod]
    public void BuildCertificateChain_DuplicateName_UsesFirstAndWarns()
    {
        X509Certificate2 first = PublicOnly("ca");
        X509Certificate2 second = PublicOnly("ca");
        ListLogger logger = new ListLogger();

        X509Certificate2Collection chain = ServerKestrelExtensions.BuildCertificateChain([first, second], "ca", logger);

        Assert.HasCount(1, chain);
        Assert.AreEqual(first.Thumbprint, chain[0].Thumbprint);
        Assert.HasCount(1, logger.Warnings);
    }

    [TestMethod]
    public void ParseCipherSuites_UnknownName_IsExcludedWithWarning()
    {
        ListLogger logger = new ListLogger();

        IReadOnlyCollection<TlsCipherSuite> suites = ServerKestrelExtensions.ParseCipherSuites("TLS_AES_128_GCM_SHA256, NOT_A_SUITE", logger);

        CollectionAssert.AreEquivalent(new[] { TlsCipherSuite.TLS_AES_128_GCM_SHA256 }, suites.ToArray());
        Assert.HasCount(1, logger.Warnings);
    }

    [TestMethod]
    public void WarnIfNoTls13CipherSuite_OnlyTls12Suites_Warns()
    {
        ListLogger logger = new ListLogger();

        ServerKestrelExtensions.WarnIfNoTls13CipherSuite([TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256], logger);

        Assert.HasCount(1, logger.Warnings);
    }

    [TestMethod]
    public void CreateCipherSuitesPolicy_OsBranch()
    {
        ListLogger logger = new ListLogger();

        CipherSuitesPolicy? policy = ServerKestrelExtensions.CreateCipherSuitesPolicy("TLS_AES_128_GCM_SHA256", logger);

        if (OperatingSystem.IsWindows())
        {
            Assert.IsNull(policy);
            Assert.HasCount(1, logger.Warnings);
        }
        else
        {
            Assert.IsNotNull(policy);
            Assert.IsEmpty(logger.Warnings);
        }
    }
}
