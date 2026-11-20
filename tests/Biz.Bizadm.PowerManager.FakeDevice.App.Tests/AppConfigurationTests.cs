using Biz.Bizadm.PowerManager.FakeDevice.App.Security;
using System.Configuration;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Tests;

[TestClass]
public sealed class AppConfigurationTests
{
    private static AppConfiguration Valid() => new AppConfiguration { Device = new DeviceSection { Mac = "A1B2C3D4E5F6" } };

    private static void ValidateAll(AppConfiguration config)
    {
        ConfigurationValidator.Validate(config);
        config.Validate();
    }

    [TestMethod]
    public void Defaults_MatchSpecC1()
    {
        AppConfiguration config = Valid();
        ValidateAll(config);

        Assert.AreEqual("", config.ContentRootPath);
        Assert.AreEqual("dawon-fake-device", config.RecordDirectory);
        Assert.IsNull(config.OsKms);
        Assert.AreEqual("dwapi.dawonai.com", config.Dawon.ApiServerHost);
        Assert.AreEqual(18443, config.Dawon.ApiServerPort);
        Assert.AreEqual("dwmqtt.dawonai.com", config.Dawon.MqttServerHost);
        Assert.AreEqual(8883, config.Dawon.MqttServerPort);
        Assert.IsFalse(config.Dawon.SkipCertificateValidation);
        Assert.AreEqual("B540_W", config.Device.Model);
        Assert.AreEqual("DAWONDNS", config.Device.Company);
        Assert.AreEqual("", config.Device.UserId);
        Assert.AreEqual("0", config.Device.Lati);
        Assert.AreEqual("0", config.Device.Long);
        Assert.AreEqual("1.01.36", config.Device.FirmwareVersion);
        Assert.AreEqual("", config.Register.MqttKey);
        Assert.AreEqual("dwmqtt.dawonai.com", config.EffectiveVerifyServerAddr());
        Assert.AreEqual(60, config.Mqtt.KeepAliveSeconds);
        Assert.AreEqual("dwd", config.Mqtt.TopicPrefix);
        Assert.AreEqual("/lwt", config.Mqtt.WillTopic);
        Assert.AreEqual("offline", config.Mqtt.WillPayload);
        Assert.IsTrue(config.Publish.SendRegister);
        Assert.IsTrue(config.Publish.SendTimeSync);
        Assert.AreEqual("json", config.Publish.RegisterSuffix);
        Assert.AreEqual(0, config.Publish.QoS);
        Assert.AreEqual(0, config.Publish.TimeSyncIntervalSeconds);
        Assert.AreEqual(0, config.Publish.NotifyIntervalSeconds);
        Assert.IsTrue(config.Fota.AutoDownload);
        Assert.AreEqual("Mozilla/5.0 (Windows NT 5.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/30.0.1599.101 Safari/537.36", config.Fota.UserAgent);
        Assert.IsFalse(ConfigSecretProtector.HasSecrets(config));
    }

    [TestMethod]
    public void Options_FillDefaultsFromConfiguration()
    {
        AppConfiguration config = Valid();
        ValidateAll(config);
        string basePath = Path.Combine(Path.GetTempPath(), "fake-device-base");

        FakeDeviceOptions options = FakeDeviceOptions.Create(config, ConfigSecrets.Empty, basePath);

        Assert.AreEqual("a1b2c3d4e5f6", options.Mac);
        Assert.AreEqual("DAWONDNS-a1b2c3d4e5f6", options.ClientId);
        Assert.AreEqual("DAWONDNS-B540_W-a1b2c3d4e5f6", options.UserName);
        Assert.AreEqual("DAWONDNS-B540_W-a1b2c3d4e5f6", options.DeviceKey);
        Assert.IsNull(options.MqttKey);
        Assert.IsNull(options.Password);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(basePath, "dawon-fake-device")), options.RecordDirectory);
        Assert.AreEqual(TimeSpan.FromSeconds(10), options.RestTimeout);
        Assert.AreEqual(TimeSpan.FromSeconds(10), options.MqttTimeout);
        Assert.AreEqual(TimeSpan.FromSeconds(60), options.FotaTimeout);
        Assert.AreEqual(4 * 1024 * 1024, options.FotaMaxBytes);
    }

    [TestMethod]
    public void Options_UseConfiguredValues()
    {
        AppConfiguration config = Valid();
        config.Mqtt.ClientId = "custom-client";
        config.Mqtt.UserName = "custom-user";
        config.Register.VerifyServerAddr = "verify.example";
        config.RecordDirectory = Path.Combine(Path.GetTempPath(), "absolute-records");
        ValidateAll(config);

        FakeDeviceOptions options = FakeDeviceOptions.Create(config, new ConfigSecrets("key", "password"), "C:\\unused");

        Assert.AreEqual("custom-client", options.ClientId);
        Assert.AreEqual("custom-user", options.UserName);
        Assert.AreEqual("verify.example", options.VerifyServerAddr);
        Assert.AreEqual("key", options.MqttKey);
        Assert.AreEqual("password", options.Password);
        Assert.AreEqual(Path.GetFullPath(config.RecordDirectory), options.RecordDirectory);
    }

    [TestMethod]
    public void ExampleConfiguration_IsValid()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "config.example.yaml");
        AppConfiguration config = new YamlDotNet.Serialization.Deserializer().Deserialize<AppConfiguration>(File.ReadAllText(path));

        ValidateAll(config);

        Assert.AreEqual("000000000000", config.Device.Mac);
        Assert.IsFalse(ConfigSecretProtector.HasSecrets(config));
    }

    public static IEnumerable<object[]> InvalidCases()
    {
        yield return Case("mac short", c => c.Device.Mac = "a1b2c3d4e5");
        yield return Case("mac not hex", c => c.Device.Mac = "a1b2c3d4e5g6");
        yield return Case("api port 0", c => c.Dawon.ApiServerPort = 0);
        yield return Case("mqtt port 65536", c => c.Dawon.MqttServerPort = 65536);
        yield return Case("qos 3", c => c.Publish.QoS = 3);
        yield return Case("qos -1", c => c.Publish.QoS = -1);
        yield return Case("timesync interval negative", c => c.Publish.TimeSyncIntervalSeconds = -1);
        yield return Case("notify interval negative", c => c.Publish.NotifyIntervalSeconds = -1);
        yield return Case("keepalive negative", c => c.Mqtt.KeepAliveSeconds = -1);
        yield return Case("keepalive 65536", c => c.Mqtt.KeepAliveSeconds = 65536);
        yield return Case("verify addr outside alphabet", c => c.Register.VerifyServerAddr = "dwmqtt_dawonai.com");
        yield return Case("default verify addr outside alphabet", c => c.Dawon.MqttServerHost = "dwmqtt_dawonai.com");
        yield return Case("topic prefix empty", c => c.Mqtt.TopicPrefix = "");
        yield return Case("topic prefix wildcard", c => c.Mqtt.TopicPrefix = "dw+d");
        yield return Case("register suffix wildcard", c => c.Publish.RegisterSuffix = "json/#");
        yield return Case("will topic empty", c => c.Mqtt.WillTopic = "");
        yield return Case("will topic wildcard", c => c.Mqtt.WillTopic = "/lwt/#");
        yield return Case("user agent LF", c => c.Fota.UserAgent = "agent\nX-Injected: 1");
        yield return Case("user agent CR", c => c.Fota.UserAgent = "agent\r");
        yield return Case("user agent empty", c => c.Fota.UserAgent = "");
        yield return Case("notify element empty n", c => c.Publish.NotifyElements = [new NotifyElementSection { N = "", Sv = "1" }]);
        yield return Case("mqtt key without OsKms", c => c.Register.MqttKey = "secret-key");
        yield return Case("password without OsKms", c => c.Mqtt.Password = "secret-password");
        yield return Case("OsKms service not absolute", c =>
        {
            c.Register.MqttKey = "secret-key";
            c.OsKms = new OsKmsSection { CredentialService = "relative/service", CredentialAccount = "a", SecretsDekFilePath = "dek" };
        });
    }

    private static object[] Case(string name, Action<AppConfiguration> mutate) => [name, mutate];

    [TestMethod]
    [DynamicData(nameof(InvalidCases))]
    public void Validate_RejectsInvalidValue(string name, object mutate)
    {
        AppConfiguration config = Valid();
        ((Action<AppConfiguration>)mutate)(config);

        Assert.ThrowsExactly<ArgumentException>(() => ValidateAll(config), name);
    }

    [TestMethod]
    public void Validate_MissingMac_Fails()
    {
        AppConfiguration config = new AppConfiguration();

        Assert.Throws<Exception>(() => ValidateAll(config));
    }

    [TestMethod]
    public void Validate_SecretsWithOsKms_Passes()
    {
        AppConfiguration config = Valid();
        config.Mqtt.Password = "secret-password";
        config.OsKms = new OsKmsSection { CredentialService = "https://example.invalid/fake-device", CredentialAccount = "a", SecretsDekFilePath = "dek" };

        ValidateAll(config);

        Assert.IsTrue(ConfigSecretProtector.HasSecrets(config));
    }
}
