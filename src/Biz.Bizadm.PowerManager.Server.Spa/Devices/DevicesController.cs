using Biz.Bizadm.PowerManager.Core.Devices;
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
        public async Task<IActionResult> Register([FromBody] DeviceRegisterRequest request, CancellationToken cancellationToken)
        {
            if (!DeviceIdentity.TryNormalizeMac(request.Device?.Mac, out string? mac))
            {
                logger.LogWarning("Device register rejected: invalid mac {Mac}", request.Device?.Mac);
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid mac");
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
        private static readonly JsonSerializerOptions RegisterJsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        [HttpGet("/api/v1/device/connect/{deviceKey}")]
        public async Task<IActionResult> Connect(string deviceKey, CancellationToken cancellationToken)
        {
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
