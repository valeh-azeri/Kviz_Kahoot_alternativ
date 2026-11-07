using Kviz.Business.Abstractions;
using Kviz.Business.Games;
using Kviz.Business.Models;
using Kviz.DataAccess.Repositories;
using Kviz.Entities;
using Kviz.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kviz.Business.Services;

/// <summary>Tapşırıq rejimi: oyunçular sualları müəllimsiz, öz tempi ilə keçir. Hər şey bazada saxlanılır.</summary>
public class ChallengeService : IChallengeService
{
    private readonly IGameRepository _games;
    private readonly IRepository<GamePlayer> _players;
    private readonly IRepository<PlayerAnswer> _answers;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _time;
    private readonly GameTimingOptions _opt;

    public ChallengeService(IGameRepository games, IRepository<GamePlayer> players, IRepository<PlayerAnswer> answers,
        IUnitOfWork uow, TimeProvider time, GameTimingOptions opt)
    {
        _games = games;
        _players = players;
        _answers = answers;
        _uow = uow;
        _time = time;
        _opt = opt;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public async Task<ChallengeJoinResultDto> JoinAsync(string pin, string nickname)
    {
        pin = new string((pin ?? string.Empty).Where(char.IsDigit).ToArray());
        var game = await _games.Query().FirstOrDefaultAsync(g => g.Pin == pin && g.Mode == GameMode.Challenge && g.Status == GameStatus.InProgress);
        if (game == null) return new() { Error = "Bu PIN ilə tapşırıq tapılmadı." };
        if (game.Deadline.HasValue && game.Deadline <= Now) return new() { Error = "Bu tapşırığın vaxtı bitib." };

        var name = string.Join(' ', (nickname ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length is < 1 or > 20) return new() { Error = "Ləqəb 1-20 simvol olmalıdır." };
        var norm = TextNormalizer.Normalize(name);
        var existing = await _players.QueryNoTracking().Where(p => p.GameSessionId == game.Id).Select(p => p.Nickname).ToListAsync();
        if (existing.Any(n => TextNormalizer.Normalize(n) == norm))
            return new() { Error = "Bu ləqəb artıq istifadə olunur. Başqa ad seçin." };

        var player = new GamePlayer { Id = Guid.NewGuid(), GameSessionId = game.Id, Nickname = name, Token = Guid.NewGuid(), JoinedAt = Now };
        await _players.AddAsync(player);
        await _uow.SaveChangesAsync();
        return new() { Success = true, Token = player.Token };
    }

    private sealed record Loaded(GameSession Game, GamePlayer Player, List<GameQuestionData> Questions);

    private async Task<Loaded?> LoadAsync(Guid token)
    {
        var player = await _players.Query().FirstOrDefaultAsync(p => p.Token == token);
        if (player == null || player.IsKicked) return null;
        var game = await _games.Query().Include(g => g.Questions).FirstOrDefaultAsync(g => g.Id == player.GameSessionId);
        if (game == null) return null;
        var qs = game.Questions.OrderBy(q => q.Order).Select(GameQuestionData.FromEntity).ToList();
        return new Loaded(game, player, qs);
    }

    private bool IsOpen(GameSession g) => g.Status == GameStatus.InProgress && (!g.Deadline.HasValue || g.Deadline > Now);

    public async Task<ChallengeStateDto?> GetStateAsync(Guid token)
    {
        var l = await LoadAsync(token);
        if (l == null) return null;
        var (game, p, qs) = (l.Game, l.Player, l.Questions);

        if (game.Status == GameStatus.InProgress && game.Deadline.HasValue && game.Deadline <= Now)
        {
            var all = await _games.Query().Include(g => g.Players).FirstAsync(g => g.Id == game.Id);
            FinalizeRanks(all);
            all.Status = GameStatus.Finished;
            all.FinishedAt = Now;
            await _uow.SaveChangesAsync();
            game = all;
        }

        var state = await BaseStateAsync(game, p, qs);
        if (game.Status != GameStatus.InProgress)
        {
            state.Phase = "closed";
            state.Message = "Bu tapşırıq bağlanıb.";
            return state;
        }

        // Vaxtı keçmiş sualları avtomatik cavabsız say
        while (p.CurrentQuestionIndex < qs.Count && p.QuestionServedAt.HasValue
               && (Now - p.QuestionServedAt.Value).TotalMilliseconds > qs[p.CurrentQuestionIndex].TimeLimitSeconds * 1000 + _opt.GraceMs)
        {
            RecordAnswer(game, p, qs[p.CurrentQuestionIndex], null, 0);
        }

        if (p.CurrentQuestionIndex >= qs.Count)
        {
            if (!p.FinishedAt.HasValue) p.FinishedAt = Now;
            await _uow.SaveChangesAsync();
            state = await BaseStateAsync(game, p, qs);
            state.Phase = "finished";
            return state;
        }

        var q = qs[p.CurrentQuestionIndex];
        p.QuestionServedAt ??= Now;
        await _uow.SaveChangesAsync();

        state = await BaseStateAsync(game, p, qs);
        state.Phase = "question";
        state.Question = ResultBuilder.ToPublic(q, p.CurrentQuestionIndex, qs.Count);
        state.RemainingMs = (int)Math.Max(0, q.TimeLimitSeconds * 1000 - (Now - p.QuestionServedAt.Value).TotalMilliseconds);
        return state;
    }

    private async Task<ChallengeStateDto> BaseStateAsync(GameSession game, GamePlayer p, List<GameQuestionData> qs)
    {
        var count = await _players.QueryNoTracking().CountAsync(x => x.GameSessionId == game.Id && !x.IsKicked);
        var rank = await _players.QueryNoTracking().CountAsync(x => x.GameSessionId == game.Id && !x.IsKicked &&
            (x.TotalScore > p.TotalScore || (x.TotalScore == p.TotalScore && x.TotalResponseMs < p.TotalResponseMs))) + 1;
        var s = new ChallengeStateDto
        {
            QuizTitle = game.QuizTitle, Nickname = p.Nickname, PlayerId = p.Id, Score = p.TotalScore, Streak = p.Streak,
            Correct = p.CorrectCount, TotalQuestions = qs.Count, Rank = rank, PlayerCount = count, Deadline = game.Deadline,
            ShowLeaderboard = game.ShowLeaderboard
        };
        if (game.ShowLeaderboard) s.Leaderboard = await LeaderboardAsync(_games, game.Id, 10);
        return s;
    }

    public async Task<ChallengeFeedbackDto> AnswerAsync(Guid token, AnswerSubmission submission)
    {
        var l = await LoadAsync(token);
        if (l == null) return new() { Error = "Oyunçu tapılmadı." };
        var (game, p, qs) = (l.Game, l.Player, l.Questions);
        if (!IsOpen(game)) return new() { Error = "Bu tapşırıq bağlanıb." };
        if (p.CurrentQuestionIndex >= qs.Count) return new() { Error = "Bütün suallar cavablandırılıb." };
        if (!p.QuestionServedAt.HasValue) return new() { Error = "Sual hələ açılmayıb." };

        var q = qs[p.CurrentQuestionIndex];
        var elapsed = (int)(Now - p.QuestionServedAt.Value).TotalMilliseconds;
        string? data = null;
        if (elapsed <= q.TimeLimitSeconds * 1000 + _opt.GraceMs)
        {
            if (!AnswerEvaluator.TryNormalize(q, submission, out var d, out var err)) return new() { Error = err };
            data = d;
        }

        var fb = RecordAnswer(game, p, q, data, Math.Clamp(elapsed, 0, q.TimeLimitSeconds * 1000));
        await _uow.SaveChangesAsync();
        return fb;
    }

    /// <summary>Cavabı (və ya vaxt aşımını) qeyd edir, oyunçunu növbəti suala keçirir.</summary>
    private ChallengeFeedbackDto RecordAnswer(GameSession game, GamePlayer p, GameQuestionData q, string? data, int responseMs)
    {
        var answered = data != null;
        var correct = answered && AnswerEvaluator.IsCorrect(q, data!);
        int points = 0, bonus = 0;

        if (q.IsScored)
        {
            if (correct)
            {
                p.Streak++;
                p.MaxStreak = Math.Max(p.MaxStreak, p.Streak);
                p.CorrectCount++;
                if (q.AwardsPoints)
                {
                    points = ScoreCalculator.QuestionPoints(q.PointsMode, q.TimeLimitSeconds * 1000, responseMs);
                    bonus = game.StreakBonusEnabled ? ScoreCalculator.StreakBonus(p.Streak) : 0;
                }
            }
            else p.Streak = 0;
            p.TotalScore += points + bonus;
        }
        if (answered) { p.AnsweredCount++; p.TotalResponseMs += responseMs; }

        _answers.Add(new PlayerAnswer
        {
            Id = Guid.NewGuid(), GamePlayerId = p.Id, GameQuestionId = q.Id, AnswerData = data ?? string.Empty,
            Answered = answered, IsCorrect = correct, Points = points + bonus, ResponseTimeMs = responseMs, StreakAfter = p.Streak
        });

        var index = p.CurrentQuestionIndex;
        p.CurrentQuestionIndex++;
        p.QuestionServedAt = null;
        if (p.CurrentQuestionIndex >= game.Questions.Count) p.FinishedAt = Now;

        var stored = answered ? new List<StoredAnswer> { new(data!, correct, responseMs) } : new List<StoredAnswer>();
        return new ChallengeFeedbackDto
        {
            Accepted = true, Scored = q.IsScored, TimedOut = !answered, Correct = correct, Points = points, StreakBonus = bonus,
            Streak = p.Streak, Score = p.TotalScore, Reveal = ResultBuilder.Build(q, index, stored, 1)
        };
    }

    public static void FinalizeRanks(GameSession game)
    {
        var rank = 1;
        foreach (var p in game.Players.Where(x => !x.IsKicked).OrderByDescending(x => x.TotalScore).ThenBy(x => x.TotalResponseMs))
            p.FinalRank = rank++;
    }

    public static async Task<List<LeaderboardEntryDto>> LeaderboardAsync(IGameRepository games, Guid gameId, int take)
    {
        var rows = await games.QueryNoTracking().Where(g => g.Id == gameId)
            .SelectMany(g => g.Players).Where(p => !p.IsKicked)
            .OrderByDescending(p => p.TotalScore).ThenBy(p => p.TotalResponseMs)
            .Take(take).Select(p => new { p.Id, p.Nickname, p.TotalScore, p.Streak, p.CorrectCount }).ToListAsync();
        return rows.Select((r, i) => new LeaderboardEntryDto
        {
            PlayerId = r.Id, Rank = i + 1, Nickname = r.Nickname, Score = r.TotalScore, Streak = r.Streak, Correct = r.CorrectCount
        }).ToList();
    }
}
