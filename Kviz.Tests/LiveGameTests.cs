using Kviz.Business.Abstractions;
using Kviz.Business.Games;
using Kviz.Business.Models;
using Kviz.DataAccess;
using Kviz.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kviz.Tests;

public class LiveGameTests
{
    private static AnswerSubmission Pick(int i) => new() { Choices = new[] { i } };

    [Fact]
    public async Task Full_game_scores_ranks_and_persists()
    {
        using var h = new TestHost();
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 1), TestHost.Mc("S2", 2), TestHost.Mc("S3", 0));
        var game = await h.CreateGameAsync(quiz);

        var a = await h.Engine.JoinAsync(game.Pin, "Aysel", null);
        var b = await h.Engine.JoinAsync(game.Pin, "Rəşad", null);
        Assert.True(a.Success && b.Success);

        Assert.True((await h.Engine.StartAsync(game.GameId, h.UserId)).Ok);

        // 1-ci sual: Aysel düzgün, Rəşad yanlış
        await h.WaitHostPhase(game.GameId, "question");
        await h.Engine.SubmitAnswerAsync(game.GameId, a.PlayerId, Pick(1));
        await h.Engine.SubmitAnswerAsync(game.GameId, b.PlayerId, Pick(3));
        var results = await h.WaitHostPhase(game.GameId, "results"); // hamı cavab verdi, vaxt gözlənilmədən bağlandı
        Assert.Equal(1, results.Result!.Correct);
        Assert.Equal(new[] { 0, 1, 0, 1 }.Length, results.Result.OptionCounts.Count);
        Assert.Equal(new[] { 1 }, results.Result.CorrectIndexes);

        var pa = h.Notifier.LastState<PlayerStateDto>("player", a.PlayerId)!;
        Assert.True(pa.Result!.Correct);
        Assert.InRange(pa.Result.Points, 900, 1000);
        Assert.Equal(1, pa.Result.Rank);
        var pb = h.Notifier.LastState<PlayerStateDto>("player", b.PlayerId)!;
        Assert.False(pb.Result!.Correct);
        Assert.Equal(0, pb.Result.Points);
        Assert.True(pb.Result.PointsBehind > 0);

        // Liderlər cədvəli → 2-ci sual: ikisi də düzgün, Aysel-in seriyası 2 olur (bonus 100)
        Assert.True((await h.Engine.AdvanceAsync(game.GameId, h.UserId)).Ok);
        var lb = await h.WaitHostPhase(game.GameId, "leaderboard");
        Assert.Equal("Aysel", lb.Leaderboard[0].Nickname);
        await h.Engine.AdvanceAsync(game.GameId, h.UserId);
        await h.WaitHostPhase(game.GameId, "question");
        await h.Engine.SubmitAnswerAsync(game.GameId, a.PlayerId, Pick(2));
        await h.Engine.SubmitAnswerAsync(game.GameId, b.PlayerId, Pick(2));
        await h.WaitHostPhase(game.GameId, "results");
        pa = h.Notifier.LastState<PlayerStateDto>("player", a.PlayerId)!;
        Assert.Equal(100, pa.Result!.StreakBonus);
        Assert.Equal(2, pa.Result.Streak);

        // 3-cü (son) sual: heç kim cavab vermir, host keçir
        await h.Engine.AdvanceAsync(game.GameId, h.UserId);
        await h.Engine.AdvanceAsync(game.GameId, h.UserId);
        await h.WaitHostPhase(game.GameId, "question");
        await h.Engine.AdvanceAsync(game.GameId, h.UserId); // sualı bitir
        var last = await h.WaitHostPhase(game.GameId, "results");
        Assert.True(last.IsLastQuestion);
        Assert.Equal(0, last.Result!.Answered);
        pa = h.Notifier.LastState<PlayerStateDto>("player", a.PlayerId)!;
        Assert.Equal(0, pa.Result!.Streak); // cavab verilmədi, seriya sıfırlandı

        await h.Engine.AdvanceAsync(game.GameId, h.UserId);
        var podium = await h.WaitHostPhase(game.GameId, "podium");
        Assert.Equal("Aysel", podium.Leaderboard[0].Nickname);
        Assert.Equal(2, podium.Leaderboard.Count);

        // Bazada saxlanıb
        await h.WithScope(async sp =>
        {
            var db = sp.GetRequiredService<KvizDbContext>();
            var g = await db.GameSessions.Include(x => x.Players).FirstAsync(x => x.Id == game.GameId);
            Assert.Equal(GameStatus.Finished, g.Status);
            Assert.Equal(1, g.Players.First(p => p.Nickname == "Aysel").FinalRank);
            Assert.Equal(2, g.Players.First(p => p.Nickname == "Aysel").CorrectCount);
            Assert.Equal(6, await db.PlayerAnswers.CountAsync());
            return 0;
        });

        // Hesabat
        var report = await h.WithScope(sp => sp.GetRequiredService<IReportService>().GetReportAsync(game.GameId, h.UserId));
        Assert.NotNull(report);
        Assert.Equal(2, report!.PlayerCount);
        Assert.Equal(3, report.Questions.Count);
        Assert.Equal(50, report.Questions[0].CorrectPercent);
        Assert.Equal(100, report.Questions[1].CorrectPercent);
        Assert.Equal(0, report.Questions[2].CorrectPercent);
        Assert.Equal("Aysel", report.Players[0].Nickname);

        // Excel və PDF hazırlanır
        var xlsx = await h.WithScope(sp => sp.GetRequiredService<IReportService>().ExportExcelAsync(game.GameId, h.UserId));
        Assert.True(xlsx!.Length > 2000);
        var pdf = await h.WithScope(sp => sp.GetRequiredService<IReportService>().ExportPdfAsync(game.GameId, h.UserId));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf!, 0, 4));
    }

    [Theory]
    [InlineData(2, new[] { false, true, false, true, false })]    // 5 sual: 2-ci və 4-cü sualdan sonra; sonuncudan sonra podium
    [InlineData(1, new[] { true, true, true, true, false })]
    [InlineData(3, new[] { false, false, true, false, false })]
    [InlineData(0, new[] { false, false, false, false, false })]  // göstərmə: yalnız podium
    public async Task Leaderboard_appears_every_n_questions(int every, bool[] expectedAfterEachQuestion)
    {
        using var h = new TestHost(introMs: 5);
        var quiz = await h.CreateQuizAsync(Enumerable.Range(1, 5).Select(i => TestHost.Mc("S" + i, 0, seconds: 5)).ToArray());
        var game = await h.CreateGameAsync(quiz, tweak: d => d.LeaderboardEvery = every);
        var p = await h.Engine.JoinAsync(game.Pin, "A", null);
        await h.Engine.StartAsync(game.GameId, h.UserId);

        for (var i = 0; i < 5; i++)
        {
            await h.WaitHostPhase(game.GameId, "question");
            await h.Engine.SubmitAnswerAsync(game.GameId, p.PlayerId, Pick(0));
            var results = await h.WaitHostPhase(game.GameId, "results");
            Assert.Equal(expectedAfterEachQuestion[i], results.LeaderboardNext);   // host düyməsinin mətni də buna uyğundur
            await h.Engine.AdvanceAsync(game.GameId, h.UserId);
            if (i == 4) break;
            var next = expectedAfterEachQuestion[i] ? "leaderboard" : "intro";
            if (expectedAfterEachQuestion[i]) { await h.WaitHostPhase(game.GameId, "leaderboard"); await h.Engine.AdvanceAsync(game.GameId, h.UserId); }
            else Assert.Contains((await h.Engine.GetHostStateAsync(game.GameId, h.UserId))!.Phase, new[] { "intro", "question" });
            _ = next;
        }
        Assert.Equal("podium", (await h.WaitHostPhase(game.GameId, "podium")).Phase);
    }

    [Fact]
    public async Task Host_sees_who_picked_each_option_grouped_by_team()
    {
        using var h = new TestHost(introMs: 5);
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0));
        var game = await h.CreateGameAsync(quiz, GameMode.LiveTeam, d => d.TeamCount = 2);
        var teams = (await h.Engine.GetHostStateAsync(game.GameId, h.UserId))!.Teams;
        var a = await h.Engine.JoinAsync(game.Pin, "Aysel", teams[0].Id);
        var b = await h.Engine.JoinAsync(game.Pin, "Rəşad", teams[0].Id);
        var c = await h.Engine.JoinAsync(game.Pin, "Nigar", teams[1].Id);
        var d = await h.Engine.JoinAsync(game.Pin, "Tural", teams[1].Id); // cavab verməyəcək
        await h.Engine.StartAsync(game.GameId, h.UserId);
        await h.WaitHostPhase(game.GameId, "question");
        await h.Engine.SubmitAnswerAsync(game.GameId, a.PlayerId, Pick(0));
        await h.Engine.SubmitAnswerAsync(game.GameId, b.PlayerId, Pick(1));
        await h.Engine.SubmitAnswerAsync(game.GameId, c.PlayerId, Pick(0));
        await h.Engine.AdvanceAsync(game.GameId, h.UserId); // Tural cavab verməyib, host sualı bitirir
        var res = (await h.WaitHostPhase(game.GameId, "results")).Result!;

        Assert.Equal(4, res.OptionVoters.Count);
        var first = res.OptionVoters[0];
        Assert.Empty(first.Players);
        Assert.Equal(new[] { teams[0].Name, teams[1].Name }, first.Teams.Select(t => t.Name));   // komandaların öz sırası
        Assert.Equal(new[] { "Aysel" }, first.Teams[0].Players);
        Assert.Equal(new[] { "Nigar" }, first.Teams[1].Players);
        var second = res.OptionVoters[1];
        Assert.Equal(new[] { "Rəşad" }, Assert.Single(second.Teams).Players);
        Assert.Empty(res.OptionVoters[2].Teams);
        Assert.DoesNotContain(res.OptionVoters.SelectMany(v => v.Teams).SelectMany(t => t.Players), n => n == "Tural"); // cavab verməyən siyahıda yoxdur
        Assert.Equal(res.OptionCounts, res.OptionVoters.Select(v => v.Players.Count + v.Teams.Sum(t => t.Players.Count)).ToList());
    }

    [Fact]
    public async Task Host_sees_player_names_per_option_in_classic_mode_and_multi_select()
    {
        using var h = new TestHost(introMs: 5);
        var multi = TestHost.Mc("Çoxlu", 0);
        multi.MultiSelect = true;
        multi.Options[2].IsCorrect = true;
        var quiz = await h.CreateQuizAsync(multi);
        var game = await h.CreateGameAsync(quiz);
        var a = await h.Engine.JoinAsync(game.Pin, "Aysel", null);
        var b = await h.Engine.JoinAsync(game.Pin, "Rəşad", null);
        await h.Engine.StartAsync(game.GameId, h.UserId);
        await h.WaitHostPhase(game.GameId, "question");
        await h.Engine.SubmitAnswerAsync(game.GameId, a.PlayerId, new AnswerSubmission { Choices = new[] { 0, 2 } });
        await h.Engine.SubmitAnswerAsync(game.GameId, b.PlayerId, Pick(2));
        var res = (await h.WaitHostPhase(game.GameId, "results")).Result!;
        Assert.Equal(new[] { "Aysel" }, res.OptionVoters[0].Players);
        Assert.Equal(new[] { "Aysel", "Rəşad" }, res.OptionVoters[2].Players);
        Assert.Empty(res.OptionVoters[0].Teams);
    }

    [Fact]
    public async Task Host_reveals_leaderboard_to_players_only_when_asked()
    {
        using var h = new TestHost(introMs: 5);
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0), TestHost.Mc("S2", 0));
        var game = await h.CreateGameAsync(quiz, tweak: d => d.HostRevealsBoard = true);
        var a = await h.Engine.JoinAsync(game.Pin, "Aysel", null);
        var b = await h.Engine.JoinAsync(game.Pin, "Rəşad", null);
        await h.Engine.StartAsync(game.GameId, h.UserId);
        PlayerStateDto P(Guid id) => h.Notifier.LastState<PlayerStateDto>("player", id)!;

        // Sual nəticəsi: düz/səhv və qazanılan xal görünür, yer və ümumi xal yox
        await h.WaitHostPhase(game.GameId, "question");
        await h.Engine.SubmitAnswerAsync(game.GameId, a.PlayerId, Pick(0));
        await h.Engine.SubmitAnswerAsync(game.GameId, b.PlayerId, Pick(1));
        var results = await h.WaitHostPhase(game.GameId, "results");
        Assert.True(results.HostReveals);
        Assert.True(P(a.PlayerId).Concealed);
        Assert.True(P(a.PlayerId).Result!.Correct);
        Assert.True(P(a.PlayerId).Result!.Points > 0);
        Assert.Equal(0, P(a.PlayerId).Result!.Rank);
        Assert.Null(P(b.PlayerId).Result!.PointsBehind);
        Assert.Equal(0, P(a.PlayerId).You.Score);
        Assert.Equal(0, P(a.PlayerId).You.Rank);

        // Liderlər cədvəli: host görür, oyunçular yox
        await h.Engine.AdvanceAsync(game.GameId, h.UserId);
        var lb = await h.WaitHostPhase(game.GameId, "leaderboard");
        Assert.Equal("Aysel", lb.Leaderboard[0].Nickname);
        Assert.False(lb.Revealed);
        Assert.True(P(a.PlayerId).Concealed);
        Assert.Empty(P(a.PlayerId).Leaderboard);
        Assert.Empty(P(b.PlayerId).Leaderboard);

        // Host açır
        Assert.True((await h.Engine.RevealAsync(game.GameId, h.UserId)).Ok);
        Assert.True((await h.Engine.GetHostStateAsync(game.GameId, h.UserId))!.Revealed);
        Assert.False(P(a.PlayerId).Concealed);
        Assert.Equal("Aysel", P(a.PlayerId).Leaderboard[0].Nickname);
        Assert.Equal(1, P(a.PlayerId).You.Rank);
        Assert.True(P(a.PlayerId).You.Score > 0);

        // Növbəti sualın nəticəsində yenidən gizlənir
        await h.Engine.AdvanceAsync(game.GameId, h.UserId);
        await h.WaitHostPhase(game.GameId, "question");
        await h.Engine.AdvanceAsync(game.GameId, h.UserId);
        await h.WaitHostPhase(game.GameId, "results");
        Assert.True(P(a.PlayerId).Concealed);

        // Podium da əvvəl yalnız hosta görünür
        await h.Engine.AdvanceAsync(game.GameId, h.UserId);
        var podium = await h.WaitHostPhase(game.GameId, "podium");
        Assert.NotEmpty(podium.Leaderboard);
        Assert.True(P(a.PlayerId).Concealed);
        Assert.Empty(P(a.PlayerId).Leaderboard);
        Assert.True((await h.Engine.RevealAsync(game.GameId, h.UserId)).Ok);
        Assert.False(P(b.PlayerId).Concealed);
        Assert.Equal(2, P(b.PlayerId).Leaderboard.Count);
    }

    [Fact]
    public async Task Reveal_is_rejected_when_not_applicable_and_default_game_shows_everything()
    {
        using var h = new TestHost(introMs: 5);
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0), TestHost.Mc("S2", 0));
        var normal = await h.CreateGameAsync(quiz);
        var p = await h.Engine.JoinAsync(normal.Pin, "A", null);
        await h.Engine.StartAsync(normal.GameId, h.UserId);
        await h.WaitHostPhase(normal.GameId, "question");
        await h.Engine.SubmitAnswerAsync(normal.GameId, p.PlayerId, Pick(0));
        await h.WaitHostPhase(normal.GameId, "results");
        var s = h.Notifier.LastState<PlayerStateDto>("player", p.PlayerId)!;
        Assert.False(s.Concealed);
        Assert.Equal(1, s.Result!.Rank);
        Assert.False((await h.Engine.RevealAsync(normal.GameId, h.UserId)).Ok);   // bu oyunda "host açır" rejimi yoxdur

        var hidden = await h.CreateGameAsync(quiz, tweak: d => d.HostRevealsBoard = true);
        await h.Engine.JoinAsync(hidden.Pin, "B", null);
        Assert.False((await h.Engine.RevealAsync(hidden.GameId, h.UserId)).Ok);   // gözləmə otağında açıqlanası bir şey yoxdur
        Assert.False((await h.Engine.RevealAsync(hidden.GameId, Guid.NewGuid())).Ok); // başqasının oyununu idarə etmək olmaz
    }

    [Fact]
    public async Task Timer_closes_question_without_host()
    {
        using var h = new TestHost(introMs: 10);
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0, seconds: 5));
        var game = await h.CreateGameAsync(quiz);
        var p = await h.Engine.JoinAsync(game.Pin, "Tək", null);
        await h.Engine.StartAsync(game.GameId, h.UserId);
        await h.WaitHostPhase(game.GameId, "question");
        var started = DateTime.UtcNow;
        HostStateDto? s0 = null;
        await TestHost.WaitUntil(() => (s0 = h.Engine.GetHostStateAsync(game.GameId, h.UserId).Result)?.Phase == "results", 12000);
        Assert.InRange((DateTime.UtcNow - started).TotalSeconds, 4.5, 7);
        var s = h.Notifier.LastState<PlayerStateDto>("player", p.PlayerId)!;
        Assert.False(s.Result!.Answered);
    }

    [Fact]
    public async Task Join_rules_duplicate_name_locked_and_validation()
    {
        using var h = new TestHost();
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0));
        var game = await h.CreateGameAsync(quiz);

        Assert.True((await h.Engine.JoinAsync(game.Pin, "Aysel", null)).Success);
        Assert.False((await h.Engine.JoinAsync(game.Pin, "  aysel ", null)).Success);   // eyni ad
        Assert.False((await h.Engine.JoinAsync(game.Pin, "", null)).Success);
        Assert.False((await h.Engine.JoinAsync(game.Pin, new string('x', 21), null)).Success);
        Assert.False((await h.Engine.JoinAsync("000000", "Kimsə", null)).Success);

        await h.Engine.SetLockedAsync(game.GameId, h.UserId, true);
        var locked = await h.Engine.JoinAsync(game.Pin, "Gec", null);
        Assert.False(locked.Success);
        Assert.Contains("bağla", locked.Error);

        // Yalnız host idarə edə bilər
        Assert.False((await h.Engine.StartAsync(game.GameId, Guid.NewGuid())).Ok);
        Assert.Null(await h.Engine.GetHostStateAsync(game.GameId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Cannot_start_empty_lobby_and_kick_works()
    {
        using var h = new TestHost();
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0));
        var game = await h.CreateGameAsync(quiz);
        Assert.False((await h.Engine.StartAsync(game.GameId, h.UserId)).Ok);

        var p = await h.Engine.JoinAsync(game.Pin, "Çıxarılan", null);
        Assert.True((await h.Engine.KickAsync(game.GameId, h.UserId, p.PlayerId)).Ok);
        Assert.False((await h.Engine.StartAsync(game.GameId, h.UserId)).Ok);
        var rejoin = await h.Engine.RejoinAsync(game.GameId, p.Token);
        Assert.False(rejoin.Success);
    }

    [Fact]
    public async Task Rejoin_with_token_restores_state()
    {
        using var h = new TestHost();
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0));
        var game = await h.CreateGameAsync(quiz);
        var p = await h.Engine.JoinAsync(game.Pin, "Aysel", null);
        await h.Engine.PlayerDisconnectedAsync(game.GameId, p.PlayerId);
        var again = await h.Engine.RejoinAsync(game.GameId, p.Token);
        Assert.True(again.Success);
        Assert.Equal(p.PlayerId, again.PlayerId);
        Assert.Equal("lobby", again.State!.Phase);
        Assert.False((await h.Engine.RejoinAsync(game.GameId, Guid.NewGuid())).Success);
    }

    [Fact]
    public async Task Late_and_duplicate_answers_are_rejected()
    {
        using var h = new TestHost(introMs: 10);
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0), TestHost.Mc("S2", 0));
        var game = await h.CreateGameAsync(quiz);
        var a = await h.Engine.JoinAsync(game.Pin, "A", null);
        var b = await h.Engine.JoinAsync(game.Pin, "B", null);
        await h.Engine.StartAsync(game.GameId, h.UserId);

        // Sual açılmamışdan əvvəl cavab qəbul olunmur
        Assert.False((await h.Engine.SubmitAnswerAsync(game.GameId, a.PlayerId, Pick(0))).Ok);
        await h.WaitHostPhase(game.GameId, "question");
        Assert.True((await h.Engine.SubmitAnswerAsync(game.GameId, a.PlayerId, Pick(0))).Ok);
        Assert.False((await h.Engine.SubmitAnswerAsync(game.GameId, a.PlayerId, Pick(1))).Ok); // ikinci cavab
        Assert.False((await h.Engine.SubmitAnswerAsync(game.GameId, b.PlayerId, Pick(99))).Ok); // yanlış indeks
        await h.Engine.AdvanceAsync(game.GameId, h.UserId);
        await h.WaitHostPhase(game.GameId, "results");
        Assert.False((await h.Engine.SubmitAnswerAsync(game.GameId, b.PlayerId, Pick(0))).Ok); // vaxt bitib
    }

    [Fact]
    public async Task Power_ups_double_points_shield_and_fifty_fifty()
    {
        using var h = new TestHost(introMs: 10);
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0), TestHost.Mc("S2", 0));
        var game = await h.CreateGameAsync(quiz, tweak: d => d.PowerUpsEnabled = true);
        var a = await h.Engine.JoinAsync(game.Pin, "A", null);
        var b = await h.Engine.JoinAsync(game.Pin, "B", null);
        await h.Engine.StartAsync(game.GameId, h.UserId);
        await h.WaitHostPhase(game.GameId, "question");

        // 50/50: iki yanlış variant gizlənir, düzgün qalır
        Assert.True((await h.Engine.UsePowerUpAsync(game.GameId, a.PlayerId, PowerUpFlags.FiftyFifty)).Ok);
        var hidden = h.Notifier.LastState<PlayerStateDto>("player", a.PlayerId)!.HiddenOptions;
        Assert.Equal(2, hidden.Count);
        Assert.DoesNotContain(0, hidden);
        Assert.False((await h.Engine.UsePowerUpAsync(game.GameId, a.PlayerId, PowerUpFlags.FiftyFifty)).Ok); // yalnız bir dəfə

        Assert.True((await h.Engine.UsePowerUpAsync(game.GameId, a.PlayerId, PowerUpFlags.DoublePoints)).Ok);
        await h.Engine.SubmitAnswerAsync(game.GameId, a.PlayerId, Pick(0));
        await h.Engine.SubmitAnswerAsync(game.GameId, b.PlayerId, Pick(0));
        await h.WaitHostPhase(game.GameId, "results");
        var pa = h.Notifier.LastState<PlayerStateDto>("player", a.PlayerId)!;
        var pb = h.Notifier.LastState<PlayerStateDto>("player", b.PlayerId)!;
        Assert.True(pa.Result!.Points > pb.Result!.Points * 1.7, $"{pa.Result.Points} vs {pb.Result.Points}");

        // Qoruyucu: yanlış cavabda seriya qalır
        await h.Engine.AdvanceAsync(game.GameId, h.UserId);
        await h.Engine.AdvanceAsync(game.GameId, h.UserId);
        await h.WaitHostPhase(game.GameId, "question");
        Assert.True((await h.Engine.UsePowerUpAsync(game.GameId, b.PlayerId, PowerUpFlags.StreakShield)).Ok);
        await h.Engine.SubmitAnswerAsync(game.GameId, b.PlayerId, Pick(3));
        await h.Engine.SubmitAnswerAsync(game.GameId, a.PlayerId, Pick(3));
        await h.WaitHostPhase(game.GameId, "results");
        Assert.Equal(1, h.Notifier.LastState<PlayerStateDto>("player", b.PlayerId)!.Result!.Streak);
        Assert.Equal(0, h.Notifier.LastState<PlayerStateDto>("player", a.PlayerId)!.Result!.Streak);
    }

    [Fact]
    public async Task Power_ups_disabled_when_not_enabled()
    {
        using var h = new TestHost(introMs: 10);
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0));
        var game = await h.CreateGameAsync(quiz);
        var a = await h.Engine.JoinAsync(game.Pin, "A", null);
        await h.Engine.StartAsync(game.GameId, h.UserId);
        await h.WaitHostPhase(game.GameId, "question");
        Assert.False((await h.Engine.UsePowerUpAsync(game.GameId, a.PlayerId, PowerUpFlags.DoublePoints)).Ok);
    }

    [Fact]
    public async Task Team_mode_balances_players_and_averages_scores()
    {
        using var h = new TestHost(introMs: 10);
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0));
        var game = await h.CreateGameAsync(quiz, GameMode.LiveTeam, d => d.TeamCount = 2);
        var ps = new List<JoinResultDto>();
        foreach (var n in new[] { "A", "B", "C", "D" }) ps.Add(await h.Engine.JoinAsync(game.Pin, n, null));
        var lobby = await h.Engine.GetHostStateAsync(game.GameId, h.UserId);
        Assert.Equal(2, lobby!.Teams.Count);
        Assert.All(lobby.Teams, t => Assert.Equal(2, t.MemberCount));

        await h.Engine.StartAsync(game.GameId, h.UserId);
        await h.WaitHostPhase(game.GameId, "question");
        foreach (var p in ps) await h.Engine.SubmitAnswerAsync(game.GameId, p.PlayerId, Pick(0));
        await h.WaitHostPhase(game.GameId, "results");
        await h.Engine.AdvanceAsync(game.GameId, h.UserId); // sonuncu sual → podium
        var podium = await h.WaitHostPhase(game.GameId, "podium");
        Assert.Equal(2, podium.TeamLeaderboard.Count);
        Assert.True(podium.TeamLeaderboard[0].Score > 0);
    }

    [Fact]
    public async Task Disconnected_players_do_not_block_question_end()
    {
        using var h = new TestHost(introMs: 10);
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0, seconds: 30));
        var game = await h.CreateGameAsync(quiz);
        var a = await h.Engine.JoinAsync(game.Pin, "A", null);
        var b = await h.Engine.JoinAsync(game.Pin, "B", null);
        await h.Engine.StartAsync(game.GameId, h.UserId);
        await h.WaitHostPhase(game.GameId, "question");
        await h.Engine.PlayerDisconnectedAsync(game.GameId, b.PlayerId);
        await h.Engine.SubmitAnswerAsync(game.GameId, a.PlayerId, Pick(0));
        await h.WaitHostPhase(game.GameId, "results"); // 30 san gözləmədən
    }

    [Fact]
    public async Task Word_cloud_poll_and_open_questions_collect_without_points()
    {
        using var h = new TestHost(introMs: 10);
        var quiz = await h.CreateQuizAsync(
            new QuestionEditDto { Type = QuestionType.WordCloud, Text = "Bir söz", TimeLimitSeconds = 20 },
            new QuestionEditDto { Type = QuestionType.OpenEnded, Text = "Fikir", TimeLimitSeconds = 20 },
            new QuestionEditDto { Type = QuestionType.Slider, Text = "Say", TimeLimitSeconds = 20, SliderMin = 0, SliderMax = 100, SliderStep = 1, SliderCorrect = 40, SliderTolerancePercent = 5 });
        var game = await h.CreateGameAsync(quiz);
        var a = await h.Engine.JoinAsync(game.Pin, "A", null);
        var b = await h.Engine.JoinAsync(game.Pin, "B", null);
        await h.Engine.StartAsync(game.GameId, h.UserId);

        await h.WaitHostPhase(game.GameId, "question");
        await h.Engine.SubmitAnswerAsync(game.GameId, a.PlayerId, new AnswerSubmission { Text = "Əla" });
        await h.Engine.SubmitAnswerAsync(game.GameId, b.PlayerId, new AnswerSubmission { Text = "əla" });
        var r1 = await h.WaitHostPhase(game.GameId, "results");
        Assert.Equal("əla", r1.Result!.Words.Single().Word);
        Assert.Equal(2, r1.Result.Words.Single().Count);
        Assert.Equal(0, h.Notifier.LastState<PlayerStateDto>("player", a.PlayerId)!.Result!.Points);

        await h.Engine.AdvanceAsync(game.GameId, h.UserId); await h.Engine.AdvanceAsync(game.GameId, h.UserId);
        await h.WaitHostPhase(game.GameId, "question");
        await h.Engine.SubmitAnswerAsync(game.GameId, a.PlayerId, new AnswerSubmission { Text = "Çox yaxşı dərs idi" });
        await h.Engine.SubmitAnswerAsync(game.GameId, b.PlayerId, new AnswerSubmission { Text = "Normal" });
        var r2 = await h.WaitHostPhase(game.GameId, "results");
        Assert.Equal(2, r2.Result!.Texts.Count);

        await h.Engine.AdvanceAsync(game.GameId, h.UserId); await h.Engine.AdvanceAsync(game.GameId, h.UserId);
        await h.WaitHostPhase(game.GameId, "question");
        await h.Engine.SubmitAnswerAsync(game.GameId, a.PlayerId, new AnswerSubmission { Number = 42 });   // tolerans daxilində (±5)
        await h.Engine.SubmitAnswerAsync(game.GameId, b.PlayerId, new AnswerSubmission { Number = 60 });
        var r3 = await h.WaitHostPhase(game.GameId, "results");
        Assert.Equal(1, r3.Result!.Correct);
        Assert.Equal(2, r3.Result.SliderValues.Count);
    }
}
