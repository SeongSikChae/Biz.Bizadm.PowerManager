using Biz.Bizadm.PowerManager.Core.Devices;
using Microsoft.AspNetCore.Mvc;

namespace Biz.Bizadm.PowerManager.Server.Spa.Devices
{
    /// <summary>
    /// 장치 REST endpoint 껍데기. 요청을 로그로 남기고 501을 반환한다.
    /// </summary>
    [ApiController]
    public sealed class DevicesController(ILogger<DevicesController> logger) : ControllerBase
    {
        [HttpPost("/api/v1/devices/register/create")]
        public IActionResult Register([FromBody] DeviceRegisterRequest request)
        {
            logger.LogInformation("Device register requested: mac={Mac}, model={Model}, company={Company}",
                request.Device?.Mac, request.Device?.ModelId, request.Device?.Company);
            return Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Not implemented");
        }

        [HttpGet("/api/v1/device/connect/{deviceKey}")]
        public IActionResult Connect(string deviceKey)
        {
            logger.LogInformation("Device connect requested: {DeviceKey}", deviceKey);
            return Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Not implemented");
        }
    }
}
