using System.Text;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Relay
{
    /// <summary>
    /// 중계한 주고받음을 기록 폴더의 UTC 날짜별 JSONL 파일(<c>dawon-relay-yyyyMMdd.jsonl</c>)에 한 줄씩 덧붙인다.
    /// 쓰기는 하나씩 직렬화하며, 실패해도 Error 로그만 남기고 중계는 계속한다.
    /// </summary>
    public sealed class DawonRelayRecorder(string directory, TimeProvider timeProvider, ILogger<DawonRelayRecorder> logger)
    {
        public const string DeviceToDawon = "device-to-dawon";
        public const string DawonToDevice = "dawon-to-device";

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions { Indented = false };
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

        public string Directory => directory;

        public static string FileName(DateTimeOffset utc) => $"dawon-relay-{utc.UtcDateTime:yyyyMMdd}.jsonl";

        /// <summary>
        /// 공통 필드(<c>ts</c>·<c>channel</c>·<c>direction</c>·<c>kind</c>)를 쓴 뒤 <paramref name="fields"/>로 나머지 필드를 쓴다.
        /// </summary>
        public async Task WriteAsync(string channel, string direction, string kind, Action<Utf8JsonWriter> fields)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            byte[] line;
            try
            {
                using MemoryStream buffer = new MemoryStream();
                using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer, WriterOptions))
                {
                    writer.WriteStartObject();
                    writer.WriteString("ts", now.ToString("O"));
                    writer.WriteString("channel", channel);
                    writer.WriteString("direction", direction);
                    writer.WriteString("kind", kind);
                    fields(writer);
                    writer.WriteEndObject();
                }
                buffer.WriteByte((byte)'\n');
                line = buffer.ToArray();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dawon relay record serialization failed: {Channel} {Kind}", channel, kind);
                return;
            }

            await gate.WaitAsync();
            try
            {
                System.IO.Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, FileName(now));
                await using FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                await stream.WriteAsync(line);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dawon relay record write failed: {Channel} {Kind}", channel, kind);
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// 바이트를 <c>{name}</c>(UTF-8 문자열 또는 Base64)와 <c>{encodingName}</c>(<c>utf8</c>·<c>base64</c>)으로 쓴다. 자르지 않는다.
        /// </summary>
        public static void WriteBytes(Utf8JsonWriter writer, string name, string encodingName, ReadOnlySpan<byte> bytes)
        {
            string text;
            string encoding;
            try
            {
                text = StrictUtf8.GetString(bytes);
                encoding = "utf8";
            }
            catch (DecoderFallbackException)
            {
                text = Convert.ToBase64String(bytes);
                encoding = "base64";
            }
            writer.WriteString(name, text);
            writer.WriteString(encodingName, encoding);
        }

        public Task RestRequestAsync(string method, string path, ReadOnlyMemory<byte> body)
            => WriteAsync("rest", DeviceToDawon, "request", w =>
            {
                w.WriteString("method", method);
                w.WriteString("path", path);
                WriteBytes(w, "body", "bodyEncoding", body.Span);
            });

        /// <summary>
        /// 다원 응답. 본문이 JSON 객체이면 최상위 <c>mqtt_key</c>·<c>verify</c> 값을 가린다.
        /// </summary>
        public Task RestResponseAsync(string method, string path, int status, ReadOnlyMemory<byte> body)
            => WriteAsync("rest", DawonToDevice, "response", w =>
            {
                w.WriteString("method", method);
                w.WriteString("path", path);
                w.WriteNumber("status", status);
                WriteBytes(w, "body", "bodyEncoding", TopLevelJson.Mask(body.Span, "mqtt_key", "verify"));
            });

        public Task RestErrorAsync(string method, string path, string target, string error)
            => WriteAsync("rest", DawonToDevice, "error", w =>
            {
                w.WriteString("method", method);
                w.WriteString("path", path);
                w.WriteString("target", target);
                w.WriteString("error", error);
            });
    }
}
