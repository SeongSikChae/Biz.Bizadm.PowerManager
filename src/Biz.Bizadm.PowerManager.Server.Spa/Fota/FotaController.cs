using Microsoft.AspNetCore.Mvc;

namespace Biz.Bizadm.PowerManager.Server.Spa.Fota
{
    /// <summary>
    /// FOTA endpoint 껍데기. 끝이 <c>/version</c>이면 버전 조회, 그 외는 이미지 다운로드로 보고 로그 후 501을 반환한다.
    /// HEAD도 받는다(GET만 두면 운영 모드에서 확장자 없는 경로의 HEAD가 SPA fallback으로 가 404가 된다).
    /// </summary>
    [ApiController]
    public sealed class FotaController(ILogger<FotaController> logger) : ControllerBase
    {
        [HttpGet("/fota/{**path}"), HttpHead("/fota/{**path}")]
        public IActionResult Get(string? path)
        {
            path ??= string.Empty;
            // 라우팅·Redirect 제외와 같이 대소문자를 무시한다.
            bool isVersion = path.EndsWith("/version", StringComparison.OrdinalIgnoreCase)
                || path.Equals("version", StringComparison.OrdinalIgnoreCase);
            logger.LogInformation("FOTA {Kind} requested: {Path}", isVersion ? "version" : "image", path);
            return Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Not implemented");
        }
    }
}
