using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using System.Configuration;
using System.Configuration.Annotation;

namespace Biz.Bizadm.PowerManager.DeviceConfig.App
{
    /// <summary>
    /// YAML 설정(키는 속성 이름 그대로, PascalCase). 필수 항목은 [Property(required)] + ConfigurationValidator,
    /// 범위·조건부 필수는 <see cref="Validate"/>에서 검사한다(설계 6절).
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
            if (!Uri.TryCreate(OsKms.CredentialService, UriKind.Absolute, out _))
                throw new ArgumentException($"OsKms.CredentialService must be an absolute URI: {OsKms.CredentialService}");

            Device ??= new DeviceSection();
            ConfigurationValidator.Validate(Device);
            ValidatePort("Device.Port", Device.Port);
            if (Device.TimeoutSeconds <= 0)
                throw new ArgumentException($"Device.TimeoutSeconds must be positive: {Device.TimeoutSeconds}");
            if (Device.ApConnectTimeoutSeconds <= 0)
                throw new ArgumentException($"Device.ApConnectTimeoutSeconds must be positive: {Device.ApConnectTimeoutSeconds}");

            Commands ??= [];
            WifiProfiles ??= [];
            if (Commands.Count == 0)
                throw new ArgumentException("Commands must have at least one item");
            if (!Commands.Contains(DeviceCommand.ConnectAp))
                return;

            if (WifiProfiles.Count == 0)
                throw new ArgumentException("WifiProfiles is required for ConnectAp");
            foreach (WifiProfileSection profile in WifiProfiles)
                ConfigurationValidator.Validate(profile);
            string? duplicate = WifiProfiles.GroupBy(p => p.Ssid, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1)?.Key;
            if (duplicate is not null)
                throw new ArgumentException($"Duplicate WifiProfiles.Ssid: {duplicate}");
            if (Settings is null)
                throw new ArgumentException("Settings is required for ConnectAp");
            ConfigurationValidator.Validate(Settings);
            ValidatePort("Settings.ApiServerPort", Settings.ApiServerPort);
            ValidatePort("Settings.ServerPort", Settings.ServerPort);
        }

        private static void ValidatePort(string name, int port)
        {
            if (port is < 1 or > 65535)
                throw new ArgumentException($"{name} out of range: {port}");
        }
    }

    internal sealed class OsKmsSection
    {
        /// <summary>OS 자격 증명 금고 서비스. 절대 URI여야 한다.</summary>
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
        public string ApPrefix { get; set; } = "DWD-";

        /// <summary>PC Wi-Fi 미구현 OS에서 접속할 장치 주소.</summary>
        [Property(PropertyType.STRING, required: false)]
        public string Host { get; set; } = "192.168.244.1";

        [Property(PropertyType.INT, required: false)]
        public int Port { get; set; } = 5000;

        [Property(PropertyType.INT, required: false)]
        public int TimeoutSeconds { get; set; } = 10;

        [Property(PropertyType.INT, required: false)]
        public int ApConnectTimeoutSeconds { get; set; } = 30;
    }

    internal sealed class WifiProfileSection
    {
        [Property(PropertyType.STRING, required: true)]
        public string Ssid { get; set; } = null!;

        /// <summary>기동 시 OsKms로 암호화(DEKENC:)해 설정 파일에 다시 쓴다.</summary>
        [Property(PropertyType.STRING, required: true)]
        public string Pass { get; set; } = null!;
    }

    /// <summary>ConnectAp 설정 JSON 값. 전송 시 모두 문자열로 바꾼다(SslSupport → yes/no).</summary>
    internal sealed class SettingsSection
    {
        [Property(PropertyType.STRING, required: true)]
        public string ApiServerAddr { get; set; } = null!;

        [Property(PropertyType.INT, required: true)]
        public int ApiServerPort { get; set; }

        [Property(PropertyType.STRING, required: true)]
        public string ServerAddr { get; set; } = null!;

        [Property(PropertyType.INT, required: true)]
        public int ServerPort { get; set; }

        [Property(PropertyType.BOOL, required: false)]
        public bool SslSupport { get; set; }

        [Property(PropertyType.STRING, required: true)]
        public string UserId { get; set; } = null!;

        [Property(PropertyType.STRING, required: false)]
        public string Company { get; set; } = "DAWONDNS";

        [Property(PropertyType.STRING, required: false)]
        public string Model { get; set; } = "B540_W";

        [Property(PropertyType.STRING, required: false)]
        public string Lati { get; set; } = "";

        [Property(PropertyType.STRING, required: false)]
        public string Long { get; set; } = "";

        [Property(PropertyType.STRING, required: false)]
        public string Topic { get; set; } = "dwd";
    }
}
