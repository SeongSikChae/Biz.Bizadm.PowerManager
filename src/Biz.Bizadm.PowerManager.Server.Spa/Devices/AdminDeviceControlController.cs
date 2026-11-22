using Biz.Bizadm.PowerManager.Server.Spa.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Biz.Bizadm.PowerManager.Server.Spa.Devices
{
    /// <summary>
    /// 장비 제어 API. 관리 포트에서만 처리한다(<see cref="AdminPortOnlyAttribute"/>).
    /// 204는 그 장비 클라이언트의 전송 큐에 메시지를 넣었다는 뜻이고, 장치가 받았거나 처리했다는 뜻이 아니다.
    /// </summary>
    [ApiController]
    [AdminPortOnly]
    public sealed class AdminDeviceControlController(DeviceCommandService commands) : ControllerBase
    {
        [HttpPost("/api/admin/devices/{mac}/relay")]
        public async Task<IActionResult> Relay(string mac, [FromBody] RelayRequest request, CancellationToken cancellationToken)
            => ToResult(await commands.SetRelayAsync(mac, request, cancellationToken));

        [HttpPost("/api/admin/devices/{mac}/power-cycle")]
        public async Task<IActionResult> PowerCycle(string mac, [FromBody] PowerCycleRequest request, CancellationToken cancellationToken)
            => ToResult(await commands.PowerCycleAsync(mac, request, cancellationToken));

        [HttpPost("/api/admin/devices/{mac}/reboot")]
        public async Task<IActionResult> Reboot(string mac, CancellationToken cancellationToken)
            => ToResult(await commands.RebootAsync(mac, cancellationToken));

        [HttpPut("/api/admin/devices/{mac}/report-condition")]
        public async Task<IActionResult> ReportCondition(string mac, [FromBody] ReportConditionRequest request, CancellationToken cancellationToken)
            => ToResult(await commands.SetReportConditionAsync(mac, request, cancellationToken));

        [HttpPut("/api/admin/devices/{mac}/standby-cutoff")]
        public async Task<IActionResult> StandbyCutoff(string mac, [FromBody] StandbyCutoffRequest request, CancellationToken cancellationToken)
            => ToResult(await commands.SetStandbyCutoffAsync(mac, request, cancellationToken));

        [HttpPut("/api/admin/devices/{mac}/schedule")]
        public async Task<IActionResult> Schedule(string mac, [FromBody] ScheduleRequest request, CancellationToken cancellationToken)
            => ToResult(await commands.SetScheduleAsync(mac, request, cancellationToken));

        [HttpDelete("/api/admin/devices/{mac}/schedule")]
        public async Task<IActionResult> DeleteSchedule(string mac, CancellationToken cancellationToken)
            => ToResult(await commands.DeleteScheduleAsync(mac, cancellationToken));

        private IActionResult ToResult(DeviceCommandResult result)
            => result == DeviceCommandResult.Success ? NoContent() : Problem(statusCode: result.StatusCode, title: result.Title);
    }
}
