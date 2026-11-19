using Biz.Bizadm.PowerManager.Core.Devices;
using Biz.Bizadm.PowerManager.Server.Spa.Devices;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Relay
{
    /// <summary>
    /// 중계 모드의 REST 처리. 장치 요청을 다원 REST 서버로 보내고 응답을 장치에 돌려주며, 양방향을 기록한다.
    /// 응답의 <c>mqtt_key</c>·<c>verify</c> 값은 로그에 남기지 않는다.
    /// </summary>
    public sealed class DawonRestRelay(IHttpClientFactory httpClientFactory, AppConfiguration configuration, DeviceRegistrationService registrations,
        DawonRelayRecorder recorder, ILogger<DawonRestRelay> logger)
    {
        public const string HttpClientName = "DawonRelay";
        public const string RegisterPath = "/api/v1/devices/register/create";
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private sealed record DawonResponse(int Status, string? ContentType, byte[] Body);

        /// <summary>
        /// 등록 요청(mac 검증을 마친 요청)을 다원으로 보낸다. 성공(200, 최상위 <c>mqtt_key</c>가 비어 있지 않은 치환표 문자열)이면
        /// 다원 키를 저장하고, 원문 응답에서 최상위 <c>verify</c> 문자열 값만 다시 계산한 값으로 바꿔 돌려준다.
        /// 그 밖에는 다원 응답을 그대로, 연결 실패·시간 초과는 502를 돌려준다.
        /// </summary>
        public async Task<IActionResult> RegisterAsync(string mac, DeviceRegisterRequest request, byte[] body, CancellationToken cancellationToken)
        {
            DawonResponse? response = await SendAsync(HttpMethod.Post, RegisterPath, body, cancellationToken);
            if (response is null)
                return BadGateway();

            if (response.Status == StatusCodes.Status200OK && TryGetMqttKey(response.Body, out string? mqttKey))
            {
                await registrations.StoreRelayedKeyAsync(mac, request.Account?.UserId, request.Device?.ModelId, request.Device?.Company, mqttKey, cancellationToken);
                byte[] result = response.Body;
                if (TopLevelJson.TryFindValue(result, "verify", out Range range, out JsonTokenType type) && type == JsonTokenType.String)
                {
                    string verify = DeviceVerify.Compute(configuration.DeviceMqttServerAddr, mac, mqttKey);
                    result = TopLevelJson.Replace(result, range, TopLevelJson.RawStringToken(verify));
                }
                logger.LogInformation("Dawon relay register stored: mac={Mac}", mac);
                return new RawResult(response.Status, response.ContentType, result);
            }

            logger.LogWarning("Dawon relay register not accepted: mac={Mac}, status={Status}", mac, response.Status);
            return new RawResult(response.Status, response.ContentType, response.Body);
        }

        /// <summary>
        /// 접속 조회를 다원의 같은 경로로 보내고 응답을 그대로 돌려준다. 연결 실패·시간 초과는 502.
        /// </summary>
        public async Task<IActionResult> ConnectAsync(string deviceKey, CancellationToken cancellationToken)
        {
            string path = "/api/v1/device/connect/" + Uri.EscapeDataString(deviceKey);
            DawonResponse? response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            return response is null ? BadGateway() : new RawResult(response.Status, response.ContentType, response.Body);
        }

        private static ObjectResult BadGateway()
            => new ObjectResult(new ProblemDetails { Status = StatusCodes.Status502BadGateway, Title = "Dawon server unavailable" })
            {
                StatusCode = StatusCodes.Status502BadGateway,
                ContentTypes = { "application/problem+json" },
            };

        /// <summary>
        /// 최상위 <c>mqtt_key</c>가 문자열이고 비어 있지 않으며 치환표 문자로만 이뤄졌으면 그 값을 돌려준다.
        /// </summary>
        private static bool TryGetMqttKey(byte[] body, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? mqttKey)
        {
            mqttKey = null;
            if (!TopLevelJson.TryFindValue(body, "mqtt_key", out Range range, out JsonTokenType type) || type != JsonTokenType.String)
                return false;
            Utf8JsonReader reader = new Utf8JsonReader(body.AsSpan(range));
            reader.Read();
            string? value = reader.GetString();
            if (value is null || !DeviceVerify.IsValidServerAddr(value))
                return false;
            mqttKey = value;
            return true;
        }

        private async Task<DawonResponse?> SendAsync(HttpMethod method, string path, byte[]? body, CancellationToken cancellationToken)
        {
            DawonRelayConfiguration relay = configuration.DawonRelay!;
            string target = $"{relay.ApiServerHost}:{relay.ApiServerPort}";
            await recorder.RestRequestAsync(method.Method, path, body ?? []);

            using HttpRequestMessage message = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                message.Content = new ByteArrayContent(body);
                message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                message.Headers.TryAddWithoutValidation("X-HIT-Version", "1.0");
            }

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            try
            {
                HttpClient client = httpClientFactory.CreateClient(HttpClientName);
                using HttpResponseMessage response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, timeout.Token);
                byte[] responseBody = await response.Content.ReadAsByteArrayAsync(timeout.Token);
                string? contentType = response.Content.Headers.ContentType?.ToString();
                await recorder.RestResponseAsync(method.Method, path, (int)response.StatusCode, responseBody);
                return new DawonResponse((int)response.StatusCode, contentType, responseBody);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                string error = ex is OperationCanceledException ? "timeout" : ex.Message;
                logger.LogWarning("Dawon relay request failed: {Method} {Path}, target={Target}, error={Error}", method.Method, path, target, error);
                await recorder.RestErrorAsync(method.Method, path, target, error);
                return null;
            }
        }

        /// <summary>
        /// 상태 코드·<c>Content-Type</c>·본문 바이트를 그대로 쓰는 응답.
        /// </summary>
        private sealed class RawResult(int status, string? contentType, byte[] body) : IActionResult
        {
            public async Task ExecuteResultAsync(ActionContext context)
            {
                HttpResponse response = context.HttpContext.Response;
                response.StatusCode = status;
                if (contentType is not null)
                    response.ContentType = contentType;
                response.ContentLength = body.Length;
                await response.Body.WriteAsync(body);
            }
        }
    }
}
