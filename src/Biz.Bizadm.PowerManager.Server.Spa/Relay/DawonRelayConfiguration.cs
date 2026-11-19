using System.Configuration;
using System.Configuration.Annotation;

namespace Biz.Bizadm.PowerManager.Server.Spa.Relay
{
    /// <summary>
    /// 다원 서버 중계 모드 설정. 켜져 있으면 장치 REST 요청과 MQTT 접속을 다원 서버로 전달하고 양방향 원문을 JSONL로 기록한다.
    /// </summary>
    public sealed class DawonRelayConfiguration : IValidatableConfiguration
    {
        [Property(PropertyType.BOOL, DefaultValue = "false")]
        public bool? Enabled { get; set; }

        [Property(PropertyType.STRING, DefaultValue = "dwapi.dawonai.com")]
        public string ApiServerHost { get; set; } = null!;

        [Property(PropertyType.USHORT, DefaultValue = "18443")]
        public ushort? ApiServerPort { get; set; }

        [Property(PropertyType.STRING, DefaultValue = "dwmqtt.dawonai.com")]
        public string MqttServerHost { get; set; } = null!;

        [Property(PropertyType.USHORT, DefaultValue = "8883")]
        public ushort? MqttServerPort { get; set; }

        /// <summary>
        /// 기록 폴더. <see cref="AppConfiguration.ContentRootPath"/> 기준 상대 경로 또는 절대 경로.
        /// </summary>
        [Property(PropertyType.STRING, DefaultValue = "dawon-relay")]
        public string RecordDirectory { get; set; } = null!;

        /// <summary>
        /// 다원 서버(REST·MQTT) TLS 인증서 검증을 끈다.
        /// </summary>
        [Property(PropertyType.BOOL, DefaultValue = "false")]
        public bool? SkipCertificateValidation { get; set; }

        public bool IsEnabled => Enabled == true;

        public void Validate()
        {
            if (!IsEnabled)
                return;
            ArgumentException.ThrowIfNullOrWhiteSpace(ApiServerHost, nameof(ApiServerHost));
            ArgumentException.ThrowIfNullOrWhiteSpace(MqttServerHost, nameof(MqttServerHost));
            ArgumentException.ThrowIfNullOrWhiteSpace(RecordDirectory, nameof(RecordDirectory));
            if (ApiServerPort is null or 0)
                throw new ArgumentOutOfRangeException(nameof(ApiServerPort), ApiServerPort, "Port must be 1-65535.");
            if (MqttServerPort is null or 0)
                throw new ArgumentOutOfRangeException(nameof(MqttServerPort), MqttServerPort, "Port must be 1-65535.");
        }
    }
}
