using Biz.Bizadm.KMS.Credentials.Cipher;
using System.Net;
using System.Net.Sockets;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Tests;

/// <summary>Program.RunAsync의 종료 코드와 표준 오류. 표준 오류를 바꿔 끼우므로 동시에 돌리지 않는다.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProgramTests
{
    private string tempRoot = null!;
    private TextWriter originalError = null!;
    private StringWriter error = null!;
    private TcpListener listener = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        tempRoot = TestHarness.NewTempDirectory();
        originalError = System.Console.Error;
        error = new StringWriter();
        System.Console.SetError(error);
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        listenerPort = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [TestCleanup]
    public void TestCleanup()
    {
        listener.Stop();
        System.Console.SetError(originalError);
        TestHarness.DeleteDirectory(tempRoot);
    }

    private int listenerPort;

    private int ListenerPort => listenerPort;

    /// <summary>다원 주소를 시험 수신기로 둔 설정. <paramref name="extra"/>는 섹션 단위로 덧붙인다.</summary>
    private string Config(string mac = TestHarness.Mac, string registerSection = "", string mqttSection = "", string top = "")
        => $"""
            {top}
            RecordDirectory: {Path.Combine(tempRoot, "records")}
            Dawon:
              ApiServerHost: 127.0.0.1
              ApiServerPort: {ListenerPort}
              MqttServerHost: 127.0.0.1
              MqttServerPort: {ListenerPort}
            Device:
              Mac: "{mac}"
            {registerSection}
            {mqttSection}
            """;

    private string WriteConfig(string yaml)
    {
        string path = Path.Combine(tempRoot, "config.yaml");
        File.WriteAllText(path, yaml);
        return path;
    }

    private Task<int> RunAsync(string configPath) => Program.RunAsync(["--config", configPath, "--log", Path.Combine(tempRoot, "logs")]);

    private void AssertNoConnection() => Assert.IsFalse(listener.Pending(), "no connection to Dawon");

    [TestMethod]
    public async Task MissingConfigFile_Is2()
    {
        Assert.AreEqual(2, await RunAsync(Path.Combine(tempRoot, "missing.yaml")));

        StringAssert.Contains(error.ToString(), "Configuration error");
        StringAssert.Contains(error.ToString(), "missing.yaml");
    }

    [TestMethod]
    public async Task BrokenYaml_Is2()
    {
        Assert.AreEqual(2, await RunAsync(WriteConfig("Device: [unclosed\n  Mac: : :")));

        StringAssert.Contains(error.ToString(), "Configuration error");
        AssertNoConnection();
    }

    [TestMethod]
    public async Task InvalidMac_Is2()
    {
        Assert.AreEqual(2, await RunAsync(WriteConfig(Config(mac: "a1b2c3"))));

        StringAssert.Contains(error.ToString(), "Device.Mac");
        AssertNoConnection();
    }

    [TestMethod]
    public async Task VerifyServerAddrOutsideAlphabet_Is2()
    {
        Assert.AreEqual(2, await RunAsync(WriteConfig(Config(registerSection: "Register:\n  VerifyServerAddr: dwmqtt_dawonai.com"))));

        StringAssert.Contains(error.ToString(), "Register.VerifyServerAddr");
        AssertNoConnection();
    }

    [TestMethod]
    public async Task SecretsWithOtherError_NotInStandardError()
    {
        string yaml = Config(mac: "zzzzzzzzzzzz",
            registerSection: $"Register:\n  MqttKey: {TestHarness.TestKey}",
            mqttSection: $"Mqtt:\n  Password: {TestHarness.TestPassword}");

        Assert.AreEqual(2, await RunAsync(WriteConfig(yaml)));

        Assert.IsFalse(error.ToString().Contains(TestHarness.TestKey, StringComparison.Ordinal));
        Assert.IsFalse(error.ToString().Contains(TestHarness.TestPassword, StringComparison.Ordinal));
        AssertNoConnection();
    }

    [TestMethod]
    public async Task Arguments_HelpIs0_MissingIs1()
    {
        Assert.AreEqual(0, await Program.RunAsync(["--help"]));
        Assert.AreEqual(1, await Program.RunAsync([]));
    }

    [TestMethod]
    public void BasePath_DefaultsToExecutableFolder()
    {
        Assert.AreEqual(AppContext.BaseDirectory, Program.ResolveBasePath(new AppConfiguration()));
        Assert.AreEqual(AppContext.BaseDirectory, Program.ResolveBasePath(new AppConfiguration { ContentRootPath = null }));
        Assert.AreEqual(tempRoot, Program.ResolveBasePath(new AppConfiguration { ContentRootPath = tempRoot }));
    }

    /// <summary>
    /// 비밀 값이 든 설정을 처음 실행하면 암호문과 Salt로 설정 파일을 다시 쓰고, ContentRootPath는 비어 있는 그대로다(C1, D025·D026).
    /// 다원 주소는 닫힌 로컬 포트라 C4 연결 실패로 3을 낸다. 시험용 자격 증명은 끝나면 지운다.
    /// </summary>
    [TestMethod]
    public async Task FirstRunWithSecrets_RewritesEncrypted_KeepsEmptyContentRoot()
    {
        // 수신기를 닫아 다원 주소가 닫힌 로컬 포트가 되게 한다
        listener.Stop();
        string service = "https://test.invalid/biz-bizadm-powermanager-fakedevice-tests/" + Guid.NewGuid().ToString("N");
        string account = "fake-device-test";
        string yaml = Config(
            top: $"""
                ContentRootPath: ""
                OsKms:
                  CredentialService: {service}
                  CredentialAccount: {account}
                  SecretsDekFilePath: {Path.Combine(tempRoot, "secrets.dek")}
                """,
            registerSection: $"Register:\n  MqttKey: {TestHarness.TestKey}",
            mqttSection: $"Mqtt:\n  Password: {TestHarness.TestPassword}");
        string path = WriteConfig(yaml);
        try
        {
            Assert.AreEqual(3, await RunAsync(path), error.ToString());

            string rewritten = File.ReadAllText(path);
            Assert.IsFalse(rewritten.Contains(TestHarness.TestKey, StringComparison.Ordinal));
            Assert.IsFalse(rewritten.Contains(TestHarness.TestPassword, StringComparison.Ordinal));
            AppConfiguration config = new YamlDotNet.Serialization.Deserializer().Deserialize<AppConfiguration>(rewritten);
            Assert.IsTrue(config.Register.MqttKey!.StartsWith("DEKENC:", StringComparison.Ordinal));
            Assert.IsTrue(config.Mqtt.Password!.StartsWith("DEKENC:", StringComparison.Ordinal));
            Assert.IsFalse(string.IsNullOrEmpty(config.OsKms!.Salt));
            Assert.IsTrue(string.IsNullOrEmpty(config.ContentRootPath), $"ContentRootPath: {config.ContentRootPath}");
            Assert.IsTrue(File.Exists(Path.Combine(tempRoot, "secrets.dek")));
            Assert.IsTrue(Directory.Exists(Path.Combine(tempRoot, "records")));
        }
        finally
        {
            OsKekCredentialProvider.CreateForCurrentOs(service, account, OsKekCredentialDefaults.Namespace).RemovePassword();
        }
    }
}
