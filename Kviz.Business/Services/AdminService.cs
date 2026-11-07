using System.Security.Cryptography;
using Kviz.Business.Abstractions;
using Kviz.Business.Common;
using Kviz.Business.Games;
using Kviz.Business.Models;
using Kviz.DataAccess.Repositories;
using Kviz.Entities;
using Kviz.Entities.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kviz.Business.Services;

public interface ISettingsService
{
    Task<bool> IsRegistrationOpenAsync();
    Task SetRegistrationOpenAsync(bool open);
    /// <summary>Qeydiyyatsız ziyarətçilər kvizlər kitabxanasına baxa bilərmi (standart: bəli).</summary>
    Task<bool> IsGuestLibraryOpenAsync();
    Task SetGuestLibraryOpenAsync(bool open);
}

public class SettingsService : ISettingsService
{
    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(15);
    private readonly IRepository<AppSetting> _settings;
    private readonly IUnitOfWork _uow;
    private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;

    public SettingsService(IRepository<AppSetting> settings, IUnitOfWork uow, Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    {
        _settings = settings;
        _uow = uow;
        _cache = cache;
    }

    // Standart dəyər həmişə "açıq"dır. Ayar hər səhifədə yoxlandığı üçün qısa müddət yaddaşda saxlanılır.
    private async Task<bool> GetFlagAsync(string key)
    {
        if (_cache.TryGetValue("setting:" + key, out bool cached)) return cached;
        var s = await _settings.QueryNoTracking().FirstOrDefaultAsync(x => x.Key == key);
        var value = s == null || s.Value != "false";
        _cache.Set("setting:" + key, value, CacheTime);
        return value;
    }

    private async Task SetFlagAsync(string key, bool open)
    {
        var s = await _settings.Query().FirstOrDefaultAsync(x => x.Key == key);
        if (s == null) await _settings.AddAsync(new AppSetting { Key = key, Value = open ? "true" : "false" });
        else s.Value = open ? "true" : "false";
        await _uow.SaveChangesAsync();
        _cache.Set("setting:" + key, open, CacheTime);
    }

    public Task<bool> IsRegistrationOpenAsync() => GetFlagAsync(AppSettingKeys.RegistrationOpen);
    public Task SetRegistrationOpenAsync(bool open) => SetFlagAsync(AppSettingKeys.RegistrationOpen, open);
    public Task<bool> IsGuestLibraryOpenAsync() => GetFlagAsync(AppSettingKeys.GuestLibrary);
    public Task SetGuestLibraryOpenAsync(bool open) => SetFlagAsync(AppSettingKeys.GuestLibrary, open);
}

public class AdminOptions
{
    /// <summary>Bu e-poçtlarla qeydiyyat edənlər (və ya artıq qeydiyyatda olanlar) avtomatik admin olur.</summary>
    public string[] Emails { get; set; } = Array.Empty<string>();
}

public interface IAdminService
{
    Task<AdminDashboardDto> GetDashboardAsync();
    Task<PagedResult<AdminUserDto>> ListUsersAsync(string? search, string? filter, int page, int pageSize = 20);
    Task<ServiceResult> CreateUserAsync(string displayName, string email, string password, bool makeAdmin);
    Task<ServiceResult> SetBlockedAsync(Guid adminId, Guid userId, bool blocked);
    Task<ServiceResult> SetAdminAsync(Guid adminId, Guid userId, bool isAdmin);
    Task<ServiceResult<string>> ResetPasswordAsync(Guid adminId, Guid userId);
    Task<ServiceResult> DeleteUserAsync(Guid adminId, Guid userId);
    Task<PagedResult<AdminQuizDto>> ListQuizzesAsync(string? search, string? filter, int page, int pageSize = 20);
    Task<ServiceResult> SetQuizLockedAsync(Guid quizId, bool locked);
    Task<ServiceResult> DeleteQuizAsync(Guid quizId);
    Task<PagedResult<AdminGameDto>> ListGamesAsync(string? filter, int page, int pageSize = 20);
    Task<ServiceResult> DeleteGameAsync(Guid gameId);
    Task<ServiceResult> CancelGameAsync(Guid gameId);
    Task<List<AdminCategoryDto>> ListCategoriesAsync();
    /// <summary>id boşdursa yeni kateqoriya yaradır, yoxsa mövcudu dəyişir.</summary>
    Task<ServiceResult> SaveCategoryAsync(int? id, string name, string icon);
    Task<ServiceResult> MoveCategoryAsync(int id, bool up);
    Task<ServiceResult> DeleteCategoryAsync(int id);
}

public class AdminService : IAdminService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<IdentityRole<Guid>> _roles;
    private readonly IRepository<IdentityUserRole<Guid>> _userRoles;
    private readonly IRepository<Quiz> _quizzes;
    private readonly IRepository<GameSession> _games;
    private readonly IRepository<GamePlayer> _players;
    private readonly IRepository<PlayerAnswer> _answers;
    private readonly IRepository<QuizFavorite> _favorites;
    private readonly IRepository<Category> _categories;
    private readonly IAvatarService _avatars;
    private readonly ISettingsService _settings;
    private readonly IGameService _gameService;
    private readonly ILiveGameEngine _engine;
    private readonly IUnitOfWork _uow;

    public AdminService(UserManager<ApplicationUser> users, RoleManager<IdentityRole<Guid>> roles,
        IRepository<IdentityUserRole<Guid>> userRoles, IRepository<Quiz> quizzes, IRepository<GameSession> games,
        IRepository<GamePlayer> players, IRepository<PlayerAnswer> answers, IRepository<QuizFavorite> favorites,
        IRepository<Category> categories, IAvatarService avatars,
        ISettingsService settings, IGameService gameService, ILiveGameEngine engine, IUnitOfWork uow)
    {
        _categories = categories;
        _avatars = avatars;
        _users = users;
        _roles = roles;
        _userRoles = userRoles;
        _quizzes = quizzes;
        _games = games;
        _players = players;
        _answers = answers;
        _favorites = favorites;
        _settings = settings;
        _gameService = gameService;
        _engine = engine;
        _uow = uow;
    }

    private static string Like(string s) => "%" + s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";

    private async Task<Guid?> AdminRoleIdAsync() => (await _roles.FindByNameAsync(AppRoles.Admin))?.Id;

    // ---------- İdarə paneli ----------

    public async Task<AdminDashboardDto> GetDashboardAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateTime.UtcNow.Date;
        var week = DateTime.UtcNow.AddDays(-7);
        var adminRole = await AdminRoleIdAsync();

        var d = new AdminDashboardDto
        {
            Users = await _users.Users.CountAsync(),
            Admins = adminRole == null ? 0 : await _userRoles.QueryNoTracking().CountAsync(r => r.RoleId == adminRole),
            BlockedUsers = await _users.Users.CountAsync(u => u.LockoutEnd != null && u.LockoutEnd > now),
            NewUsers7Days = await _users.Users.CountAsync(u => u.CreatedAt >= week),
            Quizzes = await _quizzes.QueryNoTracking().CountAsync(),
            PublicQuizzes = await _quizzes.QueryNoTracking().CountAsync(q => q.Visibility == QuizVisibility.Public),
            LockedQuizzes = await _quizzes.QueryNoTracking().CountAsync(q => q.AdminLocked),
            Games = await _games.QueryNoTracking().CountAsync(),
            GamesToday = await _games.QueryNoTracking().CountAsync(g => g.CreatedAt >= today),
            ActiveGames = await _games.QueryNoTracking().CountAsync(g => g.Status == GameStatus.Lobby || g.Status == GameStatus.InProgress),
            Players = await _players.QueryNoTracking().CountAsync(p => !p.IsKicked),
            RegistrationOpen = await _settings.IsRegistrationOpenAsync()
        };
        d.RecentUsers = (await ListUsersAsync(null, null, 1, 6)).Items;
        d.RecentGames = (await ListGamesAsync(null, 1, 6)).Items;
        return d;
    }

    // ---------- İstifadəçilər ----------

    public async Task<PagedResult<AdminUserDto>> ListUsersAsync(string? search, string? filter, int page, int pageSize = 20)
    {
        page = Math.Max(1, page);
        var now = DateTimeOffset.UtcNow;
        var adminRole = await AdminRoleIdAsync();
        var adminIds = adminRole == null ? new List<Guid>() : await _userRoles.QueryNoTracking().Where(r => r.RoleId == adminRole).Select(r => r.UserId).ToListAsync();

        var q = _users.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = Like(search.Trim());
            q = q.Where(u => EF.Functions.Like(u.DisplayName, like, "\\") || (u.Email != null && EF.Functions.Like(u.Email, like, "\\")));
        }
        if (filter == "admin") q = q.Where(u => adminIds.Contains(u.Id));
        else if (filter == "blocked") q = q.Where(u => u.LockoutEnd != null && u.LockoutEnd > now);

        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(u => u.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.CreatedAt, Blocked = u.LockoutEnd != null && u.LockoutEnd > now }).ToListAsync();

        var ids = rows.Select(r => r.Id).ToList();
        var quizCounts = await _quizzes.QueryNoTracking().Where(x => ids.Contains(x.OwnerId)).GroupBy(x => x.OwnerId)
            .Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N);
        var gameCounts = await _games.QueryNoTracking().Where(x => ids.Contains(x.HostId)).GroupBy(x => x.HostId)
            .Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N);

        return new PagedResult<AdminUserDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = rows.Select(r => new AdminUserDto
            {
                Id = r.Id, DisplayName = r.DisplayName, Email = r.Email ?? "", CreatedAt = r.CreatedAt, IsBlocked = r.Blocked,
                IsAdmin = adminIds.Contains(r.Id), QuizCount = quizCounts.GetValueOrDefault(r.Id), GameCount = gameCounts.GetValueOrDefault(r.Id)
            }).ToList()
        };
    }

    /// <summary>Qeydiyyat bağlı olanda (məsələn sinifdə) hesabları administrator özü yaradır.</summary>
    public async Task<ServiceResult> CreateUserAsync(string displayName, string email, string password, bool makeAdmin)
    {
        var res = new ServiceResult();
        displayName = (displayName ?? "").Trim();
        email = (email ?? "").Trim();
        if (displayName.Length is < 2 or > 60) res.Errors.Add(new ValidationError("displayName", "Ad 2-60 simvol olmalıdır."));
        if (email.Length < 4 || !email.Contains('@') || email.Contains(' ')) res.Errors.Add(new ValidationError("email", "E-poçt ünvanı düzgün deyil."));
        if ((password ?? "").Length < 6) res.Errors.Add(new ValidationError("password", "Parol ən azı 6 simvol olmalıdır."));
        if (!res.Success) return res;

        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = displayName, CreatedAt = DateTime.UtcNow };
        var r = await _users.CreateAsync(user, password!);
        if (!r.Succeeded)
        {
            var dup = r.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");
            return ServiceResult.Fail(dup ? "Bu e-poçt ilə hesab artıq mövcuddur." : "Hesab yaradıla bilmədi. Parol və e-poçtu yoxlayın.");
        }
        if (makeAdmin)
        {
            if (!await _roles.RoleExistsAsync(AppRoles.Admin)) await _roles.CreateAsync(new IdentityRole<Guid>(AppRoles.Admin));
            await _users.AddToRoleAsync(user, AppRoles.Admin);
        }
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SetBlockedAsync(Guid adminId, Guid userId, bool blocked)
    {
        if (adminId == userId) return ServiceResult.Fail("Özünüzü bloklaya bilməzsiniz.");
        var u = await _users.FindByIdAsync(userId.ToString());
        if (u == null) return ServiceResult.Fail("İstifadəçi tapılmadı.");
        if (blocked && await _users.IsInRoleAsync(u, AppRoles.Admin)) return ServiceResult.Fail("Administratoru bloklamaq üçün əvvəlcə onun admin rolunu götürün.");

        await _users.SetLockoutEnabledAsync(u, true);
        if (blocked)
        {
            await _users.SetLockoutEndDateAsync(u, DateTimeOffset.MaxValue);
            await _users.UpdateSecurityStampAsync(u); // açıq sessiyalar bağlanır
        }
        else
        {
            await _users.SetLockoutEndDateAsync(u, null);
            await _users.ResetAccessFailedCountAsync(u);
        }
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SetAdminAsync(Guid adminId, Guid userId, bool isAdmin)
    {
        var u = await _users.FindByIdAsync(userId.ToString());
        if (u == null) return ServiceResult.Fail("İstifadəçi tapılmadı.");
        if (!await _roles.RoleExistsAsync(AppRoles.Admin)) await _roles.CreateAsync(new IdentityRole<Guid>(AppRoles.Admin));

        if (isAdmin)
        {
            if (await _users.IsLockedOutAsync(u)) return ServiceResult.Fail("Bloklanmış istifadəçini admin etmək olmaz.");
            if (!await _users.IsInRoleAsync(u, AppRoles.Admin)) await _users.AddToRoleAsync(u, AppRoles.Admin);
        }
        else
        {
            if (adminId == userId) return ServiceResult.Fail("Öz admin rolunuzu götürə bilməzsiniz. Bunu başqa administrator etməlidir.");
            var admins = await _users.GetUsersInRoleAsync(AppRoles.Admin);
            if (admins.Count <= 1) return ServiceResult.Fail("Sonuncu administratorun rolunu götürmək olmaz.");
            if (await _users.IsInRoleAsync(u, AppRoles.Admin)) await _users.RemoveFromRoleAsync(u, AppRoles.Admin);
        }
        await _users.UpdateSecurityStampAsync(u); // yeni rol növbəti sorğuda qüvvəyə minir
        return ServiceResult.Ok();
    }

    /// <summary>E-poçt göndərilmədiyi üçün admin müvəqqəti parol yaradır və istifadəçiyə özü ötürür.</summary>
    public async Task<ServiceResult<string>> ResetPasswordAsync(Guid adminId, Guid userId)
    {
        var u = await _users.FindByIdAsync(userId.ToString());
        if (u == null) return ServiceResult<string>.Fail("İstifadəçi tapılmadı.");
        var temp = GenerateTempPassword();
        var token = await _users.GeneratePasswordResetTokenAsync(u);
        var r = await _users.ResetPasswordAsync(u, token, temp);
        if (!r.Succeeded) return ServiceResult<string>.Fail("Parol sıfırlana bilmədi.");
        await _users.ResetAccessFailedCountAsync(u);
        return ServiceResult<string>.Ok(temp);
    }

    private static string GenerateTempPassword()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789"; // oxşar simvollar (0/O, 1/l/I) çıxarılıb
        return new string(Enumerable.Range(0, 10).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
    }

    public async Task<ServiceResult> DeleteUserAsync(Guid adminId, Guid userId)
    {
        if (adminId == userId) return ServiceResult.Fail("Öz hesabınızı silə bilməzsiniz.");
        var u = await _users.FindByIdAsync(userId.ToString());
        if (u == null) return ServiceResult.Fail("İstifadəçi tapılmadı.");
        if (await _users.IsInRoleAsync(u, AppRoles.Admin)) return ServiceResult.Fail("Administratoru silmək üçün əvvəlcə onun admin rolunu götürün.");

        // Davam edən canlı oyunlar bağlanır
        var live = await _games.QueryNoTracking().Where(g => g.HostId == userId && g.Mode != GameMode.Challenge &&
            (g.Status == GameStatus.Lobby || g.Status == GameStatus.InProgress)).Select(g => g.Id).ToListAsync();
        foreach (var id in live) await _engine.CancelAsync(id, userId);

        // Hostu olduğu oyunlar (hesabatlarla birlikdə) və sevimliləri silinir, sonra istifadəçi (kvizləri ilə)
        var photos = await _players.QueryNoTracking().Where(p => p.GameSession!.HostId == userId && p.Avatar != null && p.Avatar.StartsWith("/uploads/"))
            .Select(p => p.Avatar).ToListAsync();
        await _answers.Query().Where(a => a.Player!.GameSession!.HostId == userId).ExecuteDeleteAsync();
        await _games.Query().Where(g => g.HostId == userId).ExecuteDeleteAsync();
        await _favorites.Query().Where(f => f.UserId == userId).ExecuteDeleteAsync();
        var r = await _users.DeleteAsync(u);
        _avatars.DeletePhotos(photos);
        return r.Succeeded ? ServiceResult.Ok() : ServiceResult.Fail("İstifadəçi silinə bilmədi.");
    }

    // ---------- Kvizlər ----------

    public async Task<PagedResult<AdminQuizDto>> ListQuizzesAsync(string? search, string? filter, int page, int pageSize = 20)
    {
        page = Math.Max(1, page);
        var q = _quizzes.QueryNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = Like(search.Trim());
            q = q.Where(x => EF.Functions.Like(x.Title, like, "\\") || EF.Functions.Like(x.Owner!.DisplayName, like, "\\") ||
                             (x.Owner.Email != null && EF.Functions.Like(x.Owner.Email, like, "\\")));
        }
        q = filter switch
        {
            "public" => q.Where(x => x.Visibility == QuizVisibility.Public),
            "private" => q.Where(x => x.Visibility == QuizVisibility.Private),
            "locked" => q.Where(x => x.AdminLocked),
            _ => q
        };
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(x => x.UpdatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AdminQuizDto
            {
                Id = x.Id, Title = x.Title, OwnerName = x.Owner!.DisplayName, OwnerEmail = x.Owner.Email ?? "",
                QuestionCount = x.Questions.Count, PlayCount = x.PlayCount, IsPublic = x.Visibility == QuizVisibility.Public,
                AdminLocked = x.AdminLocked, UpdatedAt = x.UpdatedAt
            }).ToListAsync();
        return new PagedResult<AdminQuizDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    public async Task<ServiceResult> SetQuizLockedAsync(Guid quizId, bool locked)
    {
        var q = await _quizzes.Query().FirstOrDefaultAsync(x => x.Id == quizId);
        if (q == null) return ServiceResult.Fail("Kviz tapılmadı.");
        q.AdminLocked = locked;
        if (locked) q.Visibility = QuizVisibility.Private; // kitabxanadan dərhal çıxır
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteQuizAsync(Guid quizId)
    {
        var q = await _quizzes.Query().FirstOrDefaultAsync(x => x.Id == quizId);
        if (q == null) return ServiceResult.Fail("Kviz tapılmadı.");
        _quizzes.Remove(q);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    // ---------- Oyunlar ----------

    public async Task<PagedResult<AdminGameDto>> ListGamesAsync(string? filter, int page, int pageSize = 20)
    {
        page = Math.Max(1, page);
        var q = _games.QueryNoTracking();
        q = filter switch
        {
            "active" => q.Where(g => g.Status == GameStatus.Lobby || g.Status == GameStatus.InProgress),
            "finished" => q.Where(g => g.Status == GameStatus.Finished),
            "cancelled" => q.Where(g => g.Status == GameStatus.Cancelled),
            _ => q
        };
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(g => g.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(g => new AdminGameDto
            {
                Id = g.Id, QuizTitle = g.QuizTitle, HostName = g.Host!.DisplayName, Pin = g.Pin, Mode = g.Mode, Status = g.Status,
                CreatedAt = g.CreatedAt, PlayerCount = g.Players.Count(p => !p.IsKicked)
            }).ToListAsync();
        return new PagedResult<AdminGameDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    public async Task<ServiceResult> CancelGameAsync(Guid gameId)
    {
        var g = await _games.Query().FirstOrDefaultAsync(x => x.Id == gameId);
        if (g == null) return ServiceResult.Fail("Oyun tapılmadı.");
        if (g.Status is not (GameStatus.Lobby or GameStatus.InProgress)) return ServiceResult.Ok();
        if (g.Mode == GameMode.Challenge)
            return await _gameService.CloseChallengeAsync(gameId, g.HostId);
        await _engine.CancelAsync(gameId, g.HostId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteGameAsync(Guid gameId)
    {
        var host = await _games.QueryNoTracking().Where(x => x.Id == gameId).Select(x => (Guid?)x.HostId).FirstOrDefaultAsync();
        if (host == null) return ServiceResult.Fail("Oyun tapılmadı.");
        return await _gameService.DeleteAsync(gameId, host.Value);
    }

    // ---------- Kateqoriyalar ----------

    public async Task<List<AdminCategoryDto>> ListCategoriesAsync() =>
        await _categories.QueryNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new AdminCategoryDto { Id = c.Id, Name = c.Name, Icon = c.Icon, SortOrder = c.SortOrder, QuizCount = c.Quizzes.Count })
            .ToListAsync();

    public async Task<ServiceResult> SaveCategoryAsync(int? id, string name, string icon)
    {
        name = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        icon = (icon ?? "").Trim();
        var res = new ServiceResult();
        if (name.Length is < 2 or > 60) res.Errors.Add(new ValidationError("name", "Kateqoriya adı 2-60 simvol olmalıdır."));
        if (icon.Length > 16) res.Errors.Add(new ValidationError("icon", "Nişan (emoji) çox uzundur. Bir emoji yazın."));
        if (!res.Success) return res;
        if (icon.Length == 0) icon = "🎯";

        var all = await _categories.Query().ToListAsync();
        if (all.Any(c => c.Id != id && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult.Fail("Bu adda kateqoriya artıq var.");

        if (id == null)
        {
            await _categories.AddAsync(new Category { Name = name, Icon = icon, SortOrder = all.Count == 0 ? 1 : all.Max(c => c.SortOrder) + 1 });
        }
        else
        {
            var c = all.FirstOrDefault(x => x.Id == id);
            if (c == null) return ServiceResult.Fail("Kateqoriya tapılmadı.");
            c.Name = name;
            c.Icon = icon;
        }
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> MoveCategoryAsync(int id, bool up)
    {
        var all = await _categories.Query().OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync();
        var i = all.FindIndex(c => c.Id == id);
        if (i < 0) return ServiceResult.Fail("Kateqoriya tapılmadı.");
        var j = up ? i - 1 : i + 1;
        if (j >= 0 && j < all.Count) (all[i], all[j]) = (all[j], all[i]);
        for (var k = 0; k < all.Count; k++) all[k].SortOrder = k + 1; // təkrarlanan sıra nömrələri də düzəlir
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteCategoryAsync(int id)
    {
        var c = await _categories.Query().FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return ServiceResult.Fail("Kateqoriya tapılmadı.");
        // Kvizlər silinmir, sadəcə kateqoriyasız qalır
        foreach (var q in await _quizzes.Query().Where(x => x.CategoryId == id).ToListAsync()) q.CategoryId = null;
        _categories.Remove(c);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }
}

/// <summary>İlk işə salmada Admin rolunu yaradır və konfiqurasiyadakı e-poçtları admin edir.</summary>
public sealed class AdminBootstrapper : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly AdminOptions _options;
    private readonly ILogger<AdminBootstrapper> _log;

    public AdminBootstrapper(IServiceScopeFactory scopes, AdminOptions options,
        ILogger<AdminBootstrapper> log)
    {
        _scopes = scopes;
        _options = options;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            if (!await roles.RoleExistsAsync(AppRoles.Admin)) await roles.CreateAsync(new IdentityRole<Guid>(AppRoles.Admin));
            foreach (var email in _options.Emails.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                var u = await users.FindByEmailAsync(email.Trim());
                if (u != null && !await users.IsInRoleAsync(u, AppRoles.Admin)) await users.AddToRoleAsync(u, AppRoles.Admin);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Admin rolu hazırlana bilmədi.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
