using Biz.Bizadm.PowerManager.Core.Devices;
using Biz.Bizadm.PowerManager.Server.Spa.Data;
using Microsoft.EntityFrameworkCore;

namespace Biz.Bizadm.PowerManager.Server.Spa.Devices
{
    /// <summary>
    /// 장치 등록과 발급 키 조회. 컨트롤러와 MQTT 브로커가 함께 쓴다.
    /// </summary>
    public sealed class DeviceRegistrationService(IDbContextFactory<PowerManagerDbContext> factory, TimeProvider timeProvider,
        DeviceStateStore states)
    {
        /// <summary>
        /// 등록 결과. <see cref="Created"/>는 이번 요청으로 키를 새로 발급했는지다.
        /// </summary>
        public sealed record Result(string MqttKey, bool Created);

        /// <summary>
        /// mac(정규화된 소문자)으로 등록한다. 처음이면 키를 발급해 저장하고, 이미 있으면 저장된 키를 그대로 쓰며
        /// 비어 있지 않은 값만 갱신한다. 같은 mac의 첫 등록이 동시에 들어와 키 충돌이 나면 저장된 쪽을 다시 읽는다.
        /// </summary>
        public async Task<Result> RegisterAsync(string mac, string? userId, string? modelId, string? company, CancellationToken cancellationToken)
        {
            try
            {
                return await RegisterOnceAsync(mac, userId, modelId, company, cancellationToken);
            }
            catch (DbUpdateException)
            {
                // 다른 요청이 같은 mac을 먼저 저장했다. 다시 읽어 그 키로 응답한다.
                return await RegisterOnceAsync(mac, userId, modelId, company, cancellationToken);
            }
        }

        /// <summary>
        /// mac(대소문자 무관)으로 저장된 키를 찾는다. 없거나 mac 형식이 아니면 null.
        /// </summary>
        public async Task<string?> FindMqttKeyAsync(string mac, CancellationToken cancellationToken)
        {
            if (!DeviceIdentity.TryNormalizeMac(mac, out string? normalized))
                return null;
            await using PowerManagerDbContext db = await factory.CreateDbContextAsync(cancellationToken);
            return await db.Devices.Where(d => d.Mac == normalized).Select(d => d.MqttKey).SingleOrDefaultAsync(cancellationToken);
        }

        /// <summary>
        /// 장비명을 저장한다. 판정 순서는 mac 형식(400) → 등록(404) → 장비명(400)이고, 지금 값과 같으면 저장도 알림도 하지 않는다.
        /// 바뀐 열만 저장하므로 등록 요청과 겹쳐도 서로의 값을 덮지 않는다.
        /// </summary>
        public async Task<DeviceCommandResult> RenameAsync(string mac, string? name, CancellationToken cancellationToken)
        {
            // 관리 API의 mac은 소문자 12자리다. 대문자는 형식 오류로 본다.
            if (!DeviceIdentity.TryNormalizeMac(mac, out string? normalizedMac) || normalizedMac != mac)
                return DeviceCommandResult.InvalidMac;
            await using PowerManagerDbContext db = await factory.CreateDbContextAsync(cancellationToken);
            DeviceRegistration? device = await db.Devices.SingleOrDefaultAsync(d => d.Mac == mac, cancellationToken);
            if (device is null)
                return DeviceCommandResult.NotFound;
            if (!DeviceName.TryNormalize(name, out string? normalizedName))
                return DeviceCommandResult.InvalidName;
            if (device.Name == normalizedName)
                return DeviceCommandResult.Success;
            device.Name = normalizedName;
            await db.SaveChangesAsync(cancellationToken);
            states.NotifyChanged(mac);
            return DeviceCommandResult.Success;
        }

        private async Task<Result> RegisterOnceAsync(string mac, string? userId, string? modelId, string? company, CancellationToken cancellationToken)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            await using PowerManagerDbContext db = await factory.CreateDbContextAsync(cancellationToken);
            DeviceRegistration? device = await db.Devices.SingleOrDefaultAsync(d => d.Mac == mac, cancellationToken);
            bool created = device is null;
            bool statusChanged = created;
            if (device is null)
            {
                device = new DeviceRegistration
                {
                    Mac = mac,
                    UserId = userId ?? string.Empty,
                    ModelId = modelId ?? string.Empty,
                    Company = company ?? string.Empty,
                    MqttKey = MqttKeyGenerator.Create(),
                    RegisteredAt = now,
                    LastRequestedAt = now,
                };
                db.Devices.Add(device);
            }
            else
            {
                if (!string.IsNullOrEmpty(userId))
                    device.UserId = userId;
                if (!string.IsNullOrEmpty(modelId))
                {
                    statusChanged = device.ModelId != modelId;
                    device.ModelId = modelId;
                }
                if (!string.IsNullOrEmpty(company))
                    device.Company = company;
                device.LastRequestedAt = now;
            }
            await db.SaveChangesAsync(cancellationToken);
            if (statusChanged)
                states.NotifyChanged(mac);
            return new Result(device.MqttKey, created);
        }
    }
}
