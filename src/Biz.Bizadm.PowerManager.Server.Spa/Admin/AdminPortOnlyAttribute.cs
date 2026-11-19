using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Biz.Bizadm.PowerManager.Server.Spa.Admin
{
    /// <summary>
    /// 관리 API를 관리 포트(<see cref="AppConfiguration.HttpPort"/>·<see cref="AppConfiguration.HttpsPort"/>)에서만 처리한다.
    /// 그 밖의 포트(장치 포트 등)에서 처리되는 요청은 404 ProblemDetails로 끝낸다. 관리 API에는 아직 인증이 없으므로
    /// Host 헤더가 아니라 연결이 들어온 로컬 포트로 판정한다(Host 없는 요청이나 위조된 Host도 막힌다).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
    public sealed class AdminPortOnlyAttribute : Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            HttpContext httpContext = context.HttpContext;
            AppConfiguration configuration = httpContext.RequestServices.GetRequiredService<AppConfiguration>();
            int localPort = httpContext.Connection.LocalPort;
            if (localPort == configuration.HttpPort || localPort == configuration.HttpsPort)
                return;

            ProblemDetailsFactory factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
            ProblemDetails problem = factory.CreateProblemDetails(httpContext, StatusCodes.Status404NotFound);
            context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status404NotFound };
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }
}
