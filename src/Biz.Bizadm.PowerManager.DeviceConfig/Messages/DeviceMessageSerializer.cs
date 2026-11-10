using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Messages
{
    internal static partial class DeviceMessageSerializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // 장치 파서가 \uXXXX 이스케이프를 처리한다는 근거가 없으므로 최소 이스케이프만 한다
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static string Serialize<T>(T message) => JsonSerializer.Serialize(message, Options);

        /// <summary>
        /// 로그용: pass, mqtt_key 값을 ***로 바꾼다.
        /// </summary>
        public static string Mask(string json) => SecretRegex().Replace(json, "\"$1\":\"***\"");

        [GeneratedRegex("\"(pass|mqtt_key)\":\"(?:[^\"\\\\]|\\\\.)*\"")]
        private static partial Regex SecretRegex();
    }
}
