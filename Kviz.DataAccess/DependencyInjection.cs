using Kviz.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kviz.DataAccess;

public static class DependencyInjection
{
    public static IServiceCollection AddDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default tapılmadı.");

        services.AddDbContext<KvizDbContext>(o => o.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3)));

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IQuizRepository, QuizRepository>();
        services.AddScoped<IGameRepository, GameRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services;
    }
}
