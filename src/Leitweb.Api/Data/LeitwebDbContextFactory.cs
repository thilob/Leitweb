using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Leitweb.Api.Data;

public sealed class LeitwebDbContextFactory : IDesignTimeDbContextFactory<LeitwebDbContext>
{
    public LeitwebDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LeitwebDbContext>()
            .UseNpgsql("Host=localhost;Database=leitweb;Username=leitweb;Password=design-time-only", npgsql => npgsql.UseNetTopologySuite())
            .Options;
        return new LeitwebDbContext(options);
    }
}
