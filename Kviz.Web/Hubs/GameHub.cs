using Kviz.Business.Games;
using Kviz.Business.Models;
using Kviz.Entities.Enums;
using Kviz.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Kviz.Web.Hubs;

/// <summary>Canlı oyun üçün real-vaxt kanalı. Bütün məntiq ILiveGameEngine-dədir.</summary>
public class GameHub : Hub
{
    private const string GameKey = "gid";
    private const string PlayerKey = "pid";

    private readonly ILiveGameEngine _engine;
    public GameHub(ILiveGameEngine engine) => _engine = engine;

    public static string HostGroup(Guid gameId) => $"host:{gameId}";
    public static string PlayerGroup(Guid playerId) => $"player:{playerId}";

    // ---------- Host ----------

    [Authorize]
    public async Task<HostStateDto?> HostConnect(Guid gameId)
    {
        var state = await _engine.GetHostStateAsync(gameId, Context.User!.GetUserId());
        if (state == null) return null;
        await Groups.AddToGroupAsync(Context.ConnectionId, HostGroup(gameId));
        return state;
    }

    [Authorize] public Task<AckDto> HostStart(Guid gameId) => _engine.StartAsync(gameId, Context.User!.GetUserId());
    [Authorize] public Task<AckDto> HostAdvance(Guid gameId) => _engine.AdvanceAsync(gameId, Context.User!.GetUserId());
    [Authorize] public Task<AckDto> HostReveal(Guid gameId) => _engine.RevealAsync(gameId, Context.User!.GetUserId());
    [Authorize] public Task<AckDto> HostFinish(Guid gameId) => _engine.FinishAsync(gameId, Context.User!.GetUserId());
    [Authorize] public Task<AckDto> HostCancel(Guid gameId) => _engine.CancelAsync(gameId, Context.User!.GetUserId());
    [Authorize] public Task<AckDto> HostKick(Guid gameId, Guid playerId) => _engine.KickAsync(gameId, Context.User!.GetUserId(), playerId);
    [Authorize] public Task<AckDto> HostLock(Guid gameId, bool locked) => _engine.SetLockedAsync(gameId, Context.User!.GetUserId(), locked);

    // ---------- Oyunçu ----------

    public async Task<JoinResultDto> PlayerJoin(string pin, string nickname, string? avatar)
    {
        var result = await _engine.JoinAsync((pin ?? string.Empty).Trim(), nickname ?? string.Empty, null, avatar);
        if (result.Success) await Attach(result);
        return result;
    }

    public async Task<JoinResultDto> PlayerRejoin(Guid gameId, Guid token)
    {
        var result = await _engine.RejoinAsync(gameId, token);
        if (result.Success) await Attach(result);
        return result;
    }

    private async Task Attach(JoinResultDto r)
    {
        Context.Items[GameKey] = r.GameId;
        Context.Items[PlayerKey] = r.PlayerId;
        await Groups.AddToGroupAsync(Context.ConnectionId, PlayerGroup(r.PlayerId));
    }

    public Task<AckDto> PlayerAnswer(AnswerSubmission answer)
    {
        if (!TryGetPlayer(out var gid, out var pid)) return Task.FromResult(AckDto.Fail("Əvvəlcə oyuna qoşulun."));
        return _engine.SubmitAnswerAsync(gid, pid, answer);
    }

    public Task<AckDto> PlayerSetAvatar(string avatar)
    {
        if (!TryGetPlayer(out var gid, out var pid)) return Task.FromResult(AckDto.Fail("Əvvəlcə oyuna qoşulun."));
        return _engine.SetPresetAvatarAsync(gid, pid, avatar ?? string.Empty);
    }

    public Task<AckDto> PlayerPowerUp(string type)
    {
        if (!TryGetPlayer(out var gid, out var pid)) return Task.FromResult(AckDto.Fail("Əvvəlcə oyuna qoşulun."));
        var flag = type switch
        {
            "double" => PowerUpFlags.DoublePoints,
            "fifty" => PowerUpFlags.FiftyFifty,
            "shield" => PowerUpFlags.StreakShield,
            _ => PowerUpFlags.None
        };
        return flag == PowerUpFlags.None ? Task.FromResult(AckDto.Fail("Naməlum güc.")) : _engine.UsePowerUpAsync(gid, pid, flag);
    }

    private bool TryGetPlayer(out Guid gid, out Guid pid)
    {
        gid = pid = Guid.Empty;
        if (Context.Items[GameKey] is Guid g && Context.Items[PlayerKey] is Guid p) { gid = g; pid = p; return true; }
        return false;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetPlayer(out var gid, out var pid)) await _engine.PlayerDisconnectedAsync(gid, pid);
        await base.OnDisconnectedAsync(exception);
    }
}
