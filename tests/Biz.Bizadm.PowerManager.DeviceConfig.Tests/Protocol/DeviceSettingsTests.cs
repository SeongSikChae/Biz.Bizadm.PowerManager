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

        // 참고 구현 PowerManagerConfig MqttConfigurationV2의 속성 순서
        private static readonly string[] V3Keys =
        [
            "mac", "api_server_addr", "api_server_port", "server_addr", "server_port", "ssl_support", "ssid", "pass",
            "user_id", "company", "model", "lati", "long", "topic",
        ];

        [TestMethod]
        public void ToJson_KeysInV3OrderAndAllValuesAreStrings()
        {
            string json = Settings.ToJson("a1b2c3d4e5f6", "MyRouter", "secret");

            Assert.IsTrue(json.EndsWith('\n'));
            Assert.AreEqual(1, json.Count(c => c == '\n'));
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonProperty[] properties = doc.RootElement.EnumerateObject().ToArray();
            CollectionAssert.AreEqual(V3Keys, properties.Select(p => p.Name).ToArray());
            Assert.IsTrue(properties.All(p => p.Value.ValueKind == JsonValueKind.String));
            Dictionary<string, string> values = properties.ToDictionary(p => p.Name, p => p.Value.GetString()!);
            Assert.AreEqual("443", values["api_server_port"]);
            Assert.AreEqual("8883", values["server_port"]);
            Assert.AreEqual("yes", values["ssl_support"]);
            Assert.AreEqual("secret", values["pass"]);
            Assert.AreEqual("dwd", values["topic"]);
        }

        [TestMethod]
        public void ToJson_HasNoMqttKey()
        {
            string json = Settings.ToJson("m", "s", "p");
            Assert.IsFalse(json.Contains("mqtt_key"));
        }

        [TestMethod]
        public void ToJson_SslSupportFalse_IsNo()
        {
            string json = (Settings with { SslSupport = false }).ToJson("m", "s", "p");
            StringAssert.Contains(json, "\"ssl_support\":\"no\"");
        }

        [TestMethod]
        public void ToJson_EscapesLikeDefaultEncoder()
        {
            string json = Settings.ToJson("m", "s", "a+b&c<d>'e");
            StringAssert.Contains(json, "\"pass\":\"a\\u002Bb\\u0026c\\u003Cd\\u003E\\u0027e\"");
        }

        [TestMethod]
        public void ToJson_Masked_HidesPassOnly()
        {
            string json = Settings.ToJson("m", "MyRouter", "secret", masked: true);
            Assert.IsFalse(json.Contains("secret"));
            StringAssert.Contains(json, "\"pass\":\"***\"");
            StringAssert.Contains(json, "\"ssid\":\"MyRouter\"");
            StringAssert.Contains(json, "\"user_id\":\"user\"");
        }
    }
}
