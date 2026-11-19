using Biz.Bizadm.PowerManager.Core.Devices;
using Biz.Bizadm.PowerManager.Server.Spa.Data;
using Microsoft.EntityFrameworkCore;

namespace Biz.Bizadm.PowerManager.Server.Spa.Devices
{
    /// <summary>
    /// 장치 등록과 발급 키 조회. 컨트롤러와 MQTT 브로커가 함께 쓴다.
    /// </summary>
    public sealed class DeviceRegistrationService(IDbContextFactory<PowerManagerDbContext> factory, TimeProvider timeProvider)
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
        /// 중계 모드에서 다원이 발급한 키를 mac(정규화된 소문자)에 저장한다. 처음이면 새로 만들고, 이미 있으면 키를 바꾸며
        /// 비어 있지 않은 값만 갱신한다. 같은 mac의 첫 등록이 동시에 들어와 충돌하면 한 번 다시 시도해 마지막 키로 갱신한다.
        /// </summary>
        public async Task StoreRelayedKeyAsync(string mac, string? userId, string? modelId, string? company, string mqttKey, CancellationToken cancellationToken)
        {
            try
            {
                await StoreRelayedKeyOnceAsync(mac, userId, modelId, company, mqttKey, cancellationToken);
            }
            catch (DbUpdateException)
            {
                await StoreRelayedKeyOnceAsync(mac, userId, modelId, company, mqttKey, cancellationToken);
            }
        }

        private async Task StoreRelayedKeyOnceAsync(string mac, string? userId, string? modelId, string? company, string mqttKey, CancellationToken cancellationToken)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            await using PowerManagerDbContext db = await factory.CreateDbContextAsync(cancellationToken);
            DeviceRegistration? device = await db.Devices.SingleOrDefaultAsync(d => d.Mac == mac, cancellationToken);
            if (device is null)
            {
                db.Devices.Add(new DeviceRegistration
                {
                    Mac = mac,
                    UserId = userId ?? string.Empty,
                    ModelId = modelId ?? string.Empty,
                    Company = company ?? string.Empty,
                    MqttKey = mqttKey,
                    RegisteredAt = now,
                    LastRequestedAt = now,
                });
            }
            else
            {
                device.MqttKey = mqttKey;
                if (!string.IsNullOrEmpty(userId))
                    device.UserId = userId;
                if (!string.IsNullOrEmpty(modelId))
                    device.ModelId = modelId;
                if (!string.IsNullOrEmpty(company))
                    device.Company = company;
                device.LastRequestedAt = now;
            }
            await db.SaveChangesAsync(cancellationToken);
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

        private async Task<Result> RegisterOnceAsync(string mac, string? userId, string? modelId, string? company, CancellationToken cancellationToken)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            await using PowerManagerDbContext db = await factory.CreateDbContextAsync(cancellationToken);
            DeviceRegistration? device = await db.Devices.SingleOrDefaultAsync(d => d.Mac == mac, cancellationToken);
            bool created = device is null;
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
                    device.ModelId = modelId;
                if (!string.IsNullOrEmpty(company))
                    device.Company = company;
                device.LastRequestedAt = now;
            }
            await db.SaveChangesAsync(cancellationToken);
            return new Result(device.MqttKey, created);
        }
    }
}
