namespace Biz.Bizadm.PowerManager.Server.Core.Tests;

[TestClass]
public sealed class OsKmsConfigurationTests
{
    private static OsKmsConfiguration Create(string service) => new()
    {
        CredentialService = service,
        CredentialAccount = "powermanager",
        Iterations = 100000,
        SecretsDekFilePath = "secrets.dek",
    };

    [TestMethod]
    public void Validate_NonUriCredentialService_Throws()
        => Assert.ThrowsExactly<ArgumentException>(() => Create("powermanager").Validate());

    [TestMethod]
    public void Validate_UriCredentialService_Passes()
        => Create("https://powermanager.local").Validate();
}

[TestClass]
public sealed class TlsConfigurationTests
{
    [TestMethod]
    public void Validate_UseTlsWithoutCertificate_Throws()
        => Assert.Throws<ArgumentException>(() => new TlsConfiguration { UseTls = true, ServerCertificatePassword = "x" }.Validate());

    [TestMethod]
    public void Validate_UseTlsWithoutPassword_Throws()
        => Assert.Throws<ArgumentException>(() => new TlsConfiguration { UseTls = true, ServerCertificate = "cert.pfx" }.Validate());

    [TestMethod]
    public void Validate_NoTls_Passes()
        => new TlsConfiguration { UseTls = false }.Validate();
}
