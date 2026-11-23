using Biz.Bizadm.KMS.Credentials.Cipher;

namespace Biz.Bizadm.PowerManager.Server.Core.Tests;

/// <summary>프로세스 환경 변수를 바꾸므로 동시에 돌리지 않고, 시험마다 원래 값으로 되돌린다.</summary>
[TestClass]
[DoNotParallelize]
public sealed class CredentialStoreEnvironmentTests
{
    private static readonly string[] Variables =
        [CredentialStoreEnvironment.StoreVariable, CredentialStoreEnvironment.GpgTtyVariable, CredentialStoreEnvironment.SshTtyVariable];

    private Dictionary<string, string?> saved = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        saved = Variables.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        foreach (string name in Variables)
            Environment.SetEnvironmentVariable(name, null);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        foreach ((string name, string? value) in saved)
            Environment.SetEnvironmentVariable(name, value);
    }

    private static string? Get(string name) => Environment.GetEnvironmentVariable(name);

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Apply_Empty_ChangesNothing(string? store)
    {
        Environment.SetEnvironmentVariable(CredentialStoreEnvironment.StoreVariable, "wincredman");

        CredentialStoreEnvironment.Apply(store);

        Assert.AreEqual("wincredman", Get(CredentialStoreEnvironment.StoreVariable));
        Assert.IsNull(Get(CredentialStoreEnvironment.GpgTtyVariable));
    }

    [TestMethod]
    [DataRow("gpg")]
    [DataRow("secretservice")]
    public void Apply_Value_OverridesExistingStore(string store)
    {
        Environment.SetEnvironmentVariable(CredentialStoreEnvironment.StoreVariable, "wincredman");

        CredentialStoreEnvironment.Apply(store);

        Assert.AreEqual(store, Get(CredentialStoreEnvironment.StoreVariable));
    }

    [TestMethod]
    public void Apply_GpgWithoutTty_SetsDevNull()
    {
        CredentialStoreEnvironment.Apply("gpg");

        Assert.AreEqual("/dev/null", Get(CredentialStoreEnvironment.GpgTtyVariable));
    }

    [TestMethod]
    [DataRow("/dev/pts/1")]
    [DataRow("")]
    public void Apply_GpgWithGpgTty_KeepsGpgTty(string gpgTty)
    {
        Environment.SetEnvironmentVariable(CredentialStoreEnvironment.GpgTtyVariable, gpgTty);

        CredentialStoreEnvironment.Apply("gpg");

        Assert.AreEqual(gpgTty, Get(CredentialStoreEnvironment.GpgTtyVariable));
    }

    [TestMethod]
    [DataRow("/dev/pts/0")]
    [DataRow("")]
    public void Apply_GpgWithSshTty_LeavesGpgTtyUnset(string sshTty)
    {
        Environment.SetEnvironmentVariable(CredentialStoreEnvironment.SshTtyVariable, sshTty);

        CredentialStoreEnvironment.Apply("gpg");

        Assert.IsNull(Get(CredentialStoreEnvironment.GpgTtyVariable));
        Assert.AreEqual(sshTty, Get(CredentialStoreEnvironment.SshTtyVariable));
    }

    [TestMethod]
    public void Apply_SecretServiceWithoutTty_LeavesGpgTtyUnset()
    {
        CredentialStoreEnvironment.Apply("secretservice");

        Assert.IsNull(Get(CredentialStoreEnvironment.GpgTtyVariable));
    }

    /// <summary>
    /// 저장소를 만들기 전에 적용하면 Windows에서 gpg 저장소를 고르게 되어 패키지가 POSIX 오류로 멈춘다.
    /// 저장소를 만든 뒤에 적용하면 Windows 기본 저장소로 진행해 KEK 패스워드와 Salt를 만든다.
    /// </summary>
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void Protect_GpgOnWindows_FailsBeforeSaltIsWritten()
    {
        string root = Path.Combine(Path.GetTempPath(), "pm-credstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        OsKmsConfiguration osKms = new()
        {
            CredentialService = $"https://powermanager-test-{Guid.NewGuid():N}.local",
            CredentialAccount = "powermanager-test",
            Iterations = 1000,
            SecretsDekFilePath = "secrets.dek",
            CredentialStore = "gpg",
        };
        bool saved = false;
        try
        {
            Exception ex = Assert.Throws<Exception>(() => KmsSecretProtector.Protect(osKms, root, null, () => saved = true));

            StringAssert.Contains(ex.Message, "POSIX");
            Assert.IsFalse(saved);
            Assert.IsNull(osKms.Salt);
            Assert.IsFalse(File.Exists(Path.Combine(root, osKms.SecretsDekFilePath)));
        }
        finally
        {
            // 실패했을 때 Windows 저장소에 남았을 수 있는 시험 항목을 지운다.
            foreach (string name in Variables)
                Environment.SetEnvironmentVariable(name, null);
            OsKekCredentialProvider.CreateForCurrentOs(osKms.CredentialService, osKms.CredentialAccount, OsKekCredentialDefaults.Namespace)
                .RemovePassword();
            Directory.Delete(root, recursive: true);
        }
    }
}
