using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;
using System.Configuration;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests
{
    [TestClass]
    public sealed class McpConfigurationTests
    {
        private static McpConfiguration Valid() => new()
        {
            ContentRootPath = @"C:\DeviceConfigMcp",
            OsKms = new OsKmsSection
            {
                CredentialService = "https://deviceconfig.powermanager.local",
                CredentialAccount = "deviceconfig",
                SecretsDekFilePath = "deviceconfig.dek",
            },
            Device = new DeviceSection(),
            WifiProfiles = [],
            Settings = null,
        };

        private static SettingsSection Settings() => new()
        {
            ApiServerAddr = "api.example.local",
            ApiServerPort = 443,
            ServerAddr = "mqtt.example.local",
            ServerPort = 8883,
            SslSupport = true,
            UserId = "user",
        };

        private static void Validate(McpConfiguration config)
        {
            ConfigurationValidator.Validate(config);
            config.Validate();
        }

        [TestMethod]
        public void Validate_NoWifiProfilesNoSettings_Ok()
        {
            McpConfiguration config = Valid();
            config.WifiProfiles = null!; // YAML에 키가 없거나 빈 값

            Validate(config);

            Assert.IsEmpty(config.WifiProfiles);
            Assert.IsNull(config.Settings);
        }

        [TestMethod]
        public void Validate_WifiProfilesWithoutSettings_Ok()
        {
            McpConfiguration config = Valid();
            config.WifiProfiles = [new WifiProfileSection { Ssid = "MyRouter", Pass = "a" }];

            Validate(config); // App과 달리 ConnectAp 여부와 관계없이 Settings는 기동 시 필수가 아니다

            Assert.IsNull(config.Settings);
        }

        [TestMethod]
        public void Validate_DuplicateSsid_Throws()
        {
            McpConfiguration config = Valid();
            config.WifiProfiles =
            [
                new WifiProfileSection { Ssid = "MyRouter", Pass = "a" },
                new WifiProfileSection { Ssid = "MyRouter", Pass = "b" },
            ];

            ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(() => Validate(config));
            StringAssert.Contains(ex.Message, "Duplicate WifiProfiles.Ssid: MyRouter");
        }

        [TestMethod]
        public void Validate_SettingsPortOutOfRange_Throws()
        {
            McpConfiguration config = Valid();
            config.Settings = Settings();
            config.Settings.ServerPort = 70000;

            ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(() => Validate(config));
            StringAssert.Contains(ex.Message, "Settings.ServerPort out of range");
        }

        [TestMethod]
        public void Validate_OsKmsMissing_Throws()
        {
            McpConfiguration config = Valid();
            config.OsKms = null!;

            ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(() => Validate(config));
            StringAssert.Contains(ex.Message, "OsKms is required");
        }

        [TestMethod]
        public void ToOptions_CopiesDeviceAndSettings()
        {
            McpConfiguration config = Valid();
            config.Device = new DeviceSection { ApPrefix = "ABC-", Host = "10.0.0.1", Port = 6000, TimeoutSeconds = 7, ApConnectTimeoutSeconds = 40 };
            config.Settings = Settings();
            config.Settings.Lati = "37.5";
            config.Settings.Long = "127.0";
            WifiProfile[] profiles = [new WifiProfile("MyRouter", "secret")];

            DeviceConfigOptions options = config.ToOptions(profiles);

            Assert.AreEqual("10.0.0.1", options.Host);
            Assert.AreEqual(6000, options.Port);
            Assert.AreEqual(TimeSpan.FromSeconds(7), options.Timeout);
            Assert.AreEqual("ABC-", options.ApPrefix);
            Assert.AreEqual(TimeSpan.FromSeconds(40), options.ApConnectTimeout);
            Assert.IsEmpty(options.Commands);
            Assert.AreSame(profiles, options.WifiProfiles);
            Assert.IsNotNull(options.Settings);
            Assert.AreEqual("api.example.local", options.Settings.ApiServerAddr);
            Assert.AreEqual(443, options.Settings.ApiServerPort);
            Assert.AreEqual("mqtt.example.local", options.Settings.ServerAddr);
            Assert.AreEqual(8883, options.Settings.ServerPort);
            Assert.IsTrue(options.Settings.SslSupport);
            Assert.AreEqual("user", options.Settings.UserId);
            Assert.AreEqual("DAWONDNS", options.Settings.Company);
            Assert.AreEqual("B540_W", options.Settings.Model);
            Assert.AreEqual("37.5", options.Settings.Lati);
            Assert.AreEqual("127.0", options.Settings.Long);
            Assert.AreEqual("dwd", options.Settings.Topic);
        }
    }
}
