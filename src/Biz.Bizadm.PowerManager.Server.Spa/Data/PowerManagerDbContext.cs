using Microsoft.EntityFrameworkCore;

namespace Biz.Bizadm.PowerManager.Server.Spa.Data
{
    public sealed class PowerManagerDbContext(DbContextOptions<PowerManagerDbContext> options) : DbContext(options)
    {
        public DbSet<DeviceRegistration> Devices => Set<DeviceRegistration>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DeviceRegistration>(entity =>
            {
                entity.ToTable("Devices");
                entity.HasKey(e => e.Mac);
                entity.Property(e => e.Mac).HasMaxLength(12);
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.ModelId).IsRequired();
                entity.Property(e => e.Company).IsRequired();
                entity.Property(e => e.MqttKey).HasMaxLength(32).IsRequired();
                // SQLite는 DateTimeOffset 정렬·비교를 지원하지 않으므로 UTC ticks로 저장한다.
                entity.Property(e => e.RegisteredAt).HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
                entity.Property(e => e.LastRequestedAt).HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
            });
        }
    }
}
