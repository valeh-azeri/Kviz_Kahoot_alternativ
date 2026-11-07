using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kviz.DataAccess;

/// <summary>Yalnız "dotnet ef" əmrləri üçün. Bağlantı sətri KVIZ_CONNECTION mühit dəyişənindən oxunur.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<KvizDbContext>
{
    public KvizDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("KVIZ_CONNECTION")
                 ?? "Server=.;Database=Kviz;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<KvizDbContext>().UseSqlServer(cs).Options;
        return new KvizDbContext(options);
    }
}
