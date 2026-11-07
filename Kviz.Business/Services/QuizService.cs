using Kviz.Business.Abstractions;
using Kviz.Business.Common;
using Kviz.Business.Models;
using Kviz.DataAccess.Repositories;
using Kviz.Entities;
using Kviz.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kviz.Business.Services;

public class QuizService : IQuizService
{
    public const int MaxQuestions = 100;
    public const int MaxOptions = 6;

    private readonly IQuizRepository _quizzes;
    private readonly IRepository<Category> _categories;
    private readonly IRepository<QuizFavorite> _favorites;
    private readonly IRepository<GameSession> _games;
    private readonly IUnitOfWork _uow;

    public QuizService(IQuizRepository quizzes, IRepository<Category> categories, IRepository<QuizFavorite> favorites,
        IRepository<GameSession> games, IUnitOfWork uow)
    {
        _quizzes = quizzes;
        _categories = categories;
        _favorites = favorites;
        _games = games;
        _uow = uow;
    }

    public async Task<List<CategoryDto>> GetCategoriesAsync() =>
        await _categories.QueryNoTracking().OrderBy(c => c.SortOrder)
            .Select(c => new CategoryDto { Id = c.Id, Name = c.Name, Icon = c.Icon }).ToListAsync();

    // ---------- Redaktor ----------

    public QuizEditDto NewDraft() => new()
    {
        Title = string.Empty,
        Language = "az",
        Questions = { DefaultQuestion(QuestionType.MultipleChoice) }
    };

    public static QuestionEditDto DefaultQuestion(QuestionType type) => type switch
    {
        QuestionType.TrueFalse => new QuestionEditDto
        {
            Type = type, Options = { new() { Text = "Doğru", IsCorrect = true }, new() { Text = "Yanlış" } }
        },
        QuestionType.TypeAnswer => new QuestionEditDto { Type = type, TimeLimitSeconds = 30, Options = { new() { Text = "", IsCorrect = true } } },
        QuestionType.Slider => new QuestionEditDto
        { Type = type, SliderMin = 0, SliderMax = 100, SliderStep = 1, SliderCorrect = 50, SliderTolerancePercent = 5 },
        QuestionType.Puzzle => new QuestionEditDto
        { Type = type, TimeLimitSeconds = 30, Options = { new() { Text = "" }, new() { Text = "" }, new() { Text = "" }, new() { Text = "" } } },
        QuestionType.Poll => new QuestionEditDto
        { Type = type, PointsMode = PointsMode.NoPoints, Options = { new(), new(), new(), new() } },
        QuestionType.WordCloud or QuestionType.OpenEnded => new QuestionEditDto { Type = type, TimeLimitSeconds = 30, PointsMode = PointsMode.NoPoints },
        _ => new QuestionEditDto { Type = QuestionType.MultipleChoice, Options = { new(), new(), new(), new() } }
    };

    public async Task<QuizEditDto?> GetForEditAsync(Guid quizId, Guid userId)
    {
        var q = await _quizzes.GetFullNoTrackingAsync(quizId);
        if (q == null || q.OwnerId != userId) return null;
        return new QuizEditDto
        {
            Id = q.Id, Title = q.Title, Description = q.Description, CoverImageUrl = q.CoverImageUrl,
            CategoryId = q.CategoryId, Language = q.Language, IsPublic = q.Visibility == QuizVisibility.Public, AdminLocked = q.AdminLocked,
            Questions = q.Questions.OrderBy(x => x.Order).Select(x => new QuestionEditDto
            {
                Type = x.Type, Text = x.Text, ImageUrl = x.ImageUrl, RelatedImageUrl = x.RelatedImageUrl, RelatedImageCredit = x.RelatedImageCredit, TimeLimitSeconds = x.TimeLimitSeconds,
                PointsMode = x.PointsMode, MultiSelect = x.MultiSelect, SliderMin = x.SliderMin, SliderMax = x.SliderMax,
                SliderStep = x.SliderStep, SliderCorrect = x.SliderCorrect, SliderTolerancePercent = x.SliderTolerancePercent,
                Options = x.Options.OrderBy(o => o.Order).Select(o => new OptionEditDto { Text = o.Text, IsCorrect = o.IsCorrect }).ToList()
            }).ToList()
        };
    }

    public async Task<ServiceResult<Guid>> SaveAsync(Guid userId, QuizEditDto dto)
    {
        Normalize(dto);
        var errors = Validate(dto);
        if (dto.CategoryId.HasValue && !await _categories.QueryNoTracking().AnyAsync(c => c.Id == dto.CategoryId))
            errors.Add(new ValidationError("categoryId", "Seçilmiş kateqoriya mövcud deyil."));
        if (errors.Count > 0) return new ServiceResult<Guid> { Errors = errors };

        Quiz quiz;
        if (dto.Id.HasValue)
        {
            var existing = await _quizzes.GetFullAsync(dto.Id.Value);
            if (existing == null || existing.OwnerId != userId) return ServiceResult<Guid>.Fail("Kviz tapılmadı və ya icazəniz yoxdur.");
            quiz = existing;
            foreach (var oldQ in quiz.Questions.ToList()) quiz.Questions.Remove(oldQ);
        }
        else
        {
            quiz = new Quiz { Id = Guid.NewGuid(), OwnerId = userId, CreatedAt = DateTime.UtcNow };
            await _quizzes.AddAsync(quiz);
        }

        quiz.Title = dto.Title;
        quiz.Description = dto.Description;
        quiz.CoverImageUrl = dto.CoverImageUrl;
        quiz.CategoryId = dto.CategoryId;
        quiz.Language = dto.Language;
        quiz.Visibility = dto.IsPublic && !quiz.AdminLocked ? QuizVisibility.Public : QuizVisibility.Private;
        quiz.UpdatedAt = DateTime.UtcNow;

        // Diqqət: yeni sual/variantlara Id verilmir. Mövcud (izlənən) kvizə əlavə edilən və açarı artıq təyin olunmuş
        // entity EF tərəfindən "Modified" sayılır və mövcud olmayan sətri yeniləməyə çalışır. Açarı EF özü yaradır.
        var order = 0;
        foreach (var q in dto.Questions)
        {
            var entity = new Question
            {
                Order = order++, Type = q.Type, Text = q.Text, ImageUrl = q.ImageUrl, RelatedImageUrl = q.RelatedImageUrl, RelatedImageCredit = q.RelatedImageCredit,
                TimeLimitSeconds = q.TimeLimitSeconds, PointsMode = q.PointsMode, MultiSelect = q.MultiSelect,
                SliderMin = q.SliderMin, SliderMax = q.SliderMax, SliderStep = q.SliderStep, SliderCorrect = q.SliderCorrect,
                SliderTolerancePercent = q.SliderTolerancePercent
            };
            var o = 0;
            foreach (var opt in q.Options)
                entity.Options.Add(new AnswerOption { Order = o++, Text = opt.Text, IsCorrect = opt.IsCorrect });
            quiz.Questions.Add(entity);
        }

        await _uow.SaveChangesAsync();
        return ServiceResult<Guid>.Ok(quiz.Id);
    }

    /// <summary>Mətnləri təmizləyir, sual növünə görə variantları sahə qaydasına salır.</summary>
    public static void Normalize(QuizEditDto dto)
    {
        dto.Title = (dto.Title ?? string.Empty).Trim();
        dto.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        dto.CoverImageUrl = CleanUrl(dto.CoverImageUrl);
        dto.Language = string.IsNullOrWhiteSpace(dto.Language) ? "az" : dto.Language.Trim().ToLowerInvariant();
        dto.Questions ??= new();

        foreach (var q in dto.Questions)
        {
            q.Text = (q.Text ?? string.Empty).Trim();
            q.ImageUrl = CleanUrl(q.ImageUrl);
            // Əlavə (internetdən gətirilən) şəkil sualın şəklindən ayrıdır; müəllif/lisenziya yalnız ona aiddir
            q.RelatedImageUrl = CleanUrl(q.RelatedImageUrl);
            q.RelatedImageCredit = q.RelatedImageUrl == null || string.IsNullOrWhiteSpace(q.RelatedImageCredit) ? null
                : (q.RelatedImageCredit.Trim().Length > 300 ? q.RelatedImageCredit.Trim()[..300] : q.RelatedImageCredit.Trim());
            q.Options ??= new();
            foreach (var o in q.Options) o.Text = (o.Text ?? string.Empty).Trim();

            switch (q.Type)
            {
                case QuestionType.TrueFalse:
                    if (q.Options.Count == 2) { q.Options[0].Text = "Doğru"; q.Options[1].Text = "Yanlış"; }
                    q.MultiSelect = false;
                    break;
                case QuestionType.MultipleChoice:
                    q.Options = q.Options.Where(o => o.Text.Length > 0).ToList();
                    break;
                case QuestionType.Puzzle:
                    q.Options = q.Options.Where(o => o.Text.Length > 0).ToList();
                    foreach (var o in q.Options) o.IsCorrect = true;
                    q.MultiSelect = false;
                    break;
                case QuestionType.Poll:
                    q.Options = q.Options.Where(o => o.Text.Length > 0).ToList();
                    foreach (var o in q.Options) o.IsCorrect = false;
                    q.MultiSelect = false;
                    break;
                case QuestionType.TypeAnswer:
                    q.Options = q.Options.Where(o => o.Text.Length > 0).ToList();
                    foreach (var o in q.Options) o.IsCorrect = true;
                    q.MultiSelect = false;
                    break;
                default:
                    q.Options = new();
                    q.MultiSelect = false;
                    break;
            }

            if (q.Type != QuestionType.Slider)
            {
                q.SliderMin = q.SliderMax = q.SliderStep = q.SliderCorrect = null;
                q.SliderTolerancePercent = 0;
            }
            if (q.Type is QuestionType.Poll or QuestionType.WordCloud or QuestionType.OpenEnded) q.PointsMode = PointsMode.NoPoints;
        }
    }

    private static string? CleanUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (url.Length > 500) return null;
        if (url.StartsWith('/') && !url.StartsWith("//")) return url;
        return Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) ? url : null;
    }

    public static List<ValidationError> Validate(QuizEditDto dto)
    {
        var e = new List<ValidationError>();
        if (dto.Title.Length < 1) e.Add(new("title", "Kvizə ad verin."));
        else if (dto.Title.Length > 120) e.Add(new("title", "Ad 120 simvoldan uzun ola bilməz."));
        if (dto.Description?.Length > 500) e.Add(new("description", "Təsvir 500 simvoldan uzun ola bilməz."));
        if (dto.Language.Length > 8) e.Add(new("language", "Dil kodu düzgün deyil."));
        if (dto.Questions.Count == 0) e.Add(new("questions", "Ən azı bir sual əlavə edin."));
        if (dto.Questions.Count > MaxQuestions) e.Add(new("questions", $"Bir kvizdə ən çox {MaxQuestions} sual ola bilər."));

        for (var i = 0; i < dto.Questions.Count; i++)
        {
            var q = dto.Questions[i];
            var p = $"questions[{i}]";
            var n = i + 1;
            // Sual yalnız şəkil kimi də ola bilər: mətn və ya şəkildən biri mütləqdir
            if (q.Text.Length == 0 && q.ImageUrl == null && q.RelatedImageUrl == null)
                e.Add(new($"{p}.text", $"{n}-ci sual üçün mətn yazın və ya sualın şəklini yapışdırın."));
            else if (q.Text.Length > 500) e.Add(new($"{p}.text", $"{n}-ci sualın mətni 500 simvoldan uzundur."));
            if (q.TimeLimitSeconds is < 5 or > 600) e.Add(new($"{p}.time", $"{n}-ci sual üçün vaxt 5-600 saniyə arası olmalıdır."));

            var correct = q.Options.Count(o => o.IsCorrect);
            switch (q.Type)
            {
                case QuestionType.MultipleChoice:
                    if (q.Options.Count < 2) e.Add(new($"{p}.options", $"{n}-ci sual üçün ən azı 2 variant yazın."));
                    else if (q.Options.Count > MaxOptions) e.Add(new($"{p}.options", $"{n}-ci sualda ən çox {MaxOptions} variant ola bilər."));
                    if (correct == 0) e.Add(new($"{p}.options", $"{n}-ci sual üçün düzgün cavab seçin."));
                    else if (!q.MultiSelect && correct > 1) e.Add(new($"{p}.options", $"{n}-ci sualda yalnız bir düzgün cavab ola bilər."));
                    break;
                case QuestionType.TrueFalse:
                    if (q.Options.Count != 2 || correct != 1) e.Add(new($"{p}.options", $"{n}-ci sual üçün Doğru və ya Yanlış seçin."));
                    break;
                case QuestionType.TypeAnswer:
                    if (q.Options.Count == 0) e.Add(new($"{p}.options", $"{n}-ci sual üçün ən azı bir qəbul olunan cavab yazın."));
                    else if (q.Options.Count > 4) e.Add(new($"{p}.options", $"{n}-ci sualda ən çox 4 qəbul olunan cavab ola bilər."));
                    break;
                case QuestionType.Puzzle:
                    if (q.Options.Count < 2) e.Add(new($"{p}.options", $"{n}-ci sual üçün ən azı 2 element yazın."));
                    else if (q.Options.Count > MaxOptions) e.Add(new($"{p}.options", $"{n}-ci sualda ən çox {MaxOptions} element ola bilər."));
                    break;
                case QuestionType.Poll:
                    if (q.Options.Count < 2) e.Add(new($"{p}.options", $"{n}-ci sorğu üçün ən azı 2 variant yazın."));
                    else if (q.Options.Count > MaxOptions) e.Add(new($"{p}.options", $"{n}-ci sorğuda ən çox {MaxOptions} variant ola bilər."));
                    break;
                case QuestionType.Slider:
                    if (q.SliderMin is not double min || q.SliderMax is not double max || q.SliderStep is not double step || q.SliderCorrect is not double right)
                    { e.Add(new($"{p}.slider", $"{n}-ci sualda minimum, maksimum, addım və düzgün dəyəri doldurun.")); break; }
                    if (min >= max) e.Add(new($"{p}.slider", $"{n}-ci sualda minimum maksimumdan kiçik olmalıdır."));
                    else if (step <= 0 || (max - min) / step > 10000) e.Add(new($"{p}.slider", $"{n}-ci sualda addım düzgün deyil."));
                    else if (right < min || right > max) e.Add(new($"{p}.slider", $"{n}-ci sualda düzgün dəyər aralığın içində olmalıdır."));
                    if (q.SliderTolerancePercent is < 0 or > 50) e.Add(new($"{p}.slider", $"{n}-ci sualda dəqiqlik həddi 0-50% arası olmalıdır."));
                    break;
            }
            for (var j = 0; j < q.Options.Count; j++)
                if (q.Options[j].Text.Length > 200) e.Add(new($"{p}.options[{j}]", $"{n}-ci sualın {j + 1}-ci variantı 200 simvoldan uzundur."));
        }
        return e;
    }

    // ---------- Oxuma ----------

    public async Task<QuizDetailsDto?> GetDetailsAsync(Guid quizId, Guid? userId, bool isAdmin = false)
    {
        var q = await _quizzes.GetFullNoTrackingAsync(quizId);
        if (q == null) return null;
        var isOwner = userId.HasValue && q.OwnerId == userId;
        if (!isOwner && !isAdmin && q.Visibility != QuizVisibility.Public) return null;
        var seesAnswers = isOwner || isAdmin;

        var isFav = userId.HasValue && await _favorites.QueryNoTracking().AnyAsync(f => f.QuizId == quizId && f.UserId == userId);
        var details = new QuizDetailsDto
        {
            Card = new QuizCardDto
            {
                Id = q.Id, Title = q.Title, Description = q.Description, CoverImageUrl = q.CoverImageUrl, CategoryId = q.CategoryId,
                CategoryName = q.Category?.Name, CategoryIcon = q.Category?.Icon, Language = q.Language,
                QuestionCount = q.Questions.Count, PlayCount = q.PlayCount, OwnerName = q.Owner?.DisplayName ?? "",
                IsPublic = q.Visibility == QuizVisibility.Public, AdminLocked = q.AdminLocked, IsFavorite = isFav, IsOwner = isOwner, UpdatedAt = q.UpdatedAt
            },
            TotalSeconds = q.Questions.Sum(x => x.TimeLimitSeconds),
            Questions = q.Questions.OrderBy(x => x.Order).Select(x => new QuizQuestionSummaryDto
            {
                Order = x.Order + 1, Type = x.Type, Text = x.Text, ImageUrl = x.ImageUrl, TimeLimitSeconds = x.TimeLimitSeconds,
                PointsMode = x.PointsMode,
                Options = seesAnswers ? x.Options.OrderBy(o => o.Order).Select(o => new OptionEditDto { Text = o.Text, IsCorrect = o.IsCorrect }).ToList() : null
            }).ToList()
        };

        if (isOwner)
        {
            details.RecentGames = await _games.QueryNoTracking().Where(g => g.QuizId == quizId)
                .OrderByDescending(g => g.CreatedAt).Take(5)
                .Select(g => new GameHistoryItemDto
                {
                    Id = g.Id, QuizTitle = g.QuizTitle, Pin = g.Pin, Mode = g.Mode, Status = g.Status, CreatedAt = g.CreatedAt,
                    PlayerCount = g.Players.Count(p => !p.IsKicked), QuestionCount = g.Questions.Count
                }).ToListAsync();
        }
        return details;
    }

    private static IQueryable<QuizCardDto> ToCards(IQueryable<Quiz> query, Guid? userId) =>
        query.Select(x => new QuizCardDto
        {
            Id = x.Id, Title = x.Title, Description = x.Description, CoverImageUrl = x.CoverImageUrl, CategoryId = x.CategoryId,
            CategoryName = x.Category != null ? x.Category.Name : null, CategoryIcon = x.Category != null ? x.Category.Icon : null,
            Language = x.Language, QuestionCount = x.Questions.Count, PlayCount = x.PlayCount,
            OwnerName = x.Owner!.DisplayName, IsPublic = x.Visibility == QuizVisibility.Public,
            AdminLocked = x.AdminLocked, IsFavorite = userId != null && x.Favorites.Any(f => f.UserId == userId),
            IsOwner = userId != null && x.OwnerId == userId, UpdatedAt = x.UpdatedAt
        });

    private static async Task<PagedResult<QuizCardDto>> PageAsync(IQueryable<QuizCardDto> cards, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 60);
        var total = await cards.CountAsync();
        var items = await cards.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedResult<QuizCardDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    private static string Like(string s) => "%" + s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";

    public Task<PagedResult<QuizCardDto>> ListMineAsync(Guid userId, string? search, int page, int pageSize = 12)
    {
        var q = _quizzes.QueryNoTracking().Where(x => x.OwnerId == userId);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => EF.Functions.Like(x.Title, Like(search.Trim()), "\\"));
        return PageAsync(ToCards(q.OrderByDescending(x => x.UpdatedAt), userId), page, pageSize);
    }

    public Task<PagedResult<QuizCardDto>> ListFavoritesAsync(Guid userId, int page, int pageSize = 12)
    {
        var q = _quizzes.QueryNoTracking().Where(x => x.Favorites.Any(f => f.UserId == userId)
            && (x.Visibility == QuizVisibility.Public || x.OwnerId == userId));
        return PageAsync(ToCards(q.OrderByDescending(x => x.UpdatedAt), userId), page, pageSize);
    }

    public Task<PagedResult<QuizCardDto>> SearchPublicAsync(LibraryQueryDto query, Guid? userId)
    {
        var q = _quizzes.QueryNoTracking().Where(x => x.Visibility == QuizVisibility.Public && x.Questions.Any());
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = Like(query.Search.Trim());
            q = q.Where(x => EF.Functions.Like(x.Title, like, "\\") || (x.Description != null && EF.Functions.Like(x.Description, like, "\\")));
        }
        if (query.CategoryId.HasValue) q = q.Where(x => x.CategoryId == query.CategoryId);
        if (!string.IsNullOrWhiteSpace(query.Language)) q = q.Where(x => x.Language == query.Language);
        q = query.Sort == "new"
            ? q.OrderByDescending(x => x.UpdatedAt)
            : q.OrderByDescending(x => x.PlayCount).ThenByDescending(x => x.UpdatedAt);
        return PageAsync(ToCards(q, userId), query.Page, query.PageSize);
    }

    // ---------- Dəyişdirmə ----------

    public async Task<ServiceResult> DeleteAsync(Guid quizId, Guid userId)
    {
        var quiz = await _quizzes.Query().FirstOrDefaultAsync(x => x.Id == quizId && x.OwnerId == userId);
        if (quiz == null) return ServiceResult.Fail("Kviz tapılmadı.");
        _quizzes.Remove(quiz);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<Guid>> CopyAsync(Guid quizId, Guid userId)
    {
        var src = await _quizzes.GetFullNoTrackingAsync(quizId);
        if (src == null || (src.OwnerId != userId && src.Visibility != QuizVisibility.Public))
            return ServiceResult<Guid>.Fail("Kviz tapılmadı.");

        var copy = new Quiz
        {
            Id = Guid.NewGuid(), OwnerId = userId, Title = src.OwnerId == userId ? Truncate(src.Title + " (surət)", 120) : src.Title,
            Description = src.Description, CoverImageUrl = src.CoverImageUrl, CategoryId = src.CategoryId, Language = src.Language,
            Visibility = QuizVisibility.Private, CopiedFromQuizId = src.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        foreach (var q in src.Questions.OrderBy(x => x.Order))
        {
            var nq = new Question
            {
                Id = Guid.NewGuid(), QuizId = copy.Id, Order = q.Order, Type = q.Type, Text = q.Text, ImageUrl = q.ImageUrl, RelatedImageUrl = q.RelatedImageUrl, RelatedImageCredit = q.RelatedImageCredit,
                TimeLimitSeconds = q.TimeLimitSeconds, PointsMode = q.PointsMode, MultiSelect = q.MultiSelect,
                SliderMin = q.SliderMin, SliderMax = q.SliderMax, SliderStep = q.SliderStep, SliderCorrect = q.SliderCorrect,
                SliderTolerancePercent = q.SliderTolerancePercent
            };
            foreach (var o in q.Options.OrderBy(x => x.Order))
                nq.Options.Add(new AnswerOption { Id = Guid.NewGuid(), QuestionId = nq.Id, Order = o.Order, Text = o.Text, IsCorrect = o.IsCorrect });
            copy.Questions.Add(nq);
        }
        await _quizzes.AddAsync(copy);
        await _uow.SaveChangesAsync();
        return ServiceResult<Guid>.Ok(copy.Id);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public async Task<ServiceResult> SetVisibilityAsync(Guid quizId, Guid userId, bool isPublic)
    {
        var quiz = await _quizzes.Query().Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == quizId && x.OwnerId == userId);
        if (quiz == null) return ServiceResult.Fail("Kviz tapılmadı.");
        if (isPublic && quiz.AdminLocked) return ServiceResult.Fail("Bu kviz administrator tərəfindən hamıya açıqlıqdan çıxarılıb və yenidən açıla bilməz.");
        if (isPublic && quiz.Questions.Count == 0) return ServiceResult.Fail("Boş kvizi hamıya açmaq olmaz.");
        quiz.Visibility = isPublic ? QuizVisibility.Public : QuizVisibility.Private;
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<bool> ToggleFavoriteAsync(Guid quizId, Guid userId)
    {
        var fav = await _favorites.Query().FirstOrDefaultAsync(f => f.QuizId == quizId && f.UserId == userId);
        if (fav != null)
        {
            _favorites.Remove(fav);
            await _uow.SaveChangesAsync();
            return false;
        }
        var ok = await _quizzes.QueryNoTracking().AnyAsync(x => x.Id == quizId && (x.Visibility == QuizVisibility.Public || x.OwnerId == userId));
        if (!ok) return false;
        await _favorites.AddAsync(new QuizFavorite { QuizId = quizId, UserId = userId });
        await _uow.SaveChangesAsync();
        return true;
    }
}
