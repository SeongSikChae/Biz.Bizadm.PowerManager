using Biz.Bizadm.PowerManager.Core.Devices;
using Biz.Bizadm.PowerManager.FakeDevice.App.Security;
using System.Text;

namespace Biz.Bizadm.PowerManager.FakeDevice.App
{
    /// <summary>상태 보고 요소 하나(<c>n</c>·<c>sv</c>).</summary>
    internal sealed record NotifyElement(string N, string Sv);

    /// <summary>
    /// 검사·복호화를 마친 실행 값. 기본값을 채운 ClientId·사용자명·verify 서버 주소, 정규화한 mac, 기준 경로로 푼 기록 폴더를 담는다.
    /// 시간 제한과 FOTA 크기 상한은 spec 고정값이며 테스트에서만 바꾼다.
    /// </summary>
    internal sealed record FakeDeviceOptions
    {
        public required string ApiServerHost { get; init; }
        public required int ApiServerPort { get; init; }
        public required string MqttServerHost { get; init; }
        public required int MqttServerPort { get; init; }
        public bool SkipCertificateValidation { get; init; }

        /// <summary>소문자 12자리.</summary>
        public required string Mac { get; init; }
        public required string Model { get; init; }
        public required string Company { get; init; }
        public string UserId { get; init; } = "";
        public string Lati { get; init; } = "0";
        public string Long { get; init; } = "0";
        public string FirmwareVersion { get; init; } = "1.01.36";

        /// <summary>설정의 mqtt_key(복호화 값). 없으면 등록으로 받는다.</summary>
        public string? MqttKey { get; init; }
        public required string VerifyServerAddr { get; init; }

        public required string ClientId { get; init; }
        public required string UserName { get; init; }

        /// <summary>설정의 비밀번호(복호화 값). 없으면 mqtt_key를 쓴다.</summary>
        public string? Password { get; init; }
        public int KeepAliveSeconds { get; init; } = 60;
        public string TopicPrefix { get; init; } = "dwd";
        public string WillTopic { get; init; } = "/lwt";
        public string WillPayload { get; init; } = "offline";

        public bool SendRegister { get; init; } = true;
        public bool SendTimeSync { get; init; } = true;
        public string RegisterSuffix { get; init; } = "json";
        public int QoS { get; init; }
        public TimeSpan TimeSyncInterval { get; init; }
        public TimeSpan NotifyInterval { get; init; }
        public IReadOnlyList<NotifyElement> NotifyElements { get; init; } = [];

        public bool FotaAutoDownload { get; init; } = true;
        public string UserAgent { get; init; } = FotaSection.DefaultUserAgent;

        /// <summary>절대 경로.</summary>
        public required string RecordDirectory { get; init; }

        public TimeSpan RestTimeout { get; init; } = TimeSpan.FromSeconds(10);
        public TimeSpan MqttTimeout { get; init; } = TimeSpan.FromSeconds(10);
        public TimeSpan FotaTimeout { get; init; } = TimeSpan.FromSeconds(60);
        public long FotaMaxBytes { get; init; } = 4 * 1024 * 1024;

        /// <summary>장치 키 <c>{회사}-{모델}-{mac}</c>.</summary>
        public string DeviceKey => $"{Company}-{Model}-{Mac}";

        /// <summary>
        /// <paramref name="secrets"/>는 <see cref="ConfigSecretProtector"/>가 복호화한 값이다. YAML 모델의 비밀 값(기동 후 암호문)은 쓰지 않는다.
        /// </summary>
        /// <param name="basePath">상대 기록 폴더의 기준(D025).</param>
        public static FakeDeviceOptions Create(AppConfiguration config, ConfigSecrets secrets, string basePath)
        {
            if (!DeviceIdentity.TryNormalizeMac(config.Device.Mac, out string? mac))
                throw new ArgumentException($"Device.Mac must be 12 hexadecimal digits: {config.Device.Mac}");
            string company = config.Device.Company;
            string model = config.Device.Model;
            return new FakeDeviceOptions
            {
                ApiServerHost = config.Dawon.ApiServerHost,
                ApiServerPort = config.Dawon.ApiServerPort,
                MqttServerHost = config.Dawon.MqttServerHost,
                MqttServerPort = config.Dawon.MqttServerPort,
                SkipCertificateValidation = config.Dawon.SkipCertificateValidation,
                Mac = mac,
                Model = model,
                Company = company,
                UserId = config.Device.UserId,
                Lati = config.Device.Lati,
                Long = config.Device.Long,
                FirmwareVersion = config.Device.FirmwareVersion,
                MqttKey = secrets.MqttKey,
                VerifyServerAddr = config.EffectiveVerifyServerAddr(),
                ClientId = string.IsNullOrEmpty(config.Mqtt.ClientId) ? $"{company}-{mac}" : config.Mqtt.ClientId,
                UserName = string.IsNullOrEmpty(config.Mqtt.UserName) ? $"{company}-{model}-{mac}" : config.Mqtt.UserName,
                Password = secrets.Password,
                KeepAliveSeconds = config.Mqtt.KeepAliveSeconds,
                TopicPrefix = config.Mqtt.TopicPrefix,
                WillTopic = config.Mqtt.WillTopic,
                WillPayload = config.Mqtt.WillPayload,
                SendRegister = config.Publish.SendRegister,
                SendTimeSync = config.Publish.SendTimeSync,
                RegisterSuffix = config.Publish.RegisterSuffix,
                QoS = config.Publish.QoS,
                TimeSyncInterval = TimeSpan.FromSeconds(config.Publish.TimeSyncIntervalSeconds),
                NotifyInterval = TimeSpan.FromSeconds(config.Publish.NotifyIntervalSeconds),
                NotifyElements = config.Publish.NotifyElements.Select(e => new NotifyElement(e.N, e.Sv)).ToList(),
                FotaAutoDownload = config.Fota.AutoDownload,
                UserAgent = config.Fota.UserAgent,
                RecordDirectory = Path.GetFullPath(Path.Combine(basePath, config.RecordDirectory)),
            };
        }

        // record 기본 ToString이 비밀 값을 내지 않도록 키·비밀번호는 있는지만 쓴다(C10)
        private bool PrintMembers(StringBuilder builder)
        {
            builder.Append($"ApiServer = {ApiServerHost}:{ApiServerPort}, MqttServer = {MqttServerHost}:{MqttServerPort}, ");
            builder.Append($"DeviceKey = {DeviceKey}, ClientId = {ClientId}, UserName = {UserName}, ");
            builder.Append($"MqttKey = {(MqttKey is null ? "null" : "***")}, Password = {(Password is null ? "null" : "***")}, ");
            builder.Append($"RecordDirectory = {RecordDirectory}");
            return true;
        }
    }
}
