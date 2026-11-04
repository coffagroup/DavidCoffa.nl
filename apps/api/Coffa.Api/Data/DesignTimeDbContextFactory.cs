using Microsoft.EntityFrameworkCore;
using Coffa.Api.Infrastructure;
using Microsoft.EntityFrameworkCore.Design;
namespace Coffa.Api.Data;
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection=Environment.GetEnvironmentVariable("ConnectionStrings__Default")??"Host=127.0.0.1;Port=55439;Database=coffa_os_dev;Username=coffa_os";
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(DatabaseConnection.Normalize(connection)).Options);
    }
}
