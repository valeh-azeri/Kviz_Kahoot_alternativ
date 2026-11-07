using Kviz.Business;
using Kviz.Business.Abstractions;
using Kviz.Business.Games;
using Kviz.Business.Models;
using Kviz.Business.Services;
using Kviz.DataAccess;
using Kviz.DataAccess.Repositories;
using Kviz.Entities;
using Kviz.Entities.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kviz.Tests;

public record SentEvent(string Target, Guid Id, string Name, object Payload);

public class FakeNotifier : IGameNotifier
{
    public List<SentEvent> Events { get; } = new();
    private readonly object _lock = new();

    public Task ToHostAsync(Guid gameId, string eventName, object payload)
    {
        lock (_lock) Events.Add(new SentEvent("host", gameId, eventName, payload));
        return Task.CompletedTask;
    }

    public Task ToPlayerAsync(Guid playerId, string eventName, object payload)
    {
        lock (_lock) Events.Add(new SentEvent("player", playerId, eventName, payload));
        return Task.CompletedTask;
    }

    public T? LastState<T>(string target, Guid id) where T : class
    {
        lock (_lock) return Events.LastOrDefault(e => e.Target == target && e.Id == id && e.Name == "state")?.Payload as T;
    }
}

public class FakeFileStorage : IFileStorage
{
    public Task<string> SaveImageAsync(Stream content, string extension, CancellationToken ct = default) =>
        Task.FromResult("/uploads/test" + extension);

    public List<string> Avatars { get; } = new();
    public List<string> DeletedAvatars { get; } = new();

    public Task<string> SaveAvatarAsync(Stream content, CancellationToken ct = default)
    {
        var url = "/uploads/avatars/" + Guid.NewGuid().ToString("N") + ".jpg";
        Avatars.Add(url);
        return Task.FromResult(url);
    }

    public void DeleteAvatar(string url) => DeletedAvatars.Add(url);
}

/// <summary>Testlər üçün kiçik DI konteyneri (EF InMemory baza, saxta bildiriş).</summary>
public sealed class TestHost : IDisposable
{
    public ServiceProvider Provider { get; }
    public FakeNotifier Notifier { get; } = new();
    public Guid UserId { get; } = Guid.NewGuid();

    public TestHost(int introMs = 20)
    {
        var dbName = "kviz-" + Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<KvizDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IQuizRepository, QuizRepository>();
        services.AddScoped<IGameRepository, GameRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton(new GameTimingOptions { IntroMs = introMs, GraceMs = 500, RetentionMinutes = 1 });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGameNotifier>(Notifier);
        services.AddSingleton<IFileStorage, FakeFileStorage>();
        services.AddSingleton<ILiveGameEngine, LiveGameEngine>();
        services.AddScoped<IQuizService, QuizService>();
        services.AddScoped<IQuizExcelService, QuizExcelService>();
        services.AddScoped<IGameService, GameService>();
        services.AddScoped<IChallengeService, ChallengeService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IImageUploadService, ImageUploadService>();
        services.AddScoped<IAvatarService, AvatarService>();
        services.AddIdentityCore<ApplicationUser>(o => { o.Password.RequireNonAlphanumeric = false; o.Password.RequireDigit = false; o.Password.RequireUppercase = false; o.Password.RequireLowercase = false; o.Password.RequiredLength = 6; o.User.RequireUniqueEmail = true; })
            .AddRoles<Microsoft.AspNetCore.Identity.IdentityRole<Guid>>().AddEntityFrameworkStores<KvizDbContext>().AddDefaultTokenProviders();
        services.AddDataProtection();
        services.AddMemoryCache();
        services.AddSingleton(new AdminOptions());
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IAdminService, AdminService>();
        Provider = services.BuildServiceProvider();

        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KvizDbContext>();
        db.Users.Add(new ApplicationUser { Id = UserId, UserName = "t@t.test", Email = "t@t.test", DisplayName = "Test", SecurityStamp = Guid.NewGuid().ToString() });
        db.SaveChanges();
    }

    public ILiveGameEngine Engine => Provider.GetRequiredService<ILiveGameEngine>();

    public Guid NewUser()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KvizDbContext>();
        var id = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = id, UserName = id + "@t.test", Email = id + "@t.test", DisplayName = "Başqası", SecurityStamp = Guid.NewGuid().ToString() });
        db.SaveChanges();
        return id;
    }

    public async Task<T> WithScope<T>(Func<IServiceProvider, Task<T>> fn)
    {
        using var scope = Provider.CreateScope();
        return await fn(scope.ServiceProvider);
    }

    public static QuestionEditDto Mc(string text, int correct, int seconds = 20, PointsMode points = PointsMode.Standard) => new()
    {
        Type = QuestionType.MultipleChoice, Text = text, TimeLimitSeconds = seconds, PointsMode = points,
        Options = Enumerable.Range(0, 4).Select(i => new OptionEditDto { Text = "V" + i, IsCorrect = i == correct }).ToList()
    };

    public async Task<Guid> CreateQuizAsync(params QuestionEditDto[] questions)
    {
        var r = await WithScope(sp => sp.GetRequiredService<IQuizService>().SaveAsync(UserId, new QuizEditDto
        {
            Title = "Test kvizi", Questions = questions.ToList()
        }));
        Assert.True(r.Success, r.FirstError);
        return r.Value;
    }

    public async Task<GameCreatedDto> CreateGameAsync(Guid quizId, GameMode mode = GameMode.LiveClassic, Action<CreateGameDto>? tweak = null)
    {
        var dto = new CreateGameDto { QuizId = quizId, Mode = mode, ShuffleAnswers = false };
        tweak?.Invoke(dto);
        var r = await WithScope(sp => sp.GetRequiredService<IGameService>().CreateAsync(UserId, dto));
        Assert.True(r.Success, r.FirstError);
        return r.Value!;
    }

    /// <summary>Şərtə qədər gözləyir (taymerlər fon axınında işləyir).</summary>
    public static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            if (condition()) return;
            await Task.Delay(15);
        }
        throw new TimeoutException("Gözlənilən vəziyyət alınmadı.");
    }

    public async Task<HostStateDto> WaitHostPhase(Guid gameId, string phase)
    {
        HostStateDto? s = null;
        await WaitUntil(() => (s = Engine.GetHostStateAsync(gameId, UserId).Result)?.Phase == phase);
        return s!;
    }

    public void Dispose() => Provider.Dispose();
}
