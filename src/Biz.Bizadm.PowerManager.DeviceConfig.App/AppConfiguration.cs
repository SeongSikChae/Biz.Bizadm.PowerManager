using System.Configuration;
using System.Configuration.Annotation;
using System.Text;

namespace Biz.Bizadm.PowerManager.DeviceConfig.App
{
    /// <summary>
    /// YAML 설정(키는 속성 이름 그대로, PascalCase). 중첩 섹션·목록·조건부 필수는 <see cref="Validate"/>에서 검사한다.
    /// </summary>
    internal sealed class AppConfiguration : IValidatableConfiguration
    {
        [Property(PropertyType.STRING, required: true)]
        public string ContentRootPath { get; set; } = null!;

        public OsKmsSection OsKms { get; set; } = null!;

        public DeviceSection Device { get; set; } = new();

        public List<DeviceCommand> Commands { get; set; } = [];

        public List<WifiProfileSection> WifiProfiles { get; set; } = [];

        public SettingsSection? Settings { get; set; }

        public void Validate()
        {
            if (OsKms is null)
                throw new ArgumentException("OsKms is required");
            ConfigurationValidator.Validate(OsKms);
            Device ??= new DeviceSection();
            ConfigurationValidator.Validate(Device);
            Device.Validate();

            Commands ??= [];
            WifiProfiles ??= [];
            if (Commands.Count == 0)
                throw new ArgumentException("Commands must have at least one item");
            int terminalIndex = Commands.FindIndex(command => command.IsTerminal());
            if (terminalIndex >= 0 && terminalIndex != Commands.Count - 1)
                throw new ArgumentException($"{Commands[terminalIndex]} must be the last command (the device leaves SoftAP or reboots)");
            if (!Commands.Contains(DeviceCommand.ConnectAp))
                return;

            if (WifiProfiles.Count == 0)
                throw new ArgumentException("WifiProfiles is required for ConnectAp");
            foreach (WifiProfileSection profile in WifiProfiles)
                ConfigurationValidator.Validate(profile);
            string? duplicate = WifiProfiles.GroupBy(profile => profile.Ssid, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1)?.Key;
            if (duplicate is not null)
                throw new ArgumentException($"Duplicate WifiProfiles.Ssid: {duplicate}");
            if (WifiProfiles.Any(profile => !Ascii.IsValid(profile.Ssid)))
                throw new ArgumentException("WifiProfiles.Ssid must be ASCII");
            if (Settings is null)
                throw new ArgumentException("Settings is required for ConnectAp");
            ConfigurationValidator.Validate(Settings);
        }
    }

    internal sealed class OsKmsSection
    {
        [Property(PropertyType.STRING, required: true)]
        public string CredentialService { get; set; } = null!;

        [Property(PropertyType.STRING, required: true)]
        public string CredentialAccount { get; set; } = null!;

        /// <summary>최초 기동 시 생성·기록한다.</summary>
        [Property(PropertyType.STRING, required: false)]
        public string? Salt { get; set; }

        [Property(PropertyType.INT, required: false)]
        public int Iterations { get; set; } = 600000;

        [Property(PropertyType.STRING, required: true)]
        public string SecretsDekFilePath { get; set; } = null!;
    }

    internal sealed class DeviceSection
    {
        [Property(PropertyType.STRING, required: false)]
        public string ApSsidPrefix { get; set; } = "DWD-";

        [Property(PropertyType.INT, required: false)]
        public int ApConnectTimeoutSeconds { get; set; } = 30;

        [Property(PropertyType.STRING, required: false)]
        public string Host { get; set; } = "192.168.4.1";

        [Property(PropertyType.INT, required: false)]
        public int Port { get; set; } = 5000;

        [Property(PropertyType.INT, required: false)]
        public int TimeoutSeconds { get; set; } = 5;

        [Property(PropertyType.INT, required: false)]
        public int ApScanTimeoutSeconds { get; set; } = 15;

        public void Validate()
        {
            if (string.IsNullOrEmpty(ApSsidPrefix))
                throw new ArgumentException("Device.ApSsidPrefix must not be empty");
            if (string.IsNullOrEmpty(Host))
                throw new ArgumentException("Device.Host must not be empty");
            if (Port is < 1 or > 65535)
                throw new ArgumentException($"Device.Port out of range: {Port}");
            if (ApConnectTimeoutSeconds <= 0 || TimeoutSeconds <= 0 || ApScanTimeoutSeconds <= 0)
                throw new ArgumentException("Device timeouts must be positive");
        }
    }

    internal sealed class WifiProfileSection
    {
        [Property(PropertyType.STRING, required: true)]
        public string Ssid { get; set; } = null!;

        /// <summary>기동 시 KMS로 암호화(DEKENC:)한다.</summary>
        [Property(PropertyType.STRING, required: true)]
        public string Pass { get; set; } = null!;
    }

    internal sealed class SettingsSection
    {
        /// <summary>비우면 핸드셰이크 START_OK 응답의 MAC을 쓴다.</summary>
        [Property(PropertyType.STRING, required: false)]
        public string? Mac { get; set; }

        [Property(PropertyType.STRING, required: true)]
        public string UserId { get; set; } = null!;

        [Property(PropertyType.STRING, required: false)]
        public string Company { get; set; } = "DAWONDNS";

        [Property(PropertyType.STRING, required: true)]
        public string Model { get; set; } = null!;

        [Property(PropertyType.STRING, required: false)]
        public string Latitude { get; set; } = "0";

        [Property(PropertyType.STRING, required: false)]
        public string Longitude { get; set; } = "0";

        [Property(PropertyType.STRING, required: true)]
        public string ApiServerAddr { get; set; } = null!;

        [Property(PropertyType.STRING, required: true)]
        public string ApiServerPort { get; set; } = null!;

        [Property(PropertyType.STRING, required: true)]
        public string MqttServerAddr { get; set; } = null!;

        [Property(PropertyType.STRING, required: true)]
        public string MqttServerPort { get; set; } = null!;

        [Property(PropertyType.STRING, required: false)]
        public string SslSupport { get; set; } = "yes";

        [Property(PropertyType.STRING, required: true)]
        public string Topic { get; set; } = null!;

        /// <summary>비우면 ConnectAp 때 자동 생성한다. 기동 시 KMS로 암호화(DEKENC:)한다.</summary>
        [Property(PropertyType.STRING, required: false)]
        public string? MqttKey { get; set; }

        public FotaSection? Fota { get; set; }
    }

    internal sealed class FotaSection
    {
        public string? Url { get; set; }
        public string? Addr { get; set; }
        public string? Port { get; set; }
        public string? Version { get; set; }
    }
}
