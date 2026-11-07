using System.Collections.Concurrent;
using Kviz.Business.Abstractions;
using Kviz.Business.Models;
using Kviz.DataAccess.Repositories;
using Kviz.Entities;
using Kviz.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kviz.Business.Games;

public class GameTimingOptions
{
    /// <summary>Sual göstərilib, variantlar hələ açılmamışdan əvvəl gözləmə (oxuma) müddəti.</summary>
    public int IntroMs { get; set; } = 3500;
    /// <summary>Vaxt bitəndən sonra gec gələn cavab üçün tolerans.</summary>
    public int GraceMs { get; set; } = 800;
    /// <summary>Oyun bitəndən sonra yaddaşda saxlanma müddəti (dəqiqə).</summary>
    public int RetentionMinutes { get; set; } = 30;
}

public interface ILiveGameEngine
{
    Task RegisterAsync(Guid gameId, CancellationToken ct = default);
    bool TryGetGameIdByPin(string pin, out Guid gameId);
    bool IsPinActive(string pin);

    Task<HostStateDto?> GetHostStateAsync(Guid gameId, Guid hostUserId);
    Task<AckDto> StartAsync(Guid gameId, Guid hostUserId);
    Task<AckDto> AdvanceAsync(Guid gameId, Guid hostUserId);
    Task<AckDto> FinishAsync(Guid gameId, Guid hostUserId);
    Task<AckDto> CancelAsync(Guid gameId, Guid hostUserId);
    Task<AckDto> KickAsync(Guid gameId, Guid hostUserId, Guid playerId);
    Task<AckDto> SetLockedAsync(Guid gameId, Guid hostUserId, bool locked);
    /// <summary>Host liderlər cədvəlini / podiumu oyunçulara açır (yalnız "host açır" rejimində).</summary>
    Task<AckDto> RevealAsync(Guid gameId, Guid hostUserId);

    Task<JoinResultDto> JoinAsync(string pin, string nickname, Guid? teamId, string? avatar = null);
    Task<JoinResultDto> RejoinAsync(Guid gameId, Guid token);
    /// <summary>Gözləmə otağında oyunçu hazır avatarlardan birini seçir.</summary>
    Task<AckDto> SetPresetAvatarAsync(Guid gameId, Guid playerId, string avatar);
    /// <summary>Şəkil yükləməzdən əvvəl: oyunçu tanınır, oyun gözləmə otağındadır və host şəkli qadağan etməyib.</summary>
    Task<AckDto> CanUploadPhotoAsync(Guid gameId, Guid playerId, Guid token);
    /// <summary>Yüklənmiş (kiçildilmiş) şəkli oyunçunun avatarı edir; köhnə şəkil faylı silinir.</summary>
    Task<AckDto> SetPhotoAvatarAsync(Guid gameId, Guid playerId, Guid token, string photoUrl);
    Task PlayerDisconnectedAsync(Guid gameId, Guid playerId);
    Task<AckDto> SubmitAnswerAsync(Guid gameId, Guid playerId, AnswerSubmission submission);
    Task<AckDto> UsePowerUpAsync(Guid gameId, Guid playerId, PowerUpFlags type);
}

internal sealed class PlayerRt
{
    public Guid Id;
    public string Nickname = string.Empty;
    public string? Avatar;
    public Guid? TeamId;
    public Guid Token;
    public int JoinOrder;
    public bool Connected = true;
    public bool Kicked;
    public int Score, Streak, MaxStreak, Correct, Answered, Rank, LastDelta;
    public long TotalMs;
    public PowerUpFlags Used;
    public bool DoubleArmed, ShieldArmed;
    public List<int> Hidden = new();
    public PlayerResultDto? LastResult;
}

internal sealed record AnswerRt(string Data, int ResponseMs);

internal sealed class TeamRt
{
    public Guid Id;
    public string Name = string.Empty;
    public int Color;
}

internal sealed class GameRt
{
    public Guid Id;
    public string Pin = string.Empty;
    public Guid HostId;
    public GameMode Mode;
    public string Title = string.Empty;
    public bool ShowLeaderboard, PowerUps, StreakBonus;
    /// <summary>Liderlər cədvəli hər neçə sualdan sonra göstərilir (1 = hər sualdan sonra).</summary>
    public int LeaderboardEvery = 1;
    /// <summary>Cədvəli oyunçulara host açır; Revealed = host artıq açıb (hər sualın nəticəsindən sonra sıfırlanır).</summary>
    public bool HostReveals, Revealed;
    public bool AllowPhotos = true;
    public List<GameQuestionData> Questions = new();
    public List<TeamRt> Teams = new();
    public List<PlayerRt> Players = new();
    public string Phase = "lobby";
    public int Index = -1;
    public DateTime PhaseEnd;
    public DateTime QStart, QEnd;
    public Dictionary<Guid, AnswerRt> Answers = new();
    public QuestionResultDto? LastResult;
    public bool Locked;
    public int PhaseToken;
    public CancellationTokenSource? Cts;
    public int NextJoinOrder;
    public readonly SemaphoreSlim Gate = new(1, 1);

    public IEnumerable<PlayerRt> Active => Players.Where(p => !p.Kicked);
    public GameQuestionData? Current => Index >= 0 && Index < Questions.Count ? Questions[Index] : null;
}

public sealed class LiveGameEngine : ILiveGameEngine
{
    private readonly ConcurrentDictionary<Guid, GameRt> _games = new();
    private readonly ConcurrentDictionary<string, Guid> _pins = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly IGameNotifier _notifier;
    private readonly TimeProvider _time;
    private readonly GameTimingOptions _opt;
    private readonly ILogger<LiveGameEngine> _log;

    public LiveGameEngine(IServiceScopeFactory scopes, IGameNotifier notifier, TimeProvider time,
        GameTimingOptions options, ILogger<LiveGameEngine> log)
    {
        _scopes = scopes;
        _notifier = notifier;
        _time = time;
        _opt = options;
        _log = log;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    // ---------- Qeydiyyat ----------

    public async Task RegisterAsync(Guid gameId, CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
        var game = await repo.GetFullAsync(gameId, ct) ?? throw new InvalidOperationException("Oyun tapılmadı.");

        var rt = new GameRt
        {
            Id = game.Id,
            Pin = game.Pin,
            HostId = game.HostId,
            Mode = game.Mode,
            Title = game.QuizTitle,
            ShowLeaderboard = game.ShowLeaderboard,
            LeaderboardEvery = Math.Max(1, game.LeaderboardEvery),
            HostReveals = game.HostRevealsBoard,
            AllowPhotos = game.AllowPhotoAvatars,
            PowerUps = game.PowerUpsEnabled,
            StreakBonus = game.StreakBonusEnabled,
            Questions = game.Questions.OrderBy(q => q.Order).Select(GameQuestionData.FromEntity).ToList(),
            Teams = game.Teams.OrderBy(t => t.Order).Select(t => new TeamRt { Id = t.Id, Name = t.Name, Color = t.ColorIndex }).ToList()
        };
        _games[rt.Id] = rt;
        _pins[rt.Pin] = rt.Id;
    }

    public bool TryGetGameIdByPin(string pin, out Guid gameId) => _pins.TryGetValue(pin, out gameId);

    public bool IsPinActive(string pin) =>
        _pins.TryGetValue(pin, out var id) && _games.TryGetValue(id, out var rt) && rt.Phase is not ("podium" or "closed");

    private GameRt? Get(Guid gameId) => _games.TryGetValue(gameId, out var rt) ? rt : null;

    private static async Task<T> Locked<T>(GameRt rt, Func<Task<T>> fn)
    {
        await rt.Gate.WaitAsync();
        try { return await fn(); }
        finally { rt.Gate.Release(); }
    }

    private GameRt? GetHostRt(Guid gameId, Guid hostUserId)
    {
        var rt = Get(gameId);
        return rt != null && rt.HostId == hostUserId ? rt : null;
    }

    // ---------- Host əmrləri ----------

    public async Task<HostStateDto?> GetHostStateAsync(Guid gameId, Guid hostUserId)
    {
        var rt = GetHostRt(gameId, hostUserId);
        if (rt == null) return null;
        return await Locked(rt, () => Task.FromResult<HostStateDto?>(BuildHostState(rt)));
    }

    public async Task<AckDto> StartAsync(Guid gameId, Guid hostUserId)
    {
        var rt = GetHostRt(gameId, hostUserId);
        if (rt == null) return AckDto.Fail("Oyun tapılmadı.");
        return await Locked(rt, async () =>
        {
            if (rt.Phase != "lobby") return AckDto.Fail("Oyun artıq başlayıb.");
            // Lobbidə bağlantısı kəsilmiş oyunçuları çıxarırıq.
            var ghosts = rt.Players.Where(p => !p.Connected && !p.Kicked).ToList();
            foreach (var g in ghosts) { rt.Players.Remove(g); }
            if (ghosts.Count > 0)
            {
                await DeletePlayersAsync(ghosts.Select(g => g.Id).ToList());
                DeleteAvatarFiles(ghosts.Select(g => g.Avatar));
            }

            if (!rt.Active.Any()) return AckDto.Fail("Başlamaq üçün ən azı bir oyunçu qoşulmalıdır.");

            await UpdateGameAsync(rt.Id, g => { g.Status = GameStatus.InProgress; g.StartedAt = Now; });
            await BeginQuestionCoreAsync(rt, 0);
            return AckDto.Success();
        });
    }

    public async Task<AckDto> AdvanceAsync(Guid gameId, Guid hostUserId)
    {
        var rt = GetHostRt(gameId, hostUserId);
        if (rt == null) return AckDto.Fail("Oyun tapılmadı.");
        return await Locked(rt, async () =>
        {
            switch (rt.Phase)
            {
                case "intro":
                    await OpenQuestionCoreAsync(rt);
                    break;
                case "question":
                    await CloseQuestionCoreAsync(rt);
                    break;
                case "results":
                    if (rt.Index >= rt.Questions.Count - 1) await ToPodiumCoreAsync(rt);
                    else if (LeaderboardDue(rt)) await ToLeaderboardCoreAsync(rt);
                    else await BeginQuestionCoreAsync(rt, rt.Index + 1);
                    break;
                case "leaderboard":
                    await BeginQuestionCoreAsync(rt, rt.Index + 1);
                    break;
                default:
                    return AckDto.Fail("İndi keçid mümkün deyil.");
            }
            return AckDto.Success();
        });
    }

    public async Task<AckDto> FinishAsync(Guid gameId, Guid hostUserId)
    {
        var rt = GetHostRt(gameId, hostUserId);
        if (rt == null) return AckDto.Fail("Oyun tapılmadı.");
        return await Locked(rt, async () =>
        {
            if (rt.Phase is "podium" or "closed") return AckDto.Success();
            if (rt.Phase == "lobby") { await CancelCoreAsync(rt); return AckDto.Success(); }
            if (rt.Phase == "question") await CloseQuestionCoreAsync(rt);
            await ToPodiumCoreAsync(rt);
            return AckDto.Success();
        });
    }

    public async Task<AckDto> CancelAsync(Guid gameId, Guid hostUserId)
    {
        var rt = GetHostRt(gameId, hostUserId);
        if (rt == null) return AckDto.Fail("Oyun tapılmadı.");
        return await Locked(rt, async () =>
        {
            if (rt.Phase is "podium" or "closed") return AckDto.Success();
            await CancelCoreAsync(rt);
            return AckDto.Success();
        });
    }

    public async Task<AckDto> KickAsync(Guid gameId, Guid hostUserId, Guid playerId)
    {
        var rt = GetHostRt(gameId, hostUserId);
        if (rt == null) return AckDto.Fail("Oyun tapılmadı.");
        return await Locked(rt, async () =>
        {
            var p = rt.Players.FirstOrDefault(x => x.Id == playerId && !x.Kicked);
            if (p == null) return AckDto.Fail("Oyunçu tapılmadı.");
            p.Kicked = true;
            await UpdatePlayerAsync(p.Id, e => e.IsKicked = true);
            await _notifier.ToPlayerAsync(p.Id, "kicked", new { });
            await _notifier.ToHostAsync(rt.Id, "playerLeft", new { id = p.Id });
            if (rt.Phase == "question") await TryCloseWhenAllAnsweredAsync(rt);
            else await PushCountsAsync(rt);
            return AckDto.Success();
        });
    }

    public async Task<AckDto> SetLockedAsync(Guid gameId, Guid hostUserId, bool locked)
    {
        var rt = GetHostRt(gameId, hostUserId);
        if (rt == null) return AckDto.Fail("Oyun tapılmadı.");
        return await Locked(rt, async () =>
        {
            rt.Locked = locked;
            await _notifier.ToHostAsync(rt.Id, "state", BuildHostState(rt));
            return AckDto.Success();
        });
    }

    public async Task<AckDto> RevealAsync(Guid gameId, Guid hostUserId)
    {
        var rt = GetHostRt(gameId, hostUserId);
        if (rt == null) return AckDto.Fail("Oyun tapılmadı.");
        return await Locked(rt, async () =>
        {
            if (!rt.HostReveals) return AckDto.Fail("Bu oyunda cədvəl oyunçulara avtomatik göstərilir.");
            if (rt.Phase is not ("leaderboard" or "podium")) return AckDto.Fail("Açıqlanacaq cədvəl hələ yoxdur.");
            if (rt.Revealed) return AckDto.Success();
            rt.Revealed = true;
            await BroadcastAsync(rt);
            return AckDto.Success();
        });
    }

    // ---------- Oyunçu əmrləri ----------

    public async Task<JoinResultDto> JoinAsync(string pin, string nickname, Guid? teamId, string? avatar = null)
    {
        if (!TryGetGameIdByPin(pin, out var gid) || Get(gid) is not { } rt)
            return new JoinResultDto { Error = "Bu PIN ilə oyun tapılmadı." };

        return await Locked(rt, async () =>
        {
            if (rt.Phase is "podium" or "closed") return new JoinResultDto { Error = "Bu oyun artıq bitib." };
            if (rt.Locked) return new JoinResultDto { Error = "Host oyuna qoşulmanı bağlayıb." };

            var name = string.Join(' ', (nickname ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (name.Length is < 1 or > 20) return new JoinResultDto { Error = "Ləqəb 1-20 simvol olmalıdır." };
            var norm = TextNormalizer.Normalize(name);
            if (rt.Players.Any(p => TextNormalizer.Normalize(p.Nickname) == norm))
                return new JoinResultDto { Error = "Bu ləqəb artıq istifadə olunur. Başqa ad seçin." };

            Guid? team = null;
            if (rt.Mode == GameMode.LiveTeam && rt.Teams.Count > 0)
            {
                if (teamId.HasValue && rt.Teams.Any(t => t.Id == teamId.Value)) team = teamId;
                else team = rt.Teams.OrderBy(t => rt.Active.Count(p => p.TeamId == t.Id)).ThenBy(_ => Random.Shared.Next()).First().Id;
            }

            var p = new PlayerRt
            {
                Id = Guid.NewGuid(),
                Token = Guid.NewGuid(),
                Nickname = name,
                // Yalnız siyahıdakı hazır avatar qəbul olunur; seçilməyibsə təsadüfi biri verilir
                Avatar = AvatarCatalog.IsPreset(avatar) ? avatar : AvatarCatalog.Random(),
                TeamId = team,
                JoinOrder = rt.NextJoinOrder++,
                Connected = true
            };
            rt.Players.Add(p);
            RecomputeRanks(rt);

            using (var scope = _scopes.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IRepository<GamePlayer>>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                await repo.AddAsync(new GamePlayer
                {
                    Id = p.Id, GameSessionId = rt.Id, TeamId = team, Nickname = name, Avatar = p.Avatar,
                    Token = p.Token, JoinedAt = Now
                });
                await uow.SaveChangesAsync();
            }

            await _notifier.ToHostAsync(rt.Id, "playerJoined", ToLobby(p));
            return new JoinResultDto
            {
                Success = true, GameId = rt.Id, PlayerId = p.Id, Token = p.Token, State = BuildPlayerState(rt, p)
            };
        });
    }

    public async Task<JoinResultDto> RejoinAsync(Guid gameId, Guid token)
    {
        var rt = Get(gameId);
        if (rt == null) return new JoinResultDto { Error = "Oyun tapılmadı və ya bitib." };
        return await Locked(rt, async () =>
        {
            var p = rt.Players.FirstOrDefault(x => x.Token == token);
            if (p == null) return new JoinResultDto { Error = "Oyunçu tapılmadı." };
            if (p.Kicked) return new JoinResultDto { Error = "Oyundan çıxarılmısınız." };
            p.Connected = true;
            await _notifier.ToHostAsync(rt.Id, "playerJoined", ToLobby(p));
            return new JoinResultDto { Success = true, GameId = rt.Id, PlayerId = p.Id, Token = p.Token, State = BuildPlayerState(rt, p) };
        });
    }

    // ---------- Avatar ----------

    public async Task<AckDto> SetPresetAvatarAsync(Guid gameId, Guid playerId, string avatar)
    {
        var rt = Get(gameId);
        if (rt == null) return AckDto.Fail("Oyun tapılmadı.");
        if (!AvatarCatalog.IsPreset(avatar)) return AckDto.Fail("Belə avatar yoxdur.");
        return await Locked(rt, async () =>
        {
            var p = rt.Players.FirstOrDefault(x => x.Id == playerId && !x.Kicked);
            if (p == null) return AckDto.Fail("Oyunçu tapılmadı.");
            if (rt.Phase != "lobby") return AckDto.Fail("Avatarı yalnız oyun başlamazdan əvvəl dəyişmək olar.");
            await ApplyAvatarAsync(rt, p, avatar);
            return AckDto.Success();
        });
    }

    public async Task<AckDto> CanUploadPhotoAsync(Guid gameId, Guid playerId, Guid token)
    {
        var rt = Get(gameId);
        if (rt == null) return AckDto.Fail("Oyun tapılmadı.");
        return await Locked(rt, () => Task.FromResult(CheckPhotoRights(rt, playerId, token)));
    }

    public async Task<AckDto> SetPhotoAvatarAsync(Guid gameId, Guid playerId, Guid token, string photoUrl)
    {
        var rt = Get(gameId);
        if (rt == null) return AckDto.Fail("Oyun tapılmadı.");
        if (!AvatarCatalog.IsPhoto(photoUrl)) return AckDto.Fail("Şəkil ünvanı yanlışdır.");
        return await Locked(rt, async () =>
        {
            var check = CheckPhotoRights(rt, playerId, token);
            if (!check.Ok) return check;
            var p = rt.Players.First(x => x.Id == playerId);
            await ApplyAvatarAsync(rt, p, photoUrl);
            return AckDto.Success();
        });
    }

    private static AckDto CheckPhotoRights(GameRt rt, Guid playerId, Guid token)
    {
        var p = rt.Players.FirstOrDefault(x => x.Id == playerId && x.Token == token && !x.Kicked);
        if (p == null) return AckDto.Fail("Oyunçu tapılmadı.");
        if (!rt.AllowPhotos) return AckDto.Fail("Host bu oyunda şəkil yükləməyi bağlayıb. Hazır avatarlardan seçin.");
        if (rt.Phase != "lobby") return AckDto.Fail("Avatarı yalnız oyun başlamazdan əvvəl dəyişmək olar.");
        return AckDto.Success();
    }

    private async Task ApplyAvatarAsync(GameRt rt, PlayerRt p, string avatar)
    {
        var old = p.Avatar;
        p.Avatar = avatar;
        await UpdatePlayerAsync(p.Id, e => e.Avatar = avatar);
        if (AvatarCatalog.IsPhoto(old) && old != avatar) DeleteAvatarFiles(new[] { old });
        await _notifier.ToHostAsync(rt.Id, "playerJoined", ToLobby(p)); // hostun siyahısında yenilənir
        await _notifier.ToPlayerAsync(p.Id, "state", BuildPlayerState(rt, p));
    }

    private void DeleteAvatarFiles(IEnumerable<string?> avatars)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            scope.ServiceProvider.GetService<Kviz.Business.Services.IAvatarService>()?.DeletePhotos(avatars);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Avatar faylı silinə bilmədi."); }
    }

    public async Task PlayerDisconnectedAsync(Guid gameId, Guid playerId)
    {
        var rt = Get(gameId);
        if (rt == null) return;
        await Locked(rt, async () =>
        {
            var p = rt.Players.FirstOrDefault(x => x.Id == playerId);
            if (p == null || p.Kicked) return true;
            p.Connected = false;
            await _notifier.ToHostAsync(rt.Id, "playerJoined", ToLobby(p));
            if (rt.Phase == "question") await TryCloseWhenAllAnsweredAsync(rt);
            return true;
        });
    }

    public async Task<AckDto> SubmitAnswerAsync(Guid gameId, Guid playerId, AnswerSubmission submission)
    {
        var rt = Get(gameId);
        if (rt == null) return AckDto.Fail("Oyun tapılmadı.");
        return await Locked(rt, async () =>
        {
            var p = rt.Players.FirstOrDefault(x => x.Id == playerId && !x.Kicked);
            if (p == null) return AckDto.Fail("Oyunçu tapılmadı.");
            if (rt.Phase != "question" || rt.Current is not { } q) return AckDto.Fail("Hazırda cavab qəbul olunmur.");
            var now = Now;
            if (now > rt.QEnd.AddMilliseconds(_opt.GraceMs)) return AckDto.Fail("Vaxt bitib.");
            if (rt.Answers.ContainsKey(p.Id)) return AckDto.Fail("Artıq cavab vermisiniz.");
            if (!AnswerEvaluator.TryNormalize(q, submission, out var data, out var err)) return AckDto.Fail(err);

            var ms = (int)Math.Clamp((now - rt.QStart).TotalMilliseconds, 0, q.TimeLimitSeconds * 1000);
            rt.Answers[p.Id] = new AnswerRt(data, ms);
            await PushCountsAsync(rt);
            await TryCloseWhenAllAnsweredAsync(rt);
            return AckDto.Success();
        });
    }

    public async Task<AckDto> UsePowerUpAsync(Guid gameId, Guid playerId, PowerUpFlags type)
    {
        var rt = Get(gameId);
        if (rt == null) return AckDto.Fail("Oyun tapılmadı.");
        return await Locked(rt, async () =>
        {
            var p = rt.Players.FirstOrDefault(x => x.Id == playerId && !x.Kicked);
            if (p == null) return AckDto.Fail("Oyunçu tapılmadı.");
            if (!rt.PowerUps) return AckDto.Fail("Bu oyunda güclər söndürülüb.");
            if (rt.Phase is not ("intro" or "question")) return AckDto.Fail("Güc yalnız sual zamanı istifadə olunur.");
            if (p.Used.HasFlag(type)) return AckDto.Fail("Bu gücü artıq istifadə etmisiniz.");
            if (rt.Answers.ContainsKey(p.Id)) return AckDto.Fail("Cavab verdikdən sonra güc istifadə etmək olmaz.");
            var q = rt.Current!;

            switch (type)
            {
                case PowerUpFlags.DoublePoints:
                    if (!q.AwardsPoints) return AckDto.Fail("Bu sual xal vermir.");
                    p.DoubleArmed = true;
                    break;
                case PowerUpFlags.StreakShield:
                    p.ShieldArmed = true;
                    break;
                case PowerUpFlags.FiftyFifty:
                    if (rt.Phase != "question") return AckDto.Fail("50/50 yalnız variantlar açıldıqdan sonra olur.");
                    if (q.Type is not (QuestionType.MultipleChoice or QuestionType.TrueFalse) || q.MultiSelect)
                        return AckDto.Fail("50/50 bu sual növündə işləmir.");
                    var wrong = q.Options.Select((o, i) => (o, i)).Where(x => !x.o.IsCorrect).Select(x => x.i).ToList();
                    if (wrong.Count < 2) return AckDto.Fail("50/50 üçün kifayət qədər variant yoxdur.");
                    var hideCount = Math.Min(wrong.Count - 1, Math.Max(1, q.Options.Count / 2));
                    p.Hidden = wrong.OrderBy(_ => Random.Shared.Next()).Take(hideCount).ToList();
                    break;
                default:
                    return AckDto.Fail("Naməlum güc.");
            }
            p.Used |= type;
            await UpdatePlayerAsync(p.Id, e => e.UsedPowerUps |= type);
            await _notifier.ToPlayerAsync(p.Id, "state", BuildPlayerState(rt, p));
            return AckDto.Success();
        });
    }

    // ---------- Mərhələ keçidləri (kilid altında çağırılır) ----------

    private async Task BeginQuestionCoreAsync(GameRt rt, int index)
    {
        rt.Index = index;
        rt.Phase = "intro";
        rt.Answers.Clear();
        rt.LastResult = null;
        foreach (var p in rt.Players) { p.Hidden = new(); p.LastResult = null; p.LastDelta = 0; }
        var token = ++rt.PhaseToken;
        rt.PhaseEnd = Now.AddMilliseconds(_opt.IntroMs);
        await BroadcastAsync(rt);
        Schedule(rt, _opt.IntroMs, token, OpenQuestionCoreAsync);
    }

    private async Task OpenQuestionCoreAsync(GameRt rt)
    {
        var q = rt.Current!;
        rt.Phase = "question";
        var token = ++rt.PhaseToken;
        rt.QStart = Now;
        rt.QEnd = rt.QStart.AddSeconds(q.TimeLimitSeconds);
        rt.PhaseEnd = rt.QEnd;
        await BroadcastAsync(rt);
        Schedule(rt, q.TimeLimitSeconds * 1000 + 300, token, CloseQuestionCoreAsync);
    }

    private async Task CloseQuestionCoreAsync(GameRt rt)
    {
        var q = rt.Current!;
        rt.PhaseToken++;
        CancelTimer(rt);
        var limitMs = q.TimeLimitSeconds * 1000;
        var stored = new List<StoredAnswer>();
        var rows = new List<PlayerAnswer>();
        var active = rt.Active.ToList();

        foreach (var p in active)
        {
            rt.Answers.TryGetValue(p.Id, out var a);
            var answered = a != null;
            var correct = false;
            int points = 0, bonus = 0;

            if (q.IsScored)
            {
                if (answered) correct = AnswerEvaluator.IsCorrect(q, a!.Data);
                if (correct)
                {
                    p.Streak++;
                    p.MaxStreak = Math.Max(p.MaxStreak, p.Streak);
                    p.Correct++;
                    if (q.AwardsPoints)
                    {
                        points = ScoreCalculator.QuestionPoints(q.PointsMode, limitMs, a!.ResponseMs);
                        if (p.DoubleArmed) points *= 2;
                        bonus = rt.StreakBonus ? ScoreCalculator.StreakBonus(p.Streak) : 0;
                    }
                }
                else if (p.ShieldArmed) p.ShieldArmed = false; // qoruyucu seriyanı xilas etdi
                else p.Streak = 0;
                p.Score += points + bonus;
            }

            if (answered) { p.Answered++; p.TotalMs += a!.ResponseMs; stored.Add(new StoredAnswer(a.Data, correct, a.ResponseMs)); }
            p.DoubleArmed = false;
            p.LastDelta = points + bonus;
            p.LastResult = new PlayerResultDto
            {
                Scored = q.IsScored, Answered = answered, Correct = correct, Points = points,
                StreakBonus = bonus, Streak = p.Streak, Score = p.Score
            };

            rows.Add(new PlayerAnswer
            {
                Id = Guid.NewGuid(), GamePlayerId = p.Id, GameQuestionId = q.Id,
                AnswerData = a?.Data ?? string.Empty, Answered = answered, IsCorrect = correct,
                Points = points + bonus, ResponseTimeMs = a?.ResponseMs ?? 0, StreakAfter = p.Streak
            });
        }

        RecomputeRanks(rt);
        var ordered = rt.Active.OrderBy(p => p.Rank).ToList();
        foreach (var p in active)
        {
            var r = p.LastResult!;
            r.Rank = p.Rank;
            var ahead = ordered.FirstOrDefault(o => o.Rank == p.Rank - 1);
            if (ahead != null) { r.PointsBehind = ahead.Score - p.Score; r.NextNickname = ahead.Nickname; }
        }

        rt.LastResult = ResultBuilder.Build(q, rt.Index, stored, active.Count);
        if (rt.LastResult.OptionCounts.Count > 0) rt.LastResult.OptionVoters = BuildVoters(rt, q, active);
        rt.Revealed = false; // yeni nəticələr: "host açır" rejimində yer və xal yenidən gizlənir
        rt.Phase = "results";
        rt.PhaseEnd = Now;

        await PersistRoundAsync(rt, rows, active);
        await BroadcastAsync(rt);
    }

    /// <summary>Hər variantı kimin seçdiyi (host üçün). Komanda rejimində komandalara görə qruplaşır.</summary>
    private static List<OptionVotersDto> BuildVoters(GameRt rt, GameQuestionData q, List<PlayerRt> active)
    {
        var list = Enumerable.Range(0, q.Options.Count).Select(_ => new OptionVotersDto()).ToList();
        foreach (var p in active.OrderBy(p => p.JoinOrder))
        {
            if (!rt.Answers.TryGetValue(p.Id, out var a)) continue;
            var team = rt.Mode == GameMode.LiveTeam && p.TeamId.HasValue ? rt.Teams.FirstOrDefault(t => t.Id == p.TeamId) : null;
            foreach (var i in AnswerEvaluator.ParseIndexes(a.Data).Distinct())
            {
                if (i < 0 || i >= list.Count) continue;
                if (team == null) { list[i].Players.Add(p.Nickname); continue; }
                var g = list[i].Teams.FirstOrDefault(t => t.Name == team.Name);
                if (g == null) { g = new VoterTeamDto { Name = team.Name, ColorIndex = team.Color }; list[i].Teams.Add(g); }
                g.Players.Add(p.Nickname);
            }
        }
        foreach (var v in list) v.Teams = v.Teams.OrderBy(t => rt.Teams.FindIndex(x => x.Name == t.Name)).ToList();
        return list;
    }

    /// <summary>Cari sualdan sonra liderlər cədvəli göstərilməlidirmi? (hər N sualdan bir; son sualdan sonra həmişə podium gəlir)</summary>
    private static bool LeaderboardDue(GameRt rt) =>
        rt.ShowLeaderboard && rt.Index < rt.Questions.Count - 1 && (rt.Index + 1) % Math.Max(1, rt.LeaderboardEvery) == 0;

    private async Task ToLeaderboardCoreAsync(GameRt rt)
    {
        rt.PhaseToken++;
        rt.Phase = "leaderboard";
        await BroadcastAsync(rt);
    }

    private async Task ToPodiumCoreAsync(GameRt rt)
    {
        rt.PhaseToken++;
        CancelTimer(rt);
        RecomputeRanks(rt);
        rt.Revealed = false; // "host açır" rejimində yekun nəticəni host elan edir
        rt.Phase = "podium";
        await PersistFinalAsync(rt);
        await BroadcastAsync(rt);
        ScheduleRemoval(rt);
    }

    private async Task CancelCoreAsync(GameRt rt)
    {
        rt.PhaseToken++;
        CancelTimer(rt);
        rt.Phase = "closed";
        await UpdateGameAsync(rt.Id, g => { g.Status = GameStatus.Cancelled; g.FinishedAt = Now; });
        await BroadcastAsync(rt);
        ScheduleRemoval(rt);
    }

    private async Task TryCloseWhenAllAnsweredAsync(GameRt rt)
    {
        if (rt.Phase != "question") return;
        var expected = rt.Active.Where(p => p.Connected).Select(p => p.Id).ToList();
        if (expected.Count > 0 && expected.All(id => rt.Answers.ContainsKey(id)))
            await CloseQuestionCoreAsync(rt);
    }

    // ---------- Taymer ----------

    private void Schedule(GameRt rt, int delayMs, int token, Func<GameRt, Task> action)
    {
        CancelTimer(rt);
        var cts = new CancellationTokenSource();
        rt.Cts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), _time, cts.Token);
                await rt.Gate.WaitAsync(cts.Token);
                try
                {
                    if (rt.PhaseToken != token) return;
                    await action(rt);
                }
                finally { rt.Gate.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _log.LogError(ex, "Oyun taymeri xətası (oyun {GameId})", rt.Id); }
        });
    }

    private static void CancelTimer(GameRt rt)
    {
        try { rt.Cts?.Cancel(); } catch (ObjectDisposedException) { }
        rt.Cts = null;
    }

    private void ScheduleRemoval(GameRt rt)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(_opt.RetentionMinutes), _time);
                _games.TryRemove(rt.Id, out _);
                _pins.TryRemove(rt.Pin, out _);
            }
            catch { /* söndürmə zamanı xəta vacib deyil */ }
        });
    }

    // ---------- Reytinq və state qurulması ----------

    private static void RecomputeRanks(GameRt rt)
    {
        var rank = 1;
        foreach (var p in rt.Active.OrderByDescending(p => p.Score).ThenBy(p => p.TotalMs).ThenBy(p => p.JoinOrder))
            p.Rank = rank++;
    }

    private LobbyPlayerDto ToLobby(PlayerRt p) => new() { Id = p.Id, Nickname = p.Nickname, Avatar = p.Avatar, TeamId = p.TeamId, Connected = p.Connected };

    private static List<TeamDto> BuildTeams(GameRt rt) => rt.Teams.Select(t => new TeamDto
    {
        Id = t.Id, Name = t.Name, ColorIndex = t.Color, MemberCount = rt.Active.Count(p => p.TeamId == t.Id)
    }).ToList();

    private static List<TeamScoreDto> BuildTeamBoard(GameRt rt)
    {
        var list = rt.Teams.Select(t =>
        {
            var members = rt.Active.Where(p => p.TeamId == t.Id).ToList();
            return new TeamScoreDto
            {
                TeamId = t.Id, Name = t.Name, ColorIndex = t.Color, Members = members.Count,
                Score = members.Count == 0 ? 0 : (int)Math.Round(members.Average(m => m.Score))
            };
        }).OrderByDescending(t => t.Score).ToList();
        for (var i = 0; i < list.Count; i++) list[i].Rank = i + 1;
        return list;
    }

    private static List<LeaderboardEntryDto> BuildLeaderboard(GameRt rt, int take) =>
        rt.Active.OrderBy(p => p.Rank).Take(take).Select(p =>
        {
            var team = p.TeamId.HasValue ? rt.Teams.FirstOrDefault(t => t.Id == p.TeamId) : null;
            return new LeaderboardEntryDto
            {
                PlayerId = p.Id, Rank = p.Rank, Nickname = p.Nickname, Avatar = p.Avatar, TeamName = team?.Name, TeamColor = team?.Color ?? 0,
                Score = p.Score, Delta = p.LastDelta, Streak = p.Streak, Correct = p.Correct
            };
        }).ToList();

    private int Remaining(GameRt rt) => rt.Phase is "intro" or "question"
        ? (int)Math.Max(0, (rt.PhaseEnd - Now).TotalMilliseconds) : 0;

    private HostStateDto BuildHostState(GameRt rt)
    {
        var s = new HostStateDto
        {
            Phase = rt.Phase, GameId = rt.Id, Pin = rt.Pin, Mode = rt.Mode, QuizTitle = rt.Title,
            TotalQuestions = rt.Questions.Count, QuestionIndex = rt.Index, Locked = rt.Locked, PowerUps = rt.PowerUps,
            ShowLeaderboard = rt.ShowLeaderboard, IsLastQuestion = rt.Index >= rt.Questions.Count - 1,
            LeaderboardNext = LeaderboardDue(rt), HostReveals = rt.HostReveals, Revealed = rt.Revealed,
            Teams = BuildTeams(rt), PlayerCount = rt.Active.Count(), RemainingMs = Remaining(rt),
            AnsweredCount = rt.Answers.Count
        };
        if (rt.Phase == "lobby") s.Players = rt.Active.Select(ToLobby).ToList();
        if (rt.Current is { } q && rt.Phase is "intro" or "question" or "results")
        {
            s.Question = ResultBuilder.ToPublic(q, rt.Index, rt.Questions.Count);
            if (rt.Phase == "intro") s.Question.Options = new();
        }
        if (rt.Phase == "results") s.Result = rt.LastResult;
        if (rt.Phase is "leaderboard" or "podium")
        {
            s.Leaderboard = BuildLeaderboard(rt, rt.Phase == "podium" ? 10 : 5);
            if (rt.Mode == GameMode.LiveTeam) s.TeamLeaderboard = BuildTeamBoard(rt);
        }
        return s;
    }

    private PlayerStateDto BuildPlayerState(GameRt rt, PlayerRt p)
    {
        var team = p.TeamId.HasValue ? rt.Teams.FirstOrDefault(t => t.Id == p.TeamId) : null;
        // "Host açır" rejimində yer, ümumi xal və cədvəl host açana qədər oyunçuya heç göndərilmir
        // (yalnız ekranda gizlətmək kifayət deyil: brauzerin developer alətlərindən oxunmasın).
        var concealed = rt.HostReveals && !rt.Revealed && rt.Phase != "lobby";
        var s = new PlayerStateDto
        {
            Phase = p.Kicked ? "closed" : rt.Phase, Pin = rt.Pin, QuizTitle = rt.Title, Mode = rt.Mode,
            TotalQuestions = rt.Questions.Count, PlayerCount = rt.Active.Count(), PowerUps = rt.PowerUps,
            RemainingMs = Remaining(rt),
            Answered = rt.Answers.ContainsKey(p.Id),
            HiddenOptions = p.Hidden,
            Concealed = concealed,
            AllowPhotoAvatars = rt.AllowPhotos,
            You = new PlayerYouDto
            {
                Id = p.Id, Nickname = p.Nickname, Avatar = p.Avatar, TeamId = p.TeamId, TeamName = team?.Name, TeamColor = team?.Color ?? 0,
                Score = concealed ? 0 : p.Score, Rank = concealed ? 0 : p.Rank, Streak = p.Streak, Correct = concealed ? 0 : p.Correct,
                DoubleUsed = p.Used.HasFlag(PowerUpFlags.DoublePoints), FiftyUsed = p.Used.HasFlag(PowerUpFlags.FiftyFifty),
                ShieldUsed = p.Used.HasFlag(PowerUpFlags.StreakShield),
                DoubleArmed = p.DoubleArmed, ShieldArmed = p.ShieldArmed
            }
        };
        if (rt.Current is { } q && rt.Phase is "intro" or "question" or "results")
        {
            s.Question = ResultBuilder.ToPublic(q, rt.Index, rt.Questions.Count);
            if (rt.Phase == "intro") s.Question.Options = new();
        }
        if (rt.Phase is "results" or "leaderboard" or "podium") s.Result = concealed ? WithoutStanding(p.LastResult) : p.LastResult;
        if (rt.Phase is "leaderboard" or "podium" && !concealed)
        {
            s.Leaderboard = BuildLeaderboard(rt, 5);
            if (rt.Mode == GameMode.LiveTeam) s.TeamLeaderboard = BuildTeamBoard(rt);
        }
        return s;
    }

    /// <summary>Sualın nəticəsi (düz/səhv, bu sualdan qazanılan xal) qalır; yer, fərq və ümumi xal çıxarılır.</summary>
    private static PlayerResultDto? WithoutStanding(PlayerResultDto? r) => r == null ? null : new PlayerResultDto
    {
        Scored = r.Scored, Answered = r.Answered, Correct = r.Correct, Points = r.Points,
        StreakBonus = r.StreakBonus, Streak = r.Streak
    };

    private async Task BroadcastAsync(GameRt rt)
    {
        await _notifier.ToHostAsync(rt.Id, "state", BuildHostState(rt));
        foreach (var p in rt.Active.ToList())
            await _notifier.ToPlayerAsync(p.Id, "state", BuildPlayerState(rt, p));
    }

    private Task PushCountsAsync(GameRt rt) =>
        _notifier.ToHostAsync(rt.Id, "answerCount", new { answered = rt.Answers.Count, total = rt.Active.Count(), players = rt.Active.Count() });

    // ---------- Bazaya yazma ----------

    private async Task UpdateGameAsync(Guid gameId, Action<GameSession> change)
    {
        using var scope = _scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<GameSession>>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var g = await repo.FindAsync(gameId);
        if (g == null) return;
        change(g);
        await uow.SaveChangesAsync();
    }

    private async Task UpdatePlayerAsync(Guid playerId, Action<GamePlayer> change)
    {
        using var scope = _scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<GamePlayer>>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var p = await repo.FindAsync(playerId);
        if (p == null) return;
        change(p);
        await uow.SaveChangesAsync();
    }

    private async Task DeletePlayersAsync(List<Guid> ids)
    {
        using var scope = _scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<GamePlayer>>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var list = await repo.Query().Where(p => ids.Contains(p.Id)).ToListAsync();
        repo.RemoveRange(list);
        await uow.SaveChangesAsync();
    }

    private async Task PersistRoundAsync(GameRt rt, List<PlayerAnswer> rows, List<PlayerRt> active)
    {
        using var scope = _scopes.CreateScope();
        var players = scope.ServiceProvider.GetRequiredService<IRepository<GamePlayer>>();
        var answers = scope.ServiceProvider.GetRequiredService<IRepository<PlayerAnswer>>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var ids = active.Select(p => p.Id).ToList();
        var db = await players.Query().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        foreach (var p in active)
        {
            if (!db.TryGetValue(p.Id, out var e)) continue;
            e.TotalScore = p.Score; e.CorrectCount = p.Correct; e.AnsweredCount = p.Answered;
            e.Streak = p.Streak; e.MaxStreak = p.MaxStreak; e.TotalResponseMs = p.TotalMs; e.FinalRank = p.Rank;
        }
        await answers.AddRangeAsync(rows.Where(r => db.ContainsKey(r.GamePlayerId)));
        await uow.SaveChangesAsync();
    }

    private async Task PersistFinalAsync(GameRt rt)
    {
        using var scope = _scopes.CreateScope();
        var players = scope.ServiceProvider.GetRequiredService<IRepository<GamePlayer>>();
        var games = scope.ServiceProvider.GetRequiredService<IRepository<GameSession>>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var db = await players.Query().Where(p => p.GameSessionId == rt.Id).ToListAsync();
        foreach (var e in db)
        {
            var p = rt.Players.FirstOrDefault(x => x.Id == e.Id);
            if (p != null && !p.Kicked) e.FinalRank = p.Rank;
        }
        var g = await games.FindAsync(rt.Id);
        if (g != null) { g.Status = GameStatus.Finished; g.FinishedAt = Now; }
        await uow.SaveChangesAsync();
    }
}
