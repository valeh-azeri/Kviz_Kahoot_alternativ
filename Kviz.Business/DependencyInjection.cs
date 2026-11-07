using Kviz.Business.Abstractions;
using Kviz.Business.Games;
using Kviz.Business.Services;
using Kviz.DataAccess.Repositories;
using Kviz.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kviz.Business;

public static class DependencyInjection
{
    public static IServiceCollection AddBusiness(this IServiceCollection services, IConfiguration configuration)
    {
        var timing = new GameTimingOptions();
        configuration.GetSection("Kviz:Timing").Bind(timing);
        services.AddSingleton(timing);

        var join = new JoinLinkOptions { PublicBaseUrl = configuration["Kviz:PublicBaseUrl"] };
        services.AddSingleton(join);

        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddSingleton<ILiveGameEngine, LiveGameEngine>();
        services.AddHostedService<GameRecoveryService>();

        services.AddSingleton<IJoinLinkService, JoinLinkService>();
        services.AddSingleton<IQrService, QrService>();

        var admin = new AdminOptions { Emails = configuration.GetSection("Kviz:AdminEmails").Get<string[]>() ?? Array.Empty<string>() };
        services.AddSingleton(admin);
        services.AddHostedService<AdminBootstrapper>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IQuizService, QuizService>();
        services.AddScoped<IQuizExcelService, QuizExcelService>();
        services.AddScoped<IGameService, GameService>();
        services.AddScoped<IChallengeService, ChallengeService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IImageUploadService, ImageUploadService>();
        services.AddScoped<IAvatarService, AvatarService>();
        // Wikimedia qaydası: sorğu göndərən proqram özünü və sahibinin əlaqə ünvanını göstərməlidir.
        // Əlaqə: Kviz:ImageSearch:Contact (e-poçt və ya sayt), yoxdursa saytın ictimai ünvanı.
        var imageOptions = new ImageSearchOptions { Contact = configuration["Kviz:ImageSearch:Contact"] };
        if (string.IsNullOrWhiteSpace(imageOptions.Contact)) imageOptions.Contact = configuration["Kviz:PublicBaseUrl"];
        services.AddSingleton(imageOptions);
        services.AddHttpClient("images", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(15);
            var contact = imageOptions.Contact?.Trim();
            c.DefaultRequestHeaders.UserAgent.ParseAdd(string.IsNullOrEmpty(contact)
                ? "Kvizo/1.0 (educational quiz app)"
                : $"Kvizo/1.0 ({contact.Replace("(", "").Replace(")", "")}; educational quiz app)");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/json, image/*");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,                       // yönləndirmələri ImageSearchService özü yoxlayır
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });
        services.AddScoped<IImageSearchService, ImageSearchService>();
        return services;
    }
}

/// <summary>
/// Server yenidən başlayanda yaddaşdakı canlı oyunlar itir. Bazada yarımçıq qalan canlı oyunları ləğv edir.
/// (Tapşırıqlar bazada olduğu üçün davam edir.)
/// </summary>
public sealed class GameRecoveryService : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<GameRecoveryService> _log;

    public GameRecoveryService(IServiceScopeFactory scopes, ILogger<GameRecoveryService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var games = scope.ServiceProvider.GetRequiredService<IRepository<Kviz.Entities.GameSession>>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var stale = await games.Query()
                .Where(g => g.Mode != GameMode.Challenge && (g.Status == GameStatus.Lobby || g.Status == GameStatus.InProgress))
                .ToListAsync(cancellationToken);
            foreach (var g in stale) { g.Status = GameStatus.Cancelled; g.FinishedAt = DateTime.UtcNow; }
            if (stale.Count > 0) await uow.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Baza hələ hazır olmaya bilər (ilk işə salma): bu xəta tətbiqin açılmasına mane olmamalıdır.
            _log.LogWarning(ex, "Yarımçıq oyunlar təmizlənə bilmədi.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
