using Biz.Bizadm.PowerManager.Server.Spa.Data;
using Microsoft.EntityFrameworkCore;

namespace Biz.Bizadm.PowerManager.Server.Spa.Devices
{
    /// <summary>
    /// DB 등록 정보와 <see cref="DeviceStateStore"/>의 상태를 합쳐 관리 화면용 상태 원소를 만든다.
    /// </summary>
    public sealed class DeviceStatusService(IDbContextFactory<PowerManagerDbContext> factory, DeviceStateStore store)
    {
        /// <summary>
        /// 등록된 장비 전체를 mac 오름차순으로 반환한다.
        /// </summary>
        public async Task<IReadOnlyList<DeviceStatusResponse>> ListAsync(CancellationToken cancellationToken)
        {
            await using PowerManagerDbContext db = await factory.CreateDbContextAsync(cancellationToken);
            var devices = await db.Devices.AsNoTracking()
                .OrderBy(d => d.Mac)
                .Select(d => new { d.Mac, d.Name, d.ModelId })
                .ToListAsync(cancellationToken);
            return devices.Select(d => Create(d.Mac, d.Name, d.ModelId)).ToList();
        }

        /// <summary>
        /// 등록되지 않은 mac이면 null.
        /// </summary>
        public async Task<DeviceStatusResponse?> FindAsync(string mac, CancellationToken cancellationToken)
        {
            await using PowerManagerDbContext db = await factory.CreateDbContextAsync(cancellationToken);
            var device = await db.Devices.AsNoTracking()
                .Where(d => d.Mac == mac)
                .Select(d => new { d.Name, d.ModelId })
                .SingleOrDefaultAsync(cancellationToken);
            return device is null ? null : Create(mac, device.Name, device.ModelId);
        }

        /// <summary>
        /// 상태 원소가 바뀐 장비의 mac 알림을 구독한다.
        /// </summary>
        public DeviceChangeSubscription Subscribe() => store.Subscribe();

        private DeviceStatusResponse Create(string mac, string name, string modelId)
        {
            DeviceState state = store.Get(mac);
            string relay = state.Relay switch
            {
                RelayState.On => "on",
                RelayState.Off => "off",
                _ => "unknown",
            };
            return new DeviceStatusResponse(mac, name, modelId, state.Online, relay, state.Voltage, state.Current, state.PowerFactor,
                state.Power, state.LastReportAt, state.Controllable);
        }
    }
}
