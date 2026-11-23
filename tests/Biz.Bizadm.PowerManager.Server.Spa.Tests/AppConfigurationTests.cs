using System.Configuration;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

[TestClass]
public sealed class AppConfigurationTests
{
    private const string OsKmsYaml = """
        OsKms:
          CredentialService: https://powermanager.local
          CredentialAccount: powermanager
          Iterations: 100000
          SecretsDekFilePath: secrets.dek
        """;

    private static AppConfiguration Deserialize(string yaml)
        => new YamlDotNet.Serialization.Deserializer().Deserialize<AppConfiguration>(yaml);

    /// <summary>서버가 Salt·보안값을 기록할 때와 같은 직렬화로 다시 써도 저장소 값이 남는다.</summary>
    [TestMethod]
    public void Serialize_WithCredentialStore_KeepsValue()
    {
        AppConfiguration configuration = Deserialize("ContentRootPath: .\n" + OsKmsYaml + "\n  CredentialStore: gpg\n");
        ConfigurationValidator.Validate(configuration);
        configuration.OsKms.Salt = "c2FsdA==";

        AppConfiguration reloaded = Deserialize(new YamlDotNet.Serialization.Serializer().Serialize(configuration));

        Assert.AreEqual("gpg", reloaded.OsKms.CredentialStore);
        Assert.AreEqual("c2FsdA==", reloaded.OsKms.Salt);
    }

    [TestMethod]
    public void Validate_InvalidCredentialStore_Throws()
    {
        AppConfiguration configuration = Deserialize("ContentRootPath: .\n" + OsKmsYaml + "\n  CredentialStore: plaintext\n");

        ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(() => ConfigurationValidator.Validate(configuration));
        Assert.AreEqual("CredentialStore", ex.ParamName);
    }

    [TestMethod]
    public void Validate_WithoutOsKms_ThrowsArgumentNull()
    {
        AppConfiguration configuration = Deserialize("ContentRootPath: .\n");

        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(() => ConfigurationValidator.Validate(configuration));
        Assert.AreEqual("OsKms", ex.ParamName);
    }

    [TestMethod]
    public void Validate_WithoutPortKeys_FillsDefaults()
    {
        AppConfiguration configuration = Deserialize("ContentRootPath: .\n" + OsKmsYaml);

        ConfigurationValidator.Validate(configuration);

        Assert.AreEqual("ClientApp", configuration.WebRootPath);
        Assert.AreEqual((ushort)80, configuration.HttpPort);
        Assert.AreEqual((ushort)443, configuration.HttpsPort);
        Assert.AreEqual((ushort)18446, configuration.DeviceHttpPort);
        Assert.AreEqual((ushort)18443, configuration.DeviceHttpsPort);
        Assert.AreEqual((ushort)1803, configuration.MqttPort);
        Assert.AreEqual((ushort)8883, configuration.MqttsPort);
        Assert.AreEqual("dwmqtt.dawonai.com", configuration.DeviceMqttServerAddr);
        Assert.AreEqual("powermanager.db", configuration.DatabasePath);
    }

    [TestMethod]
    public void Validate_WithDeviceKeys_KeepsValues()
    {
        AppConfiguration configuration = Deserialize("ContentRootPath: .\nDeviceMqttServerAddr: 192.0.2.10\nDatabasePath: data/devices.db\n" + OsKmsYaml);

        ConfigurationValidator.Validate(configuration);

        Assert.AreEqual("192.0.2.10", configuration.DeviceMqttServerAddr);
        Assert.AreEqual("data/devices.db", configuration.DatabasePath);
    }

    [TestMethod]
    [DataRow("host:8883")]
    [DataRow("my_host")]
    [DataRow("\"my host\"")]
    [DataRow("\"\"")]
    public void Validate_InvalidDeviceMqttServerAddr_Throws(string value)
    {
        AppConfiguration configuration = Deserialize($"ContentRootPath: .\nDeviceMqttServerAddr: {value}\n" + OsKmsYaml);

        ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(() => ConfigurationValidator.Validate(configuration));
        Assert.AreEqual("DeviceMqttServerAddr", ex.ParamName);
    }

    [TestMethod]
    public void Validate_WithPortKeys_KeepsValues()
    {
        AppConfiguration configuration = Deserialize("ContentRootPath: .\nHttpPort: 8080\nMqttPort: 11883\n" + OsKmsYaml);

        ConfigurationValidator.Validate(configuration);

        Assert.AreEqual((ushort)8080, configuration.HttpPort);
        Assert.AreEqual((ushort)11883, configuration.MqttPort);
        Assert.AreEqual((ushort)443, configuration.HttpsPort);
    }
}
