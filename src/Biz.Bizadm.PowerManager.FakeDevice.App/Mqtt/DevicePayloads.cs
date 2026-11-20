using System.Text.Encodings.Web;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Mqtt
{
    /// <summary>
    /// 장치가 보내는 <c>rg</c>·<c>tisyn</c>·<c>n</c> 페이로드(spec C6). 공백 없는 한 줄 JSON이고 키 순서가 고정이다.
    /// 값은 이스케이프를 최소로 해 설정값 그대로 보낸다.
    /// </summary>
    internal static class DevicePayloads
    {
        private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public const string TimeSyncSuffix = "timesync/json";
        public const string NotifySuffix = "notify/json";

        public static byte[] Register(string company, string model, string mac, string firmwareVersion)
            => Write("0", "rg", w =>
            {
                Element(w, "/3/0/0", company);
                Element(w, "/3/0/1", model);
                Element(w, "/3/0/2", mac);
                Element(w, "/3/0/3", firmwareVersion);
                Element(w, "/3/0/997", "1");
                Element(w, "/3/0/998", "1.0");
                Element(w, "/3/0/999", "1");
            });

        public static byte[] TimeSync() => Write("0", "tisyn", _ => { });

        public static byte[] Notify(IEnumerable<NotifyElement> elements)
            => Write("1", "n", w =>
            {
                foreach (NotifyElement element in elements)
                {
                    w.WriteStartObject();
                    w.WriteString("n", element.N);
                    w.WriteString("sv", element.Sv);
                    w.WriteString("ti", "");
                    w.WriteEndObject();
                }
            });

        private static void Element(Utf8JsonWriter writer, string n, string sv)
        {
            writer.WriteStartObject();
            writer.WriteString("n", n);
            writer.WriteString("sv", sv);
            writer.WriteEndObject();
        }

        private static byte[] Write(string sid, string operation, Action<Utf8JsonWriter> elements)
        {
            using MemoryStream buffer = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer, WriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteString("sid", sid);
                writer.WriteStartObject("msg");
                writer.WriteString("o", operation);
                writer.WriteStartArray("e");
                elements(writer);
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            return buffer.ToArray();
        }
    }
}
