using Biz.Bizadm.PowerManager.DeviceConfig.Messages;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.Messages
{
    [TestClass]
    public sealed class DeviceMessageSerializerTests
    {
        private static DeviceSettings CreateSettings(DeviceFotaSettings? fota = null, string? mqttKey = "KEY123") => new()
        {
            UserId = "user@example.com/kakao",
            Company = "DAWONDNS",
            Model = "B540_W",
            Latitude = "37.0",
            Longitude = "127.0",
            ApiServerAddr = "api.local",
            ApiServerPort = "18443",
            MqttServerAddr = "mqtt.local",
            MqttServerPort = "8883",
            SslSupport = "yes",
            Topic = "dwd",
            MqttKey = mqttKey,
            Fota = fota,
        };

        [TestMethod]
        public void Settings_KeysInReferenceOrder()
        {
            string json = DeviceMessageSerializer.Serialize(
                DeviceSettingsMessage.From(CreateSettings(), new WifiProfile("HomeAP", "secret"), "aabbccddeeff"));

            Assert.AreEqual(
                "{\"mac\":\"aabbccddeeff\",\"api_server_addr\":\"api.local\",\"api_server_port\":\"18443\"," +
                "\"server_addr\":\"mqtt.local\",\"server_port\":\"8883\",\"ssl_support\":\"yes\"," +
                "\"ssid\":\"HomeAP\",\"pass\":\"secret\",\"user_id\":\"user@example.com/kakao\"," +
                "\"company\":\"DAWONDNS\",\"model\":\"B540_W\",\"lati\":\"37.0\",\"long\":\"127.0\"," +
                "\"topic\":\"dwd\",\"mqtt_key\":\"KEY123\"}",
                json);
        }

        [TestMethod]
        public void Settings_OmitsNullAndEmptyOptionalFields()
        {
            string json = DeviceMessageSerializer.Serialize(
                DeviceSettingsMessage.From(CreateSettings(new DeviceFotaSettings("", null, null, null), mqttKey: ""),
                    new WifiProfile("HomeAP", "secret"), "aabbccddeeff"));

            Assert.DoesNotContain("mqtt_key", json);
            Assert.DoesNotContain("LFota_", json);
        }

        [TestMethod]
        public void Settings_IncludesFotaKeys()
        {
            string json = DeviceMessageSerializer.Serialize(
                DeviceSettingsMessage.From(CreateSettings(new DeviceFotaSettings("/fota", "fota.local", "80", "1.01.35")),
                    new WifiProfile("HomeAP", "secret"), "aabbccddeeff"));

            StringAssert.EndsWith(json,
                "\"LFota_Url\":\"/fota\",\"LFota_Addr\":\"fota.local\",\"LFota_Port\":\"80\",\"LFota_Version\":\"1.01.35\"}");
        }

        [TestMethod]
        public void Settings_DoesNotEscapeAtOrSlash()
        {
            string json = DeviceMessageSerializer.Serialize(
                DeviceSettingsMessage.From(CreateSettings(), new WifiProfile("Home+AP", "p@ss/word"), "aabbccddeeff"));

            Assert.Contains("\"pass\":\"p@ss/word\"", json);
            Assert.Contains("\"ssid\":\"Home+AP\"", json);
        }

        [TestMethod]
        public void Command_WithMac_MacFirst()
        {
            Assert.AreEqual("{\"mac\":\"aabbccddeeff\",\"command\":\"connectap\"}",
                DeviceMessageSerializer.Serialize(new DeviceCommandMessage("connectap", "aabbccddeeff")));
        }

        [TestMethod]
        public void Command_WithoutMac_OmitsMac()
        {
            Assert.AreEqual("{\"command\":\"apscan\"}", DeviceMessageSerializer.Serialize(new DeviceCommandMessage("apscan")));
        }

        [TestMethod]
        public void Mask_HidesPassAndMqttKey()
        {
            string json = DeviceMessageSerializer.Serialize(
                DeviceSettingsMessage.From(CreateSettings(mqttKey: "KEY\"123"), new WifiProfile("HomeAP", "se\\cret\""), "aabbccddeeff"));

            string masked = DeviceMessageSerializer.Mask(json);

            Assert.Contains("\"pass\":\"***\"", masked);
            Assert.Contains("\"mqtt_key\":\"***\"", masked);
            Assert.DoesNotContain("cret", masked);
            Assert.DoesNotContain("KEY", masked);
            Assert.Contains("\"ssid\":\"HomeAP\"", masked);
        }

        [TestMethod]
        public void IsTerminal_OnlyConnectionEndingCommands()
        {
            DeviceCommand[] terminal = [DeviceCommand.ConnectAp, DeviceCommand.Reboot, DeviceCommand.FactoryReset, DeviceCommand.TestFactoryReset];
            foreach (DeviceCommand command in Enum.GetValues<DeviceCommand>())
                Assert.AreEqual(terminal.Contains(command), command.IsTerminal(), command.ToString());
        }
    }
}
