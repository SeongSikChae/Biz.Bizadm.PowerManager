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

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("gpg")]
    [DataRow("secretservice")]
    public void Validate_EmptyOrAllowedCredentialStore_Passes(string? store)
    {
        OsKmsConfiguration configuration = Create("https://powermanager.local");
        configuration.CredentialStore = store;

        configuration.Validate();
    }

    [TestMethod]
    [DataRow("GPG")]
    [DataRow("SecretService")]
    [DataRow(" gpg")]
    [DataRow("gpg ")]
    [DataRow("plaintext")]
    [DataRow("cache")]
    [DataRow("wincredman")]
    public void Validate_OtherCredentialStore_ThrowsWithAllowedValues(string store)
    {
        OsKmsConfiguration configuration = Create("https://powermanager.local");
        configuration.CredentialStore = store;

        ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(configuration.Validate);
        Assert.AreEqual("CredentialStore", ex.ParamName);
        StringAssert.Contains(ex.Message, "gpg");
        StringAssert.Contains(ex.Message, "secretservice");
    }
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
