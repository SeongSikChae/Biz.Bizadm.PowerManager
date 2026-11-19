using Biz.Bizadm.PowerManager.Core.Devices;
using Biz.Bizadm.PowerManager.Server.Core;
using System.Configuration;
using System.Configuration.Annotation;

namespace Biz.Bizadm.PowerManager.Server.Spa
{
    public sealed class AppConfiguration : IValidatableConfiguration
    {
        [Property(PropertyType.STRING, required: true)]
        public string ContentRootPath { get; set; } = null!;

        /// <summary>
        /// SPA 빌드 결과 폴더. <see cref="ContentRootPath"/> 기준 상대 경로.
        /// </summary>
        [Property(PropertyType.STRING, DefaultValue = "ClientApp")]
        public string WebRootPath { get; set; } = null!;

        /// <summary>
        /// HTTP 포트. TLS 사용 시 <see cref="HttpsPort"/>로 Redirect한다(<c>/fota</c> 제외).
        /// </summary>
        [Property(PropertyType.USHORT, DefaultValue = "80")]
        public ushort? HttpPort { get; set; }

        [Property(PropertyType.USHORT, DefaultValue = "443")]
        public ushort? HttpsPort { get; set; }

        /// <summary>
        /// 장치용 HTTP 포트. TLS 사용 시 <see cref="DeviceHttpsPort"/>로 Redirect한다(<c>/fota</c> 제외).
        /// </summary>
        [Property(PropertyType.USHORT, DefaultValue = "18446")]
        public ushort? DeviceHttpPort { get; set; }

        [Property(PropertyType.USHORT, DefaultValue = "18443")]
        public ushort? DeviceHttpsPort { get; set; }

        [Property(PropertyType.USHORT, DefaultValue = "1803")]
        public ushort? MqttPort { get; set; }

        [Property(PropertyType.USHORT, DefaultValue = "8883")]
        public ushort? MqttsPort { get; set; }

        /// <summary>
        /// 장치에 설정된 MQTT 서버 주소(장치 설정 <c>server_addr</c>). 등록 응답 <c>verify</c> 계산에 쓰므로 장치 값과 같아야 하며,
        /// 치환표(<see cref="DeviceVerify.Alphabet"/>) 문자로만 이뤄져야 한다.
        /// </summary>
        [Property(PropertyType.STRING, DefaultValue = "dwmqtt.dawonai.com")]
        public string DeviceMqttServerAddr { get; set; } = null!;

        /// <summary>
        /// SQLite DB 파일. <see cref="ContentRootPath"/> 기준 상대 경로 또는 절대 경로.
        /// </summary>
        [Property(PropertyType.STRING, DefaultValue = "powermanager.db")]
        public string DatabasePath { get; set; } = null!;

        public OsKmsConfiguration OsKms { get; set; } = null!;

        public TlsConfiguration? Tls { get; set; }

        public void Validate()
        {
            if (!DeviceVerify.IsValidServerAddr(DeviceMqttServerAddr))
                throw new ArgumentException($"DeviceMqttServerAddr must be non-empty and use only these characters: {DeviceVerify.Alphabet}", nameof(DeviceMqttServerAddr));
            ArgumentNullException.ThrowIfNull(OsKms, nameof(OsKms));
            ConfigurationValidator.Validate(OsKms);
            if (Tls is not null)
                ConfigurationValidator.Validate(Tls);
        }
    }
}
