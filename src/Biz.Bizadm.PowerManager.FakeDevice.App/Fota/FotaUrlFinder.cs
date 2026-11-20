using System.Text.Json;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Fota
{
    /// <summary>
    /// 받은 페이로드에서 FOTA URL 후보를 고른다(spec C14). JSON의 객체·배열 안까지 모든 문자열 값을 돌며,
    /// <c>http://</c>·<c>https://</c>(대소문자 무시)로 시작하고 절대 URI인 값을 나온 순서대로 돌려준다. JSON이 아니면 빈 목록이다.
    /// </summary>
    internal static class FotaUrlFinder
    {
        public static IReadOnlyList<string> Find(ReadOnlyMemory<byte> payload)
        {
            List<string> urls = [];
            try
            {
                using JsonDocument document = JsonDocument.Parse(payload);
                Walk(document.RootElement, urls);
            }
            catch (JsonException)
            {
                return [];
            }
            return urls;
        }

        private static void Walk(JsonElement element, List<string> urls)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                        Walk(property.Value, urls);
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                        Walk(item, urls);
                    break;
                case JsonValueKind.String:
                    string value = element.GetString()!;
                    if (IsCandidate(value))
                        urls.Add(value);
                    break;
            }
        }

        private static bool IsCandidate(string value)
            => (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
               && Uri.TryCreate(value, UriKind.Absolute, out _);
    }
}
