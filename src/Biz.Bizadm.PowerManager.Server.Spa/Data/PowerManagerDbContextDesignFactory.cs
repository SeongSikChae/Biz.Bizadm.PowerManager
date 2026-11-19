using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Biz.Bizadm.PowerManager.Server.Spa.Data
{
    /// <summary>
    /// <c>dotnet ef</c>가 <c>--config</c> 없이 컨텍스트를 만들 때만 쓴다(마이그레이션 생성용). 실행 시에는 쓰지 않는다.
    /// </summary>
    public sealed class PowerManagerDbContextDesignFactory : IDesignTimeDbContextFactory<PowerManagerDbContext>
    {
        public PowerManagerDbContext CreateDbContext(string[] args)
            => new PowerManagerDbContext(new DbContextOptionsBuilder<PowerManagerDbContext>()
                .UseSqlite("Data Source=design-time.db")
                .Options);
    }
}
