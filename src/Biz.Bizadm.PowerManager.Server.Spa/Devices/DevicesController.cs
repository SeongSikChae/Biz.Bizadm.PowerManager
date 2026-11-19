using Biz.Bizadm.PowerManager.Core.Devices;
using Biz.Bizadm.PowerManager.Server.Spa.Relay;
using Microsoft.AspNetCore.Mvc;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Devices
{
    /// <summary>
    /// 장치 등록·접속 조회 REST endpoint. 응답의 <c>mqtt_key</c>·<c>verify</c> 값은 로그에 남기지 않는다.
    /// </summary>
    [ApiController]
    public sealed class DevicesController(AppConfiguration configuration, DeviceRegistrationService registrations, ILogger<DevicesController> logger)
        : ControllerBase
    {
        [HttpPost("/api/v1/devices/register/create")]
        [BufferRequestBody]
        public async Task<IActionResult> Register([FromBody] DeviceRegisterRequest request, CancellationToken cancellationToken)
        {
            if (!DeviceIdentity.TryNormalizeMac(request.Device?.Mac, out string? mac))
            {
                logger.LogWarning("Device register rejected: invalid mac {Mac}", request.Device?.Mac);
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid mac");
            }

            if (RestRelay is DawonRestRelay relay)
            {
                // 중계 모드: 장치가 보낸 원문 바이트를 그대로 다원으로 보낸다(바인딩 전에 버퍼링해 두었다).
                Request.Body.Position = 0;
                using MemoryStream raw = new MemoryStream();
                await Request.Body.CopyToAsync(raw, cancellationToken);
                return await relay.RegisterAsync(mac, request, raw.ToArray(), cancellationToken);
            }

            DeviceRegistrationService.Result result = await registrations.RegisterAsync(
                mac, request.Account?.UserId, request.Device?.ModelId, request.Device?.Company, cancellationToken);
            string verify = DeviceVerify.Compute(configuration.DeviceMqttServerAddr, mac, result.MqttKey);

            logger.LogInformation("Device registered: mac={Mac}, model={Model}, company={Company}, created={Created}",
                mac, request.Device?.ModelId, request.Device?.Company, result.Created);
            return new JsonResult(new DeviceRegisterResponse(verify, result.MqttKey), RegisterJsonOptions);
        }

        /// <summary>
        /// verify 치환표에는 <c>+</c>가 있는데 기본 인코더는 이를 유니코드 이스케이프(백슬래시 + <c>u002B</c>)로 바꾼다. 장치 키 해석 함수가
        /// 이스케이프를 푼다는 근거가 없으므로 등록 응답은 문자를 그대로 보낸다(값은 모두 ASCII 치환표 문자).
        /// </summary>
        /// <summary>
        /// 중계 모드가 켜져 있을 때만 등록되는 REST 중계. 꺼져 있으면 null이라 지금 처리 경로를 탄다.
        /// </summary>
        private DawonRestRelay? RestRelay
            => configuration.DawonRelay?.IsEnabled == true ? HttpContext.RequestServices.GetService<DawonRestRelay>() : null;

        private static readonly JsonSerializerOptions RegisterJsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        [HttpGet("/api/v1/device/connect/{deviceKey}")]
        public async Task<IActionResult> Connect(string deviceKey, CancellationToken cancellationToken)
        {
            if (RestRelay is DawonRestRelay relay)
                return await relay.ConnectAsync(deviceKey, cancellationToken);

            if (DeviceIdentity.TryParseDeviceKey(deviceKey, out string? mac)
                && await registrations.FindMqttKeyAsync(mac, cancellationToken) is not null)
            {
                logger.LogInformation("Device connect: {DeviceKey}", deviceKey);
                return Ok(new DeviceConnectResponse("0"));
            }

            logger.LogWarning("Device connect not found: {DeviceKey}", deviceKey);
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Device not found");
        }
    }
}
