using Biz.Bizadm.PowerManager.Core.Devices;
using System.Configuration;
using System.Configuration.Annotation;

namespace Biz.Bizadm.PowerManager.FakeDevice.App
{
    /// <summary>
    /// YAML 설정(키는 속성 이름 그대로, PascalCase). 필수 항목은 [Property(required)] + ConfigurationValidator,
    /// 범위·형식·조건부 필수는 <see cref="Validate"/>에서 검사한다(plan 인터페이스 절).
    /// </summary>
    internal sealed class AppConfiguration : IValidatableConfiguration
    {
        /// <summary>상대 경로의 기준. 비어 있으면 실행 폴더(<see cref="AppContext.BaseDirectory"/>)다.</summary>
        [Property(PropertyType.STRING, required: false)]
        public string? ContentRootPath { get; set; } = "";

        /// <summary>기록 폴더. 상대 경로면 기준 경로 아래다.</summary>
        [Property(PropertyType.STRING, required: false)]
        public string RecordDirectory { get; set; } = "dawon-fake-device";

        /// <summary>Register.MqttKey 또는 Mqtt.Password가 있을 때만 필수.</summary>
        public OsKmsSection? OsKms { get; set; }

        public DawonSection Dawon { get; set; } = new();

        public DeviceSection Device { get; set; } = new();

        public RegisterSection Register { get; set; } = new();

        public MqttSection Mqtt { get; set; } = new();

        public PublishSection Publish { get; set; } = new();

        public FotaSection Fota { get; set; } = new();

        /// <summary>보호할 비밀 값(<c>Register.MqttKey</c>·<c>Mqtt.Password</c>)이 하나라도 있는지.</summary>
        public bool HasSecrets() => !string.IsNullOrEmpty(Register?.MqttKey) || !string.IsNullOrEmpty(Mqtt?.Password);

        /// <summary><c>Register.VerifyServerAddr</c>에 기본값(<c>Dawon.MqttServerHost</c>)을 채운 값.</summary>
        public string EffectiveVerifyServerAddr()
            => string.IsNullOrEmpty(Register.VerifyServerAddr) ? Dawon.MqttServerHost : Register.VerifyServerAddr;

        public void Validate()
        {
            RecordDirectory ??= "";
            if (RecordDirectory.Length == 0)
                throw new ArgumentException("RecordDirectory must not be empty");

            Dawon ??= new DawonSection();
            ConfigurationValidator.Validate(Dawon);
            RequireText("Dawon.ApiServerHost", Dawon.ApiServerHost);
            RequireText("Dawon.MqttServerHost", Dawon.MqttServerHost);
            ValidatePort("Dawon.ApiServerPort", Dawon.ApiServerPort);
            ValidatePort("Dawon.MqttServerPort", Dawon.MqttServerPort);

            Device ??= new DeviceSection();
            ConfigurationValidator.Validate(Device);
            if (!DeviceIdentity.TryNormalizeMac(Device.Mac, out _))
                throw new ArgumentException($"Device.Mac must be 12 hexadecimal digits: {Device.Mac}");
            RequireTopicLevel("Device.Model", Device.Model);
            RequireTopicLevel("Device.Company", Device.Company);
            Device.UserId ??= "";
            Device.Lati ??= "";
            Device.Long ??= "";
            Device.FirmwareVersion ??= "";

            Register ??= new RegisterSection();
            ConfigurationValidator.Validate(Register);
            if (!DeviceVerify.IsValidServerAddr(EffectiveVerifyServerAddr()))
                throw new ArgumentException($"Register.VerifyServerAddr contains characters outside the verify alphabet: {EffectiveVerifyServerAddr()}");

            Mqtt ??= new MqttSection();
            ConfigurationValidator.Validate(Mqtt);
            if (Mqtt.KeepAliveSeconds is < 0 or > 65535)
                throw new ArgumentException($"Mqtt.KeepAliveSeconds out of range: {Mqtt.KeepAliveSeconds}");
            RequireTopicLevel("Mqtt.TopicPrefix", Mqtt.TopicPrefix);
            RequireTopicLevel("Mqtt.WillTopic", Mqtt.WillTopic);
            Mqtt.WillPayload ??= "";

            Publish ??= new PublishSection();
            ConfigurationValidator.Validate(Publish);
            RequireTopicLevel("Publish.RegisterSuffix", Publish.RegisterSuffix);
            if (Publish.QoS is < 0 or > 2)
                throw new ArgumentException($"Publish.QoS must be 0, 1 or 2: {Publish.QoS}");
            if (Publish.TimeSyncIntervalSeconds < 0)
                throw new ArgumentException($"Publish.TimeSyncIntervalSeconds must not be negative: {Publish.TimeSyncIntervalSeconds}");
            if (Publish.NotifyIntervalSeconds < 0)
                throw new ArgumentException($"Publish.NotifyIntervalSeconds must not be negative: {Publish.NotifyIntervalSeconds}");
            Publish.NotifyElements ??= [];
            for (int i = 0; i < Publish.NotifyElements.Count; i++)
            {
                NotifyElementSection element = Publish.NotifyElements[i] ?? throw new ArgumentException($"Publish.NotifyElements[{i}] must not be empty");
                if (string.IsNullOrEmpty(element.N))
                    throw new ArgumentException($"Publish.NotifyElements[{i}].N must not be empty");
                element.Sv ??= "";
            }

            Fota ??= new FotaSection();
            ConfigurationValidator.Validate(Fota);
            if (string.IsNullOrEmpty(Fota.UserAgent) || Fota.UserAgent.IndexOfAny(['\r', '\n']) >= 0)
                throw new ArgumentException("Fota.UserAgent must not be empty or contain CR/LF");

            if (!HasSecrets())
                return;
            if (OsKms is null)
                throw new ArgumentException("OsKms is required when Register.MqttKey or Mqtt.Password is set");
            ConfigurationValidator.Validate(OsKms);
            if (!Uri.TryCreate(OsKms.CredentialService, UriKind.Absolute, out _))
                throw new ArgumentException($"OsKms.CredentialService must be an absolute URI: {OsKms.CredentialService}");
        }

        private static void ValidatePort(string name, int port)
        {
            if (port is < 1 or > 65535)
                throw new ArgumentException($"{name} out of range: {port}");
        }

        private static void RequireText(string name, string? value)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException($"{name} must not be empty");
        }

        /// <summary>토픽에 들어가는 값. 비어 있지 않고 MQTT 와일드카드(<c>+</c>·<c>#</c>)가 없어야 한다.</summary>
        private static void RequireTopicLevel(string name, string? value)
        {
            RequireText(name, value);
            if (value!.IndexOfAny(['+', '#']) >= 0)
                throw new ArgumentException($"{name} must not contain MQTT wildcards: {value}");
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

        /// <summary>상대 경로면 기준 경로 아래다.</summary>
        [Property(PropertyType.STRING, required: true)]
        public string SecretsDekFilePath { get; set; } = null!;
    }

    internal sealed class DawonSection
    {
        [Property(PropertyType.STRING, required: false)]
        public string ApiServerHost { get; set; } = "dwapi.dawonai.com";

        [Property(PropertyType.INT, required: false)]
        public int ApiServerPort { get; set; } = 18443;

        [Property(PropertyType.STRING, required: false)]
        public string MqttServerHost { get; set; } = "dwmqtt.dawonai.com";

        [Property(PropertyType.INT, required: false)]
        public int MqttServerPort { get; set; } = 8883;

        /// <summary>REST·MQTT·FOTA의 TLS 인증서 검증을 끈다.</summary>
        [Property(PropertyType.BOOL, required: false)]
        public bool SkipCertificateValidation { get; set; }
    }

    internal sealed class DeviceSection
    {
        /// <summary>12자리 16진수. 소문자로 정규화한다.</summary>
        [Property(PropertyType.STRING, required: true)]
        public string Mac { get; set; } = null!;

        [Property(PropertyType.STRING, required: false)]
        public string Model { get; set; } = "B540_W";

        [Property(PropertyType.STRING, required: false)]
        public string Company { get; set; } = "DAWONDNS";

        [Property(PropertyType.STRING, required: false)]
        public string UserId { get; set; } = "";

        [Property(PropertyType.STRING, required: false)]
        public string Lati { get; set; } = "0";

        [Property(PropertyType.STRING, required: false)]
        public string Long { get; set; } = "0";

        [Property(PropertyType.STRING, required: false)]
        public string FirmwareVersion { get; set; } = "1.01.36";
    }

    internal sealed class RegisterSection
    {
        /// <summary>있으면 등록을 건너뛴다. 기동 시 OsKms로 암호화(DEKENC:)해 설정 파일에 다시 쓴다.</summary>
        [Property(PropertyType.STRING, required: false)]
        public string? MqttKey { get; set; } = "";

        /// <summary>verify 확인 서버 주소. 비어 있으면 Dawon.MqttServerHost.</summary>
        [Property(PropertyType.STRING, required: false)]
        public string? VerifyServerAddr { get; set; } = "";
    }

    internal sealed class MqttSection
    {
        /// <summary>비어 있으면 <c>{Company}-{mac}</c>.</summary>
        [Property(PropertyType.STRING, required: false)]
        public string? ClientId { get; set; } = "";

        /// <summary>비어 있으면 <c>{Company}-{Model}-{mac}</c>.</summary>
        [Property(PropertyType.STRING, required: false)]
        public string? UserName { get; set; } = "";

        /// <summary>비어 있으면 mqtt_key. 기동 시 OsKms로 암호화(DEKENC:)해 설정 파일에 다시 쓴다.</summary>
        [Property(PropertyType.STRING, required: false)]
        public string? Password { get; set; } = "";

        [Property(PropertyType.INT, required: false)]
        public int KeepAliveSeconds { get; set; } = 60;

        [Property(PropertyType.STRING, required: false)]
        public string TopicPrefix { get; set; } = "dwd";

        [Property(PropertyType.STRING, required: false)]
        public string WillTopic { get; set; } = "/lwt";

        [Property(PropertyType.STRING, required: false)]
        public string WillPayload { get; set; } = "offline";
    }

    internal sealed class PublishSection
    {
        [Property(PropertyType.BOOL, required: false)]
        public bool SendRegister { get; set; } = true;

        [Property(PropertyType.BOOL, required: false)]
        public bool SendTimeSync { get; set; } = true;

        [Property(PropertyType.STRING, required: false)]
        public string RegisterSuffix { get; set; } = "json";

        [Property(PropertyType.INT, required: false)]
        public int QoS { get; set; }

        /// <summary>0이면 접속 직후 한 번만.</summary>
        [Property(PropertyType.INT, required: false)]
        public int TimeSyncIntervalSeconds { get; set; }

        /// <summary>0이면 상태 보고를 보내지 않는다.</summary>
        [Property(PropertyType.INT, required: false)]
        public int NotifyIntervalSeconds { get; set; }

        public List<NotifyElementSection> NotifyElements { get; set; } = [new NotifyElementSection()];
    }

    internal sealed class NotifyElementSection
    {
        [Property(PropertyType.STRING, required: false)]
        public string N { get; set; } = "/100/0/31";

        [Property(PropertyType.STRING, required: false)]
        public string Sv { get; set; } = "false";
    }

    internal sealed class FotaSection
    {
        [Property(PropertyType.BOOL, required: false)]
        public bool AutoDownload { get; set; } = true;

        /// <summary>B540 펌웨어 덤프의 FOTA 요청 User-Agent(슬롯 A 0x5f594, 슬롯 B 0xdf6ec). 끝 공백은 뺐다(D013).</summary>
        [Property(PropertyType.STRING, required: false)]
        public string UserAgent { get; set; } = DefaultUserAgent;

        public const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 5.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/30.0.1599.101 Safari/537.36";
    }
}
