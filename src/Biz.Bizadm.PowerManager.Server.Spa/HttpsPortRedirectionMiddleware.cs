using Microsoft.AspNetCore.Http.Extensions;

namespace Biz.Bizadm.PowerManager.Server.Spa
{
    /// <summary>
    /// HTTP 포트로 들어온 요청을 대응 HTTPS 포트로 307 Redirect한다.
    /// <c>/fota</c> 세그먼트 경로와 Host 없는 요청은 Redirect하지 않는다.
    /// </summary>
    public sealed class HttpsPortRedirectionMiddleware(RequestDelegate next, IReadOnlyDictionary<int, int> portMap)
    {
        public Task InvokeAsync(HttpContext context)
        {
            HttpRequest request = context.Request;
            if (request.IsHttps || !portMap.TryGetValue(context.Connection.LocalPort, out int httpsPort))
                return next(context);

            // FOTA SDK는 기본 HTTP이고 HTTPS 지원이 미확인이라 /fota, /fota/... 경로는 HTTP로 처리한다.
            if (request.Path.StartsWithSegments("/fota", StringComparison.OrdinalIgnoreCase))
                return next(context);

            // Host 없는 HTTP/1.0 요청은 Location을 만들 수 없어 통과시킨다.
            if (!request.Host.HasValue)
                return next(context);

            // POST 본문을 보존하도록 307을 쓴다.
            context.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
            context.Response.Headers.Location =
                UriHelper.BuildAbsolute("https", new HostString(request.Host.Host, httpsPort), request.PathBase, request.Path, request.QueryString);
            return Task.CompletedTask;
        }
    }
}
