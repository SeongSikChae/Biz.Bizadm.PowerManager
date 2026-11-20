using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Recording
{
    /// <summary>FOTA 응답 항목의 필드(plan 기록 항목 표).</summary>
    internal sealed record FotaResponseRecord(
        int Id,
        string Url,
        int Status,
        string? ContentType,
        long? ContentLength,
        string? ContentEncoding,
        string? Location,
        long? Bytes,
        string? Sha256,
        string? File);

    /// <summary>
    /// 보내고 받은 주고받음을 기록 폴더의 UTC 날짜별 JSONL 파일(<c>dawon-fake-device-yyyyMMdd.jsonl</c>)에 한 줄씩 덧붙인다.
    /// 형식은 기준 spec dawon-fake-device R9를 따른다. 쓰기는 하나씩 직렬화하며, 실패해도 Error 로그만 남기고 계속한다.
    /// </summary>
    internal sealed class FakeDeviceRecorder(string directory, TimeProvider timeProvider, ILogger<FakeDeviceRecorder> logger)
    {
        public const string DeviceToDawon = "device-to-dawon";
        public const string DawonToDevice = "dawon-to-device";

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions { Indented = false };
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

        public string Directory => directory;

        public static string FileName(DateTimeOffset utc) => $"dawon-fake-device-{utc.UtcDateTime:yyyyMMdd}.jsonl";

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
                logger.LogError(ex, "Fake device record serialization failed: {Channel} {Kind}", channel, kind);
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
                logger.LogError(ex, "Fake device record write failed: {Channel} {Kind}", channel, kind);
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

        // ---------- REST ----------

        public Task RestRequestAsync(string method, string path, ReadOnlyMemory<byte> body)
            => WriteAsync("rest", DeviceToDawon, "request", w =>
            {
                w.WriteString("method", method);
                w.WriteString("path", path);
                WriteBytes(w, "body", "bodyEncoding", body.Span);
            });

        /// <summary>
        /// 다원 응답. 본문이 JSON 객체이면 최상위 <c>mqtt_key</c>·<c>verify</c> 값을 가린다(C10).
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

        /// <param name="result"><c>match</c>·<c>mismatch</c>·<c>uncomputable</c></param>
        public Task RestVerifyAsync(string result)
            => WriteAsync("rest", DawonToDevice, "verify", w => w.WriteString("result", result));

        // ---------- MQTT ----------

        /// <summary>접속 요청. 비밀번호는 있는지만 남긴다(C10).</summary>
        public Task MqttConnectAsync(string clientId, string userName, bool hasPassword, int keepAlive, bool cleanSession,
            string willTopic, ReadOnlyMemory<byte> willPayload, int willQos, bool willRetain)
            => WriteAsync("mqtt", DeviceToDawon, "connect", w =>
            {
                w.WriteString("clientId", clientId);
                w.WriteString("userName", userName);
                w.WriteBoolean("hasPassword", hasPassword);
                w.WriteNumber("keepAlive", keepAlive);
                w.WriteBoolean("cleanSession", cleanSession);
                w.WriteStartObject("will");
                w.WriteString("topic", willTopic);
                WriteBytes(w, "payload", "payloadEncoding", willPayload.Span);
                w.WriteNumber("qos", willQos);
                w.WriteBoolean("retain", willRetain);
                w.WriteEndObject();
            });

        public Task MqttConnAckAsync(string clientId, int returnCode)
            => WriteAsync("mqtt", DawonToDevice, "connack", w =>
            {
                w.WriteString("clientId", clientId);
                w.WriteNumber("returnCode", returnCode);
            });

        public Task MqttSubscribeAsync(string clientId, IReadOnlyList<(string Topic, int QoS)> filters)
            => WriteAsync("mqtt", DeviceToDawon, "subscribe", w =>
            {
                w.WriteString("clientId", clientId);
                w.WriteStartArray("filters");
                foreach ((string topic, int qos) in filters)
                {
                    w.WriteStartObject();
                    w.WriteString("topic", topic);
                    w.WriteNumber("qos", qos);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            });

        /// <param name="clientId">ClientId.</param>
        /// <param name="results">받은 결과 값(정수) 그대로.</param>
        public Task MqttSubAckAsync(string clientId, IReadOnlyList<int> results)
            => WriteAsync("mqtt", DawonToDevice, "suback", w =>
            {
                w.WriteString("clientId", clientId);
                w.WriteStartArray("results");
                foreach (int result in results)
                    w.WriteNumberValue(result);
                w.WriteEndArray();
            });

        public Task MqttPublishAsync(string direction, string clientId, string topic, int qos, bool retain, ReadOnlyMemory<byte> payload)
            => WriteAsync("mqtt", direction, "publish", w =>
            {
                w.WriteString("clientId", clientId);
                w.WriteString("topic", topic);
                w.WriteNumber("qos", qos);
                w.WriteBoolean("retain", retain);
                WriteBytes(w, "payload", "payloadEncoding", payload.Span);
            });

        /// <param name="direction">끝낸 쪽.</param>
        /// <param name="clientId">ClientId.</param>
        /// <param name="clean">정상 종료(DISCONNECT)면 true, 다원 쪽 끊김이면 false.</param>
        public Task MqttDisconnectAsync(string direction, string clientId, bool clean)
            => WriteAsync("mqtt", direction, "disconnect", w =>
            {
                w.WriteString("clientId", clientId);
                w.WriteBoolean("clean", clean);
            });

        public Task MqttErrorAsync(string clientId, string target, string error)
            => WriteAsync("mqtt", DawonToDevice, "error", w =>
            {
                w.WriteString("clientId", clientId);
                w.WriteString("target", target);
                w.WriteString("error", error);
            });

        // ---------- FOTA ----------

        public Task FotaCandidateAsync(string topic, string url)
            => WriteAsync("fota", DawonToDevice, "candidate", w =>
            {
                w.WriteString("topic", topic);
                w.WriteString("url", url);
            });

        /// <param name="id">요청 순번.</param>
        /// <param name="purpose"><c>image</c>(받은 URL + 슬롯 접미사)·<c>get</c>(입력 URL 그대로)</param>
        /// <param name="url">실제로 보낸 요청 URL.</param>
        public Task FotaRequestAsync(int id, string purpose, string url)
            => WriteAsync("fota", DeviceToDawon, "request", w =>
            {
                w.WriteNumber("id", id);
                w.WriteString("purpose", purpose);
                w.WriteString("url", url);
            });

        /// <summary>응답 항목. 응답 본문은 기록에 넣지 않고 200일 때 파일로만 둔다.</summary>
        public Task FotaResponseAsync(FotaResponseRecord response)
            => WriteAsync("fota", DawonToDevice, "response", w =>
            {
                w.WriteNumber("id", response.Id);
                w.WriteString("url", response.Url);
                w.WriteNumber("status", response.Status);
                WriteNullableString(w, "contentType", response.ContentType);
                if (response.ContentLength is { } length)
                    w.WriteNumber("contentLength", length);
                else
                    w.WriteNull("contentLength");
                WriteNullableString(w, "contentEncoding", response.ContentEncoding);
                WriteNullableString(w, "location", response.Location);
                if (response.Bytes is { } bytes)
                    w.WriteNumber("bytes", bytes);
                if (response.Sha256 is not null)
                    w.WriteString("sha256", response.Sha256);
                if (response.File is not null)
                    w.WriteString("file", response.File);
            });

        /// <param name="id">요청 순번.</param>
        /// <param name="url">요청 URL.</param>
        /// <param name="error"><c>timeout</c>·<c>too-large</c>·<c>cancelled</c> 또는 연결 오류 메시지</param>
        public Task FotaErrorAsync(int id, string url, string error)
            => WriteAsync("fota", DawonToDevice, "error", w =>
            {
                w.WriteNumber("id", id);
                w.WriteString("url", url);
                w.WriteString("error", error);
            });

        private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
        {
            if (value is null)
                writer.WriteNull(name);
            else
                writer.WriteString(name, value);
        }
    }
}
