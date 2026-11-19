using Microsoft.EntityFrameworkCore;

namespace Biz.Bizadm.PowerManager.Server.Spa.Data
{
    /// <summary>
    /// 시작 시 DB 마이그레이션을 적용한다. 브로커(MQTT 접속 시 키 조회)보다 먼저 등록해 먼저 시작되게 한다.
    /// </summary>
    public sealed class DatabaseMigrationHostedService(IDbContextFactory<PowerManagerDbContext> factory, ILogger<DatabaseMigrationHostedService> logger)
        : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using PowerManagerDbContext db = await factory.CreateDbContextAsync(cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Database migrated: {DataSource}", db.Database.GetDbConnection().DataSource);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
