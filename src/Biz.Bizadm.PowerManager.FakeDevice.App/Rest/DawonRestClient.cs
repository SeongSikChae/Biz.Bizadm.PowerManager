using Biz.Bizadm.PowerManager.Core.Devices;
using Biz.Bizadm.PowerManager.FakeDevice.App.Recording;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Rest
{
    /// <summary>
    /// 다원 REST 서버에 장치처럼 <c>register/create</c>(C2)·<c>device/connect</c>(C3)를 보낸다.
    /// 요청·응답·오류·verify 판정을 기록하고, 콘솔에는 상태 코드·판정만 쓴다(C10).
    /// </summary>
    internal sealed class DawonRestClient : IDisposable
    {
        public const string RegisterPath = "/api/v1/devices/register/create";
        public const string ConnectPathPrefix = "/api/v1/device/connect/";

        private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly FakeDeviceOptions options;
        private readonly FakeDeviceRecorder recorder;
        private readonly TextWriter output;
        private readonly ILogger logger;
        private readonly HttpClient client;
        private readonly string target;

        public DawonRestClient(FakeDeviceOptions options, FakeDeviceRecorder recorder, TextWriter output, ILogger logger)
        {
            this.options = options;
            this.recorder = recorder;
            this.output = output;
            this.logger = logger;
            target = $"https://{options.ApiServerHost}:{options.ApiServerPort}";
            SocketsHttpHandler handler = new SocketsHttpHandler { AllowAutoRedirect = false };
            if (options.SkipCertificateValidation)
                handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            client = new HttpClient(handler)
            {
                BaseAddress = new Uri(target),
                Timeout = Timeout.InfiniteTimeSpan,
            };
        }

        /// <summary>
        /// 등록 요청을 보내고 성공 조건(200, JSON 객체, 비어 있지 않은 최상위 문자열 <c>mqtt_key</c>)을 채우면 그 키를 돌려준다.
        /// 채우지 못하거나 연결 실패·시간 초과면 null이다.
        /// </summary>
        public async Task<string?> RegisterAsync(CancellationToken cancellationToken)
        {
            byte[] body = RegisterBody(options);
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, RegisterPath)
            {
                Content = new ByteArrayContent(body),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.TryAddWithoutValidation("X-HIT-Version", "1.0");

            (int Status, byte[] Body)? response = await SendAsync(request, RegisterPath, body, cancellationToken);
            if (response is not { } r)
                return null;

            string? mqttKey = null;
            string? verify = null;
            if (r.Status == 200)
                (mqttKey, verify) = ReadRegisterResponse(r.Body);
            if (mqttKey is null)
            {
                await output.WriteLineAsync($"register/create failed: status {r.Status}");
                return null;
            }

            if (verify is not null)
            {
                string result;
                try
                {
                    result = DeviceVerify.Compute(options.VerifyServerAddr, options.Mac, mqttKey) == verify ? "match" : "mismatch";
                }
                catch (ArgumentException)
                {
                    result = "uncomputable";
                }
                await recorder.RestVerifyAsync(result);
                await output.WriteLineAsync($"register/create verify: {result}");
            }
            return mqttKey;
        }

        /// <summary><c>GET /api/v1/device/connect/{장치 키}</c>. 결과와 관계없이 끝난다.</summary>
        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            string path = ConnectPathPrefix + options.DeviceKey;
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, path);
            await SendAsync(request, path, [], cancellationToken);
        }

        public static byte[] RegisterBody(FakeDeviceOptions options)
        {
            using MemoryStream buffer = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer, WriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteStartObject("account");
                writer.WriteString("user_id", options.UserId);
                writer.WriteEndObject();
                writer.WriteStartObject("device");
                writer.WriteString("mac", options.Mac);
                writer.WriteString("model_id", options.Model);
                writer.WriteString("company", options.Company);
                writer.WriteString("lati", options.Lati);
                writer.WriteString("long", options.Long);
                writer.WriteString("verify", "true");
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            return buffer.ToArray();
        }

        /// <summary>요청을 기록하고 보낸 뒤 응답을 기록한다. 연결 실패·시간 초과면 오류를 기록하고 null이다.</summary>
        private async Task<(int Status, byte[] Body)?> SendAsync(HttpRequestMessage request, string path, byte[] body, CancellationToken cancellationToken)
        {
            string method = request.Method.Method;
            string name = path.StartsWith(ConnectPathPrefix, StringComparison.Ordinal) ? "device/connect" : "register/create";
            await recorder.RestRequestAsync(method, path, body);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.RestTimeout);
            try
            {
                using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                byte[] responseBody = await response.Content.ReadAsByteArrayAsync(timeout.Token);
                int status = (int)response.StatusCode;
                await recorder.RestResponseAsync(method, path, status, responseBody);
                await output.WriteLineAsync($"{name}: status {status}");
                return (status, responseBody);
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException)
            {
                string error = ex is OperationCanceledException && !cancellationToken.IsCancellationRequested ? "timeout" : ex.Message;
                logger.LogWarning("Dawon REST {Name} failed: {Error}", name, error);
                await recorder.RestErrorAsync(method, path, target, error);
                await output.WriteLineAsync($"{name}: error {error}");
                return null;
            }
        }

        private static (string? MqttKey, string? Verify) ReadRegisterResponse(byte[] body)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return (null, null);
                string? key = document.RootElement.TryGetProperty("mqtt_key", out JsonElement k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
                string? verify = document.RootElement.TryGetProperty("verify", out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                return (string.IsNullOrEmpty(key) ? null : key, verify);
            }
            catch (JsonException)
            {
                return (null, null);
            }
        }

        public void Dispose() => client.Dispose();
    }
}
