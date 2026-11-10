using Biz.Bizadm.PowerManager.DeviceConfig.Protocol;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.Protocol
{
    [TestClass]
    public sealed class DeviceSettingsTests
    {
        private static readonly DeviceSettings Settings = new()
        {
            ApiServerAddr = "api.example.local",
            ApiServerPort = 443,
            ServerAddr = "mqtt.example.local",
            ServerPort = 8883,
            SslSupport = true,
            UserId = "user",
            Company = "DAWONDNS",
            Model = "B540_W",
            Lati = "",
            Long = "",
            Topic = "dwd",
        };

        [TestMethod]
        public void ToJson_AllValuesAreStrings()
        {
            string json = Settings.ToJson("a1b2c3d4e5f6", "MyRouter", "secret", "KEY0123456789abc");

            Assert.IsTrue(json.EndsWith('\n'));
            Assert.AreEqual(1, json.Count(c => c == '\n'));
            using JsonDocument doc = JsonDocument.Parse(json);
            Dictionary<string, string> values = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
            CollectionAssert.AreEquivalent(
                new[] { "mac", "api_server_addr", "api_server_port", "server_addr", "server_port", "ssl_support", "ssid", "pass",
                        "user_id", "company", "model", "lati", "long", "topic", "mqtt_key" },
                values.Keys.ToArray());
            Assert.AreEqual("443", values["api_server_port"]);
            Assert.AreEqual("8883", values["server_port"]);
            Assert.AreEqual("yes", values["ssl_support"]);
            Assert.AreEqual("secret", values["pass"]);
            Assert.AreEqual("KEY0123456789abc", values["mqtt_key"]);
            Assert.AreEqual("dwd", values["topic"]);
        }

        [TestMethod]
        public void ToJson_SslSupportFalse_IsNo()
        {
            string json = (Settings with { SslSupport = false }).ToJson("m", "s", "p", "k");
            StringAssert.Contains(json, "\"ssl_support\":\"no\"");
        }

        [TestMethod]
        public void ToJson_DoesNotEscapeSpecialCharacters()
        {
            string json = Settings.ToJson("m", "s", "a+b&c<d>'e", "k");
            StringAssert.Contains(json, "\"pass\":\"a+b&c<d>'e\"");
        }

        [TestMethod]
        public void ToJson_Masked_HidesSecrets()
        {
            string json = Settings.ToJson("m", "s", "secret", "KEY0123456789abc", masked: true);
            Assert.IsFalse(json.Contains("secret"));
            Assert.IsFalse(json.Contains("KEY0123456789abc"));
            StringAssert.Contains(json, "\"pass\":\"***\"");
            StringAssert.Contains(json, "\"mqtt_key\":\"***\"");
        }
    }
}
