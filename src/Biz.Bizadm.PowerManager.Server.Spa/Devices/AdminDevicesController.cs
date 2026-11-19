using Biz.Bizadm.PowerManager.Server.Spa.Admin;
using Biz.Bizadm.PowerManager.Server.Spa.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biz.Bizadm.PowerManager.Server.Spa.Devices
{
    /// <summary>
    /// 관리 화면용 장비 조회 API. 관리 포트에서만 처리한다(<see cref="AdminPortOnlyAttribute"/>).
    /// </summary>
    [ApiController]
    [AdminPortOnly]
    public sealed class AdminDevicesController(IDbContextFactory<PowerManagerDbContext> factory) : ControllerBase
    {
        /// <summary>
        /// 저장된 장비 전체를 마지막 요청이 최근인 순서(같으면 mac 오름차순)로 반환한다.
        /// </summary>
        [HttpGet("/api/admin/devices")]
        public async Task<IReadOnlyList<AdminDeviceResponse>> List(CancellationToken cancellationToken)
        {
            await using PowerManagerDbContext db = await factory.CreateDbContextAsync(cancellationToken);
            List<DeviceRegistration> devices = await db.Devices.AsNoTracking()
                .OrderByDescending(d => d.LastRequestedAt)
                .ThenBy(d => d.Mac)
                .ToListAsync(cancellationToken);
            return devices
                .Select(d => new AdminDeviceResponse(d.Mac, d.UserId, d.ModelId, d.Company, d.RegisteredAt, d.LastRequestedAt))
                .ToList();
        }
    }
}
