using System.Globalization;
using Kviz.Business.Abstractions;
using Kviz.Business.Common;
using Kviz.Business.Games;
using Kviz.Business.Models;
using Kviz.DataAccess.Repositories;
using Kviz.Entities;
using Kviz.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kviz.Business.Services;

public class GameService : IGameService
{
    public static readonly string[] TeamNames = { "Qırmızılar", "Mavilər", "Sarılar", "Yaşıllar", "Bənövşəyilər", "Narıncılar" };

    private readonly IQuizRepository _quizzes;
    private readonly IGameRepository _games;
    private readonly IRepository<PlayerAnswer> _answers;
    private readonly IRepository<GamePlayer> _players;
    private readonly IAvatarService _avatars;
    private readonly IUnitOfWork _uow;
    private readonly ILiveGameEngine _engine;
    private readonly TimeProvider _time;

    public GameService(IQuizRepository quizzes, IGameRepository games, IRepository<PlayerAnswer> answers,
        IRepository<GamePlayer> players, IAvatarService avatars,
        IUnitOfWork uow, ILiveGameEngine engine, TimeProvider time)
    {
        _players = players;
        _avatars = avatars;
        _quizzes = quizzes;
        _games = games;
        _answers = answers;
        _uow = uow;
        _engine = engine;
        _time = time;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public async Task<ServiceResult<GameCreatedDto>> CreateAsync(Guid userId, CreateGameDto dto)
    {
        var quiz = await _quizzes.GetFullNoTrackingAsync(dto.QuizId);
        if (quiz == null || (quiz.OwnerId != userId && quiz.Visibility != QuizVisibility.Public))
            return ServiceResult<GameCreatedDto>.Fail("Kviz tapılmadı.");
        if (quiz.Questions.Count == 0) return ServiceResult<GameCreatedDto>.Fail("Boş kvizlə oyun başlatmaq olmaz.");

        var teamCount = dto.Mode == GameMode.LiveTeam ? Math.Clamp(dto.TeamCount, 2, TeamNames.Length) : 0;
        DateTime? deadline = null;
        if (dto.Mode == GameMode.Challenge)
        {
            deadline = dto.DeadlineUtc ?? Now.AddDays(7);
            if (deadline <= Now.AddMinutes(1)) return ServiceResult<GameCreatedDto>.Fail("Bitmə vaxtı gələcəkdə olmalıdır.", "deadline");
            if (deadline > Now.AddDays(90)) return ServiceResult<GameCreatedDto>.Fail("Tapşırıq ən çox 90 gün açıq qala bilər.", "deadline");
        }

        var pin = await GeneratePinAsync();
        if (pin == null) return ServiceResult<GameCreatedDto>.Fail("PIN yaratmaq mümkün olmadı, bir az sonra yenidən yoxlayın.");

        var game = new GameSession
        {
            Id = Guid.NewGuid(), QuizId = quiz.Id, QuizTitle = quiz.Title, HostId = userId, Pin = pin, Mode = dto.Mode,
            Status = dto.Mode == GameMode.Challenge ? GameStatus.InProgress : GameStatus.Lobby,
            CreatedAt = Now, StartedAt = dto.Mode == GameMode.Challenge ? Now : null, Deadline = deadline,
            ShuffleQuestions = dto.ShuffleQuestions, ShuffleAnswers = dto.ShuffleAnswers, ShowLeaderboard = dto.ShowLeaderboard && dto.LeaderboardEvery > 0, LeaderboardEvery = Math.Clamp(dto.LeaderboardEvery, 1, 50),
            HostRevealsBoard = dto.Mode != GameMode.Challenge && dto.HostRevealsBoard,
            AllowPhotoAvatars = dto.AllowPhotoAvatars,
            PowerUpsEnabled = dto.Mode != GameMode.Challenge && dto.PowerUpsEnabled,
            StreakBonusEnabled = dto.StreakBonusEnabled, TeamCount = teamCount
        };

        for (var t = 0; t < teamCount; t++)
            game.Teams.Add(new GameTeam { Id = Guid.NewGuid(), GameSessionId = game.Id, Order = t, Name = TeamNames[t], ColorIndex = t });

        var questions = quiz.Questions.OrderBy(q => q.Order).ToList();
        if (dto.ShuffleQuestions) questions = questions.OrderBy(_ => Random.Shared.Next()).ToList();
        var order = 0;
        foreach (var q in questions)
            game.Questions.Add(Snapshot(game.Id, order++, q, dto.ShuffleAnswers));

        await _games.AddAsync(game);
        var stored = await _quizzes.Query().FirstOrDefaultAsync(x => x.Id == quiz.Id);
        if (stored != null) stored.PlayCount++;
        await _uow.SaveChangesAsync();

        if (dto.Mode != GameMode.Challenge) await _engine.RegisterAsync(game.Id);
        return ServiceResult<GameCreatedDto>.Ok(new GameCreatedDto { GameId = game.Id, Pin = pin, Mode = dto.Mode });
    }

    /// <summary>Sualın oyun surətini çıxarır; variantların göstərilmə sırasını bir dəfə təyin edir.</summary>
    public static GameQuestion Snapshot(Guid gameId, int order, Question q, bool shuffleAnswers)
    {
        var options = q.Options.OrderBy(o => o.Order).Select((o, i) => new GameOptionData
        {
            Text = o.Text, IsCorrect = o.IsCorrect && q.Type != QuestionType.Puzzle, CorrectPosition = i
        }).ToList();

        if (q.Type == QuestionType.Puzzle && options.Count > 1)
        {
            var original = options.Select(o => o.Text).ToList();
            List<GameOptionData> shuffled;
            var tries = 0;
            do { shuffled = options.OrderBy(_ => Random.Shared.Next()).ToList(); tries++; }
            while (tries < 20 && shuffled.Select(o => o.Text).SequenceEqual(original));
            options = shuffled;
        }
        else if (shuffleAnswers && q.Type is QuestionType.MultipleChoice or QuestionType.Poll)
        {
            options = options.OrderBy(_ => Random.Shared.Next()).ToList();
        }

        return new GameQuestion
        {
            Id = Guid.NewGuid(), GameSessionId = gameId, Order = order, Type = q.Type, Text = q.Text, ImageUrl = q.ImageUrl, RelatedImageUrl = q.RelatedImageUrl, RelatedImageCredit = q.RelatedImageCredit,
            TimeLimitSeconds = q.TimeLimitSeconds, PointsMode = q.PointsMode, MultiSelect = q.MultiSelect,
            SliderMin = q.SliderMin, SliderMax = q.SliderMax, SliderStep = q.SliderStep, SliderCorrect = q.SliderCorrect,
            SliderTolerancePercent = q.SliderTolerancePercent, OptionsJson = GameQuestionData.SerializeOptions(options)
        };
    }

    private async Task<string?> GeneratePinAsync()
    {
        for (var i = 0; i < 30; i++)
        {
            var pin = Random.Shared.Next(100000, 1000000).ToString(CultureInfo.InvariantCulture);
            if (_engine.TryGetGameIdByPin(pin, out _)) continue;
            if (await _games.PinInUseAsync(pin)) continue;
            return pin;
        }
        return null;
    }

    public async Task<PinResolveDto> ResolvePinAsync(string pin)
    {
        pin = new string((pin ?? string.Empty).Where(char.IsDigit).ToArray());
        var result = new PinResolveDto { Kind = PinKind.NotFound, Pin = pin, Message = "Bu PIN ilə oyun tapılmadı." };
        if (pin.Length is < 4 or > 8) return result;

        if (_engine.TryGetGameIdByPin(pin, out var gid))
        {
            result.GameId = gid;
            if (_engine.IsPinActive(pin))
            {
                var g = await _games.QueryNoTracking().Where(x => x.Id == gid)
                    .Select(x => new { x.QuizTitle, x.Mode, x.AllowPhotoAvatars }).FirstOrDefaultAsync();
                result.Kind = PinKind.Live;
                result.AllowPhotoAvatars = g?.AllowPhotoAvatars ?? true;
                result.QuizTitle = g?.QuizTitle ?? string.Empty;
                result.IsTeamMode = g?.Mode == GameMode.LiveTeam;
                result.Message = null;
            }
            else
            {
                result.Kind = PinKind.Closed;
                result.Message = "Bu oyun artıq bitib.";
            }
            return result;
        }

        var game = await _games.QueryNoTracking()
            .FirstOrDefaultAsync(x => x.Pin == pin && x.Mode == GameMode.Challenge && x.Status == GameStatus.InProgress);
        if (game != null)
        {
            result.GameId = game.Id;
            result.QuizTitle = game.QuizTitle;
            if (game.Deadline.HasValue && game.Deadline <= Now) { result.Kind = PinKind.Closed; result.Message = "Bu tapşırığın vaxtı bitib."; }
            else { result.Kind = PinKind.Challenge; result.Message = null; }
        }
        return result;
    }

    public async Task<HostGameDto?> GetHostGameAsync(Guid gameId, Guid userId)
    {
        var g = await _games.QueryNoTracking().Where(x => x.Id == gameId && x.HostId == userId)
            .Select(x => new HostGameDto
            {
                GameId = x.Id, Pin = x.Pin, Mode = x.Mode, Status = x.Status, QuizTitle = x.QuizTitle,
                QuestionCount = x.Questions.Count, Deadline = x.Deadline, PowerUpsEnabled = x.PowerUpsEnabled
            }).FirstOrDefaultAsync();
        return g;
    }

    private static readonly QuestionType[] Scored =
    {
        QuestionType.MultipleChoice, QuestionType.TrueFalse, QuestionType.TypeAnswer, QuestionType.Slider, QuestionType.Puzzle
    };

    public async Task<PagedResult<GameHistoryItemDto>> ListHostGamesAsync(Guid userId, int page, int pageSize = 15)
    {
        page = Math.Max(1, page);
        var q = _games.QueryNoTracking().Where(g => g.HostId == userId);
        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(g => g.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(g => new
            {
                g.Id, g.QuizTitle, g.Pin, g.Mode, g.Status, g.CreatedAt,
                Players = g.Players.Count(p => !p.IsKicked),
                Correct = g.Players.Where(p => !p.IsKicked).Sum(p => (int?)p.CorrectCount) ?? 0,
                Questions = g.Questions.Count,
                ScoredQuestions = g.Questions.Count(x => Scored.Contains(x.Type))
            }).ToListAsync();

        return new PagedResult<GameHistoryItemDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = rows.Select(r => new GameHistoryItemDto
            {
                Id = r.Id, QuizTitle = r.QuizTitle, Pin = r.Pin, Mode = r.Mode, Status = r.Status, CreatedAt = r.CreatedAt,
                PlayerCount = r.Players, QuestionCount = r.Questions,
                AverageAccuracy = r.Players * r.ScoredQuestions == 0 ? 0 : r.Correct * 100.0 / (r.Players * r.ScoredQuestions)
            }).ToList()
        };
    }

    public async Task<ServiceResult> DeleteAsync(Guid gameId, Guid userId)
    {
        var g = await _games.Query().FirstOrDefaultAsync(x => x.Id == gameId && x.HostId == userId);
        if (g == null) return ServiceResult.Fail("Oyun tapılmadı.");
        if (g.Mode != GameMode.Challenge && g.Status is GameStatus.Lobby or GameStatus.InProgress)
            await _engine.CancelAsync(gameId, userId);

        // Oyunçuların yüklədiyi avatar şəkilləri də silinir (hesabatla birlikdə)
        var photos = await _players.QueryNoTracking().Where(p => p.GameSessionId == gameId && p.Avatar != null && p.Avatar.StartsWith("/uploads/"))
            .Select(p => p.Avatar).ToListAsync();
        await _answers.Query().Where(a => a.Player!.GameSessionId == gameId).ExecuteDeleteAsync();
        var fresh = await _games.Query().FirstOrDefaultAsync(x => x.Id == gameId);
        if (fresh != null) _games.Remove(fresh);
        await _uow.SaveChangesAsync();
        _avatars.DeletePhotos(photos);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> CloseChallengeAsync(Guid gameId, Guid userId)
    {
        var g = await _games.Query().Include(x => x.Players).FirstOrDefaultAsync(x => x.Id == gameId && x.HostId == userId && x.Mode == GameMode.Challenge);
        if (g == null) return ServiceResult.Fail("Tapşırıq tapılmadı.");
        if (g.Status != GameStatus.InProgress) return ServiceResult.Ok();
        ChallengeService.FinalizeRanks(g);
        g.Status = GameStatus.Finished;
        g.FinishedAt = Now;
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<List<LeaderboardEntryDto>> GetChallengeLeaderboardAsync(Guid gameId, Guid userId, int take = 50)
    {
        var ok = await _games.QueryNoTracking().AnyAsync(g => g.Id == gameId && g.HostId == userId);
        if (!ok) return new();
        return await ChallengeService.LeaderboardAsync(_games, gameId, take);
    }
}
