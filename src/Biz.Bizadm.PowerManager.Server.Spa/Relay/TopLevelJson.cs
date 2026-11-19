using System.Text;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Relay
{
    /// <summary>
    /// 원문 JSON 바이트에서 최상위 객체 속성 값의 바이트 범위를 찾아 바꾼다. 나머지 바이트(공백·키 순서·이스케이프 형태)는 그대로 둔다.
    /// </summary>
    public static class TopLevelJson
    {
        private static readonly byte[] MaskedValue = Encoding.UTF8.GetBytes("\"***\"");

        /// <summary>
        /// 최상위 객체에서 <paramref name="name"/> 속성(첫 번째)의 값 범위를 찾는다. 범위는 문자열이면 따옴표를 포함하고,
        /// 객체·배열이면 끝 괄호까지다. 최상위가 객체가 아니거나 JSON이 올바르지 않으면 false.
        /// </summary>
        public static bool TryFindValue(ReadOnlySpan<byte> json, string name, out Range range, out JsonTokenType type)
        {
            range = default;
            type = JsonTokenType.None;
            try
            {
                Utf8JsonReader reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                    return false;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                        return false;
                    if (reader.TokenType != JsonTokenType.PropertyName)
                        return false;

                    bool match = reader.ValueTextEquals(name);
                    if (!reader.Read())
                        return false;
                    int start = (int)reader.TokenStartIndex;
                    JsonTokenType valueType = reader.TokenType;
                    if (valueType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                    {
                        if (!reader.TrySkip())
                            return false;
                    }
                    if (match)
                    {
                        range = new Range(start, (int)reader.BytesConsumed);
                        type = valueType;
                        return true;
                    }
                }
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// <paramref name="range"/>를 <paramref name="value"/>로 바꾼 새 배열을 돌려준다.
        /// </summary>
        public static byte[] Replace(ReadOnlySpan<byte> json, Range range, ReadOnlySpan<byte> value)
        {
            (int offset, int length) = range.GetOffsetAndLength(json.Length);
            byte[] result = new byte[json.Length - length + value.Length];
            json[..offset].CopyTo(result);
            value.CopyTo(result.AsSpan(offset));
            json[(offset + length)..].CopyTo(result.AsSpan(offset + value.Length));
            return result;
        }

        /// <summary>
        /// 최상위 객체의 <paramref name="names"/> 속성 값을 <c>"***"</c>로 바꾼다. JSON 객체가 아니면 원문을 그대로 돌려준다.
        /// </summary>
        public static byte[] Mask(ReadOnlySpan<byte> json, params string[] names)
        {
            byte[] current = json.ToArray();
            foreach (string name in names)
            {
                if (TryFindValue(current, name, out Range range, out _))
                    current = Replace(current, range, MaskedValue);
            }
            return current;
        }

        /// <summary>
        /// 이스케이프하지 않은 JSON 문자열 토큰(따옴표 포함). 값은 따옴표·역슬래시·제어 문자가 없는 문자열이어야 한다.
        /// </summary>
        public static byte[] RawStringToken(string value)
        {
            if (value.Any(c => c is '"' or '\\' || char.IsControl(c)))
                throw new ArgumentException("Value must not need escaping.", nameof(value));
            return Encoding.UTF8.GetBytes("\"" + value + "\"");
        }
    }
}
