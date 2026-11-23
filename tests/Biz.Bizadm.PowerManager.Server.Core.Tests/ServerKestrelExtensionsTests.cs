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

    private const string WindowsWarning = "IncludeCipherSuites is not supported on Windows and is ignored; OS default cipher suites are used.";
    private const string Http3Warning = "IncludeCipherSuites has no TLS 1.3 cipher suite; HTTP/3 connections will fail.";

    // mqtts-tls-policy C3: 리눅스 경로(isWindows: false)에서 잘못된 이름은 경고하고 빼며, 남은 이름으로 목록을 만든다.
    [TestMethod]
    public void ResolveCipherSuites_NonWindows_UnknownNameIsExcludedWithWarning()
    {
        ListLogger logger = new ListLogger();

        IReadOnlyCollection<TlsCipherSuite>? suites = ServerKestrelExtensions.ResolveCipherSuites(
            "TLS_RSA_WITH_AES_256_CBC_SHA256, NOT_A_SUITE, TLS_AES_128_GCM_SHA256", logger, warnIfNoTls13: true, isWindows: false);

        Assert.IsNotNull(suites);
        CollectionAssert.AreEquivalent(new[] { TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA256, TlsCipherSuite.TLS_AES_128_GCM_SHA256 }, suites.ToArray());
        CollectionAssert.AreEqual(new[] { "Unknown cipher suite NOT_A_SUITE is ignored." }, logger.Warnings);
    }

    // mqtts-tls-policy C3: 남은 이름이 없으면 제한하지 않는다(null).
    [TestMethod]
    public void ResolveCipherSuites_NonWindows_NoValidName_ReturnsNull()
    {
        ListLogger logger = new ListLogger();

        IReadOnlyCollection<TlsCipherSuite>? suites = ServerKestrelExtensions.ResolveCipherSuites("NOT_A_SUITE", logger, warnIfNoTls13: false, isWindows: false);

        Assert.IsNull(suites);
        CollectionAssert.AreEqual(new[] { "Unknown cipher suite NOT_A_SUITE is ignored." }, logger.Warnings);
    }

    // mqtts-tls-policy C3(D005): warnIfNoTls13이 false면 TLS 1.2 암호만 있어도 HTTP/3 경고를 남기지 않고, true(HTTPS)면 남긴다.
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, true)]
    public void ResolveCipherSuites_NonWindows_Tls12OnlyList_Http3WarningFollowsFlag(bool warnIfNoTls13, bool expectWarning)
    {
        ListLogger logger = new ListLogger();

        IReadOnlyCollection<TlsCipherSuite>? suites = ServerKestrelExtensions.ResolveCipherSuites(
            "TLS_RSA_WITH_AES_256_CBC_SHA256,TLS_RSA_WITH_AES_128_CBC_SHA256", logger, warnIfNoTls13, isWindows: false);

        Assert.IsNotNull(suites);
        Assert.HasCount(2, suites);
        CollectionAssert.AreEqual(expectWarning ? new[] { Http3Warning } : Array.Empty<string>(), logger.Warnings);
    }

    // mqtts-tls-policy C3: 없거나 비었거나 공백뿐이면 OS와 상관없이 제한하지 않고 경고도 없다.
    [TestMethod]
    [DataRow(null, false)]
    [DataRow("", false)]
    [DataRow("   ", false)]
    [DataRow(null, true)]
    [DataRow("", true)]
    [DataRow("   ", true)]
    public void ResolveCipherSuites_Blank_ReturnsNullWithoutWarning(string? value, bool isWindows)
    {
        ListLogger logger = new ListLogger();

        IReadOnlyCollection<TlsCipherSuite>? suites = ServerKestrelExtensions.ResolveCipherSuites(value, logger, warnIfNoTls13: true, isWindows);

        Assert.IsNull(suites);
        Assert.IsEmpty(logger.Warnings);
    }

    // mqtts-tls-policy C4: Windows면 이름을 해석하지 않고(잘못된 이름 경고 없음) Windows 경고 하나만 남긴다.
    [TestMethod]
    public void ResolveCipherSuites_Windows_IgnoresListWithSingleWarning()
    {
        ListLogger logger = new ListLogger();

        IReadOnlyCollection<TlsCipherSuite>? suites = ServerKestrelExtensions.ResolveCipherSuites("NOT_A_SUITE,TLS_RSA_WITH_AES_128_CBC_SHA256", logger, warnIfNoTls13: true, isWindows: true);

        Assert.IsNull(suites);
        CollectionAssert.AreEqual(new[] { WindowsWarning }, logger.Warnings);
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
