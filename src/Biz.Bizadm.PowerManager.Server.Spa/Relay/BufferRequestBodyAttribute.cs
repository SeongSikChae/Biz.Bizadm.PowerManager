using Microsoft.AspNetCore.Mvc.Filters;

namespace Biz.Bizadm.PowerManager.Server.Spa.Relay
{
    /// <summary>
    /// 모델 바인딩 전에 요청 본문 버퍼링을 켜, 액션에서 본문을 되감아 원문 바이트로 다시 읽을 수 있게 한다.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class BufferRequestBodyAttribute : Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context) => context.HttpContext.Request.EnableBuffering();

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }
}
