using Kviz.Business.Abstractions;
using Kviz.Business.Games;
using Kviz.Business.Models;
using Kviz.Business.Services;
using Kviz.DataAccess;
using Kviz.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kviz.Tests;

public class QuizValidationTests
{
    private static QuizEditDto Valid() => new()
    {
        Title = "Ad",
        Questions = { TestHost.Mc("S", 1) }
    };

    [Fact]
    public void Valid_quiz_has_no_errors()
    {
        var q = Valid(); QuizServiceProxy.Normalize(q);
        Assert.Empty(QuizServiceProxy.Validate(q));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("questions[0].text")]
    [InlineData("questions[0].options")]
    public void Reports_errors_with_paths(string path)
    {
        var q = Valid();
        if (path == "title") q.Title = "  ";
        if (path.EndsWith("text")) q.Questions[0].Text = "";
        if (path.EndsWith("options")) q.Questions[0].Options.ForEach(o => o.IsCorrect = false);
        QuizServiceProxy.Normalize(q);
        var errors = QuizServiceProxy.Validate(q);
        Assert.Contains(errors, e => e.Path == path);
        Assert.All(errors, e => Assert.False(string.IsNullOrWhiteSpace(e.Message)));
    }

    [Fact]
    public void Single_select_cannot_have_two_correct_answers_but_multi_can()
    {
        var q = Valid();
        q.Questions[0].Options[0].IsCorrect = true;
        QuizServiceProxy.Normalize(q);
        Assert.NotEmpty(QuizServiceProxy.Validate(q));
        q.Questions[0].MultiSelect = true;
        Assert.Empty(QuizServiceProxy.Validate(q));
    }

    [Fact]
    public void Blank_options_are_dropped_and_minimum_enforced()
    {
        var q = Valid();
        q.Questions[0].Options = new() { new() { Text = "A", IsCorrect = true }, new() { Text = "  " }, new() { Text = "" } };
        QuizServiceProxy.Normalize(q);
        Assert.Single(q.Questions[0].Options);
        Assert.Contains(QuizServiceProxy.Validate(q), e => e.Path == "questions[0].options");
    }

    [Fact]
    public void Slider_requires_valid_range()
    {
        var q = new QuizEditDto
        {
            Title = "T",
            Questions = { new QuestionEditDto { Type = QuestionType.Slider, Text = "S", SliderMin = 10, SliderMax = 5, SliderStep = 1, SliderCorrect = 7 } }
        };
        QuizServiceProxy.Normalize(q);
        Assert.Contains(QuizServiceProxy.Validate(q), e => e.Path == "questions[0].slider");
    }

    [Fact]
    public void Poll_and_wordcloud_force_no_points()
    {
        var q = new QuizEditDto
        {
            Title = "T",
            Questions =
            {
                new QuestionEditDto { Type = QuestionType.Poll, Text = "P", PointsMode = PointsMode.Double, Options = { new() { Text = "a", IsCorrect = true }, new() { Text = "b" } } },
                new QuestionEditDto { Type = QuestionType.WordCloud, Text = "W", PointsMode = PointsMode.Double, Options = { new() { Text = "x" } } }
            }
        };
        QuizServiceProxy.Normalize(q);
        Assert.Empty(QuizServiceProxy.Validate(q));
        Assert.All(q.Questions, x => Assert.Equal(PointsMode.NoPoints, x.PointsMode));
        Assert.All(q.Questions[0].Options, o => Assert.False(o.IsCorrect));
        Assert.Empty(q.Questions[1].Options);
    }

    [Fact]
    public void Unsafe_image_urls_are_dropped()
    {
        var q = Valid();
        q.Questions[0].ImageUrl = "javascript:alert(1)";
        q.CoverImageUrl = "//evil.example/x.png";
        QuizServiceProxy.Normalize(q);
        Assert.Null(q.Questions[0].ImageUrl);
        Assert.Null(q.CoverImageUrl);
        q.Questions[0].ImageUrl = "/uploads/2026/11/a.png";
        QuizServiceProxy.Normalize(q);
        Assert.Equal("/uploads/2026/11/a.png", q.Questions[0].ImageUrl);
    }
}

// Statik köməkçilərə qısa giriş
internal static class QuizServiceProxy
{
    public static void Normalize(QuizEditDto d) => Business.Services.QuizService.Normalize(d);
    public static List<Business.Common.ValidationError> Validate(QuizEditDto d) => Business.Services.QuizService.Validate(d);
}

public class QuizServiceTests
{
    [Fact]
    public async Task Save_edit_copy_search_favorite_and_delete()
    {
        using var h = new TestHost();
        var quizId = await h.CreateQuizAsync(TestHost.Mc("S1", 0), TestHost.Mc("S2", 1));

        // Redaktə üçün oxu, dəyiş, yenidən yaz
        var edit = (await h.WithScope(sp => sp.GetRequiredService<IQuizService>().GetForEditAsync(quizId, h.UserId)))!;
        Assert.Equal(2, edit.Questions.Count);
        edit.Title = "Yeni ad";
        edit.IsPublic = true;
        edit.Questions.RemoveAt(0);
        var saved = await h.WithScope(sp => sp.GetRequiredService<IQuizService>().SaveAsync(h.UserId, edit));
        Assert.True(saved.Success);
        Assert.Equal(quizId, saved.Value);

        await h.WithScope(async sp =>
        {
            var db = sp.GetRequiredService<KvizDbContext>();
            Assert.Equal(1, await db.Questions.CountAsync(q => q.QuizId == quizId));
            Assert.Equal(4, await db.AnswerOptions.CountAsync());
            return 0;
        });

        // Başqasının kvizini redaktə etmək olmaz
        var stranger = await h.WithScope(sp => sp.GetRequiredService<IQuizService>().SaveAsync(Guid.NewGuid(), edit));
        Assert.False(stranger.Success);
        Assert.Null(await h.WithScope(sp => sp.GetRequiredService<IQuizService>().GetForEditAsync(quizId, Guid.NewGuid())));

        // İctimai kitabxanada axtarış
        var found = await h.WithScope(sp => sp.GetRequiredService<IQuizService>().SearchPublicAsync(new LibraryQueryDto { Search = "Yeni" }, null));
        Assert.Single(found.Items);
        var none = await h.WithScope(sp => sp.GetRequiredService<IQuizService>().SearchPublicAsync(new LibraryQueryDto { Search = "yoxdur" }, null));
        Assert.Empty(none.Items);

        // Başqası kopyalayır (gizli kimi), sevimliyə əlavə edir
        var other = h.NewUser();
        var copy = await h.WithScope(sp => sp.GetRequiredService<IQuizService>().CopyAsync(quizId, other));
        Assert.True(copy.Success);
        var copied = await h.WithScope(sp => sp.GetRequiredService<IQuizService>().GetForEditAsync(copy.Value, other));
        Assert.False(copied!.IsPublic);
        Assert.Single(copied.Questions);
        Assert.True(await h.WithScope(sp => sp.GetRequiredService<IQuizService>().ToggleFavoriteAsync(quizId, other)));
        Assert.False(await h.WithScope(sp => sp.GetRequiredService<IQuizService>().ToggleFavoriteAsync(quizId, other)));

        // Gizli kvizi başqası görə bilməz
        await h.WithScope(sp => sp.GetRequiredService<IQuizService>().SetVisibilityAsync(quizId, h.UserId, false));
        Assert.Null(await h.WithScope(sp => sp.GetRequiredService<IQuizService>().GetDetailsAsync(quizId, other)));
        Assert.False((await h.WithScope(sp => sp.GetRequiredService<IQuizService>().CopyAsync(quizId, other))).Success);

        Assert.True((await h.WithScope(sp => sp.GetRequiredService<IQuizService>().DeleteAsync(quizId, h.UserId))).Success);
    }

    [Fact]
    public async Task Game_snapshot_is_independent_of_later_quiz_edits()
    {
        using var h = new TestHost();
        var quizId = await h.CreateQuizAsync(TestHost.Mc("Orijinal sual", 0));
        var game = await h.CreateGameAsync(quizId);

        var edit = (await h.WithScope(sp => sp.GetRequiredService<IQuizService>().GetForEditAsync(quizId, h.UserId)))!;
        edit.Questions[0].Text = "Dəyişdirilmiş sual";
        await h.WithScope(sp => sp.GetRequiredService<IQuizService>().SaveAsync(h.UserId, edit));

        var report = await h.WithScope(sp => sp.GetRequiredService<IReportService>().GetReportAsync(game.GameId, h.UserId));
        Assert.Equal("Orijinal sual", report!.Questions[0].Text);
    }

    [Fact]
    public async Task Puzzle_snapshot_is_shuffled_and_pins_are_unique()
    {
        using var h = new TestHost();
        var quizId = await h.CreateQuizAsync(new QuestionEditDto
        {
            Type = QuestionType.Puzzle, Text = "Sırala", TimeLimitSeconds = 20,
            Options = { new() { Text = "1" }, new() { Text = "2" }, new() { Text = "3" }, new() { Text = "4" } }
        });
        var pins = new HashSet<string>();
        for (var i = 0; i < 15; i++)
        {
            var g = await h.CreateGameAsync(quizId);
            Assert.True(pins.Add(g.Pin));
            Assert.Equal(6, g.Pin.Length);
        }
        await h.WithScope(async sp =>
        {
            var db = sp.GetRequiredService<KvizDbContext>();
            var q = await db.GameQuestions.FirstAsync();
            var data = Business.Games.GameQuestionData.FromEntity(q);
            Assert.NotEqual(new[] { "1", "2", "3", "4" }, data.Options.Select(o => o.Text).ToArray());
            Assert.Equal(new[] { "1", "2", "3", "4" }, data.Options.OrderBy(o => o.CorrectPosition).Select(o => o.Text).ToArray());
            return 0;
        });
    }

    [Fact]
    public async Task Excel_template_round_trips_into_a_quiz()
    {
        using var h = new TestHost();
        var excel = h.Provider.CreateScope().ServiceProvider.GetRequiredService<IQuizExcelService>();
        var bytes = excel.BuildTemplate();
        using var ms = new MemoryStream(bytes);
        var r = await h.WithScope(sp => sp.GetRequiredService<IQuizExcelService>().ImportAsync(h.UserId, "İdxal", ms));
        Assert.True(r.Success, r.FirstError);
        Assert.Equal(8, r.Value!.ImportedQuestions);

        var quiz = (await h.WithScope(sp => sp.GetRequiredService<IQuizService>().GetForEditAsync(r.Value.QuizId, h.UserId)))!;
        Assert.Equal(QuestionType.MultipleChoice, quiz.Questions[0].Type);
        Assert.Equal("Bakı", quiz.Questions[0].Options.Single(o => o.IsCorrect).Text);
        Assert.True(quiz.Questions[1].MultiSelect);
        Assert.Equal(2, quiz.Questions[1].Options.Count(o => o.IsCorrect));
        Assert.Equal(QuestionType.TrueFalse, quiz.Questions[2].Type);
        Assert.True(quiz.Questions[2].Options[0].IsCorrect);
        Assert.Equal(2, quiz.Questions[3].Options.Count);
        Assert.Equal(PointsMode.NoPoints, quiz.Questions[5].PointsMode);

        // İxrac də işləyir
        var export = await h.WithScope(sp => sp.GetRequiredService<IQuizExcelService>().ExportQuizAsync(r.Value.QuizId, h.UserId));
        Assert.True(export!.Length > 1000);
    }

    [Fact]
    public async Task Excel_import_rejects_garbage()
    {
        using var h = new TestHost();
        var r = await h.WithScope(sp => sp.GetRequiredService<IQuizExcelService>().ImportAsync(h.UserId, "X", new MemoryStream(new byte[] { 1, 2, 3 })));
        Assert.False(r.Success);
    }

    [Fact]
    public async Task Image_upload_checks_real_file_type_and_size()
    {
        using var h = new TestHost();
        var svc = h.Provider.CreateScope().ServiceProvider.GetRequiredService<IImageUploadService>();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        Assert.True((await svc.SaveAsync(new MemoryStream(png), "x.png", png.Length)).Success);
        var fake = System.Text.Encoding.ASCII.GetBytes("<html><script>alert(1)</script></html>");
        Assert.False((await svc.SaveAsync(new MemoryStream(fake), "x.png", fake.Length)).Success);   // uzantı png, məzmun yox
        Assert.False((await svc.SaveAsync(new MemoryStream(png), "x.exe", png.Length)).Success);
        Assert.False((await svc.SaveAsync(new MemoryStream(png), "x.png", 6L * 1024 * 1024)).Success);
    }
}

public class ChallengeTests
{
    [Fact]
    public async Task Challenge_flow_join_answer_finish_rank()
    {
        using var h = new TestHost();
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0), TestHost.Mc("S2", 1));
        var game = await h.CreateGameAsync(quiz, GameMode.Challenge, d => d.DeadlineUtc = DateTime.UtcNow.AddDays(1));
        var pin = await h.WithScope(sp => sp.GetRequiredService<IGameService>().ResolvePinAsync(game.Pin));
        Assert.Equal(PinKind.Challenge, pin.Kind);

        var ch = (Func<IChallengeService, Task>) (_ => Task.CompletedTask);
        async Task<T> C<T>(Func<IChallengeService, Task<T>> f) => await h.WithScope(sp => f(sp.GetRequiredService<IChallengeService>()));

        var j1 = await C(s => s.JoinAsync(game.Pin, "Aysel"));
        var j2 = await C(s => s.JoinAsync(game.Pin, "Rəşad"));
        Assert.True(j1.Success && j2.Success);
        Assert.False((await C(s => s.JoinAsync(game.Pin, "aysel"))).Success);

        // Aysel: iki sualı da düzgün
        var st = (await C(s => s.GetStateAsync(j1.Token)))!;
        Assert.Equal("question", st.Phase);
        Assert.Equal(0, st.Question!.Index);
        Assert.InRange(st.RemainingMs, 1, 20000);
        var f1 = await C(s => s.AnswerAsync(j1.Token, new AnswerSubmission { Choices = new[] { 0 } }));
        Assert.True(f1.Accepted && f1.Correct);
        Assert.InRange(f1.Points, 950, 1000);
        // Eyni suala ikinci cavab verilə bilməz: indi artıq 2-ci sual açılmayıb
        var again = await C(s => s.AnswerAsync(j1.Token, new AnswerSubmission { Choices = new[] { 0 } }));
        Assert.False(again.Accepted);
        await C(s => s.GetStateAsync(j1.Token));
        var f2 = await C(s => s.AnswerAsync(j1.Token, new AnswerSubmission { Choices = new[] { 1 } }));
        Assert.True(f2.Correct);
        Assert.Equal(100, f2.StreakBonus);
        var done = (await C(s => s.GetStateAsync(j1.Token)))!;
        Assert.Equal("finished", done.Phase);
        Assert.Equal(1, done.Rank);
        Assert.Equal(2, done.Correct);

        // Rəşad: yalnız birinci sualı yanlış cavablayır
        await C(s => s.GetStateAsync(j2.Token));
        var w = await C(s => s.AnswerAsync(j2.Token, new AnswerSubmission { Choices = new[] { 3 } }));
        Assert.False(w.Correct);
        Assert.NotNull(w.Reveal);
        Assert.Equal(new[] { 0 }, w.Reveal!.CorrectIndexes);

        var st2 = (await C(s => s.GetStateAsync(j2.Token)))!;
        Assert.Equal(2, st2.Rank);
        Assert.Equal(1, st2.Question!.Index);

        // Host bağlayır, yeni oyunçu qoşula bilmir, hesabat var
        var closed = await h.WithScope(sp => sp.GetRequiredService<IGameService>().CloseChallengeAsync(game.GameId, h.UserId));
        Assert.True(closed.Success);
        Assert.False((await C(s => s.JoinAsync(game.Pin, "Gec"))).Success);
        var report = await h.WithScope(sp => sp.GetRequiredService<IReportService>().GetReportAsync(game.GameId, h.UserId));
        Assert.Equal(2, report!.PlayerCount);
        Assert.Equal(GameStatus.Finished, report.Status);
        Assert.Equal("Aysel", report.Players[0].Nickname);
        _ = ch;
    }

    [Fact]
    public async Task Expired_question_counts_as_unanswered()
    {
        using var h = new TestHost();
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0, seconds: 5), TestHost.Mc("S2", 0, seconds: 5));
        var game = await h.CreateGameAsync(quiz, GameMode.Challenge, d => d.DeadlineUtc = DateTime.UtcNow.AddDays(1));
        var j = await h.WithScope(sp => sp.GetRequiredService<IChallengeService>().JoinAsync(game.Pin, "Gec"));
        await h.WithScope(sp => sp.GetRequiredService<IChallengeService>().GetStateAsync(j.Token));

        // Sualın göstərilmə vaxtını keçmişə çəkirik (6+ saniyə əvvəl)
        await h.WithScope(async sp =>
        {
            var db = sp.GetRequiredService<KvizDbContext>();
            var p = await db.GamePlayers.FirstAsync();
            p.QuestionServedAt = DateTime.UtcNow.AddSeconds(-30);
            await db.SaveChangesAsync();
            return 0;
        });

        var late = await h.WithScope(sp => sp.GetRequiredService<IChallengeService>().AnswerAsync(j.Token, new AnswerSubmission { Choices = new[] { 0 } }));
        Assert.True(late.Accepted);
        Assert.True(late.TimedOut);
        Assert.Equal(0, late.Points);
    }

    [Fact]
    public async Task Pin_resolution_states()
    {
        using var h = new TestHost();
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0));
        var live = await h.CreateGameAsync(quiz);
        var svc = (Func<string, Task<PinResolveDto>>)(p => h.WithScope(sp => sp.GetRequiredService<IGameService>().ResolvePinAsync(p)));
        Assert.Equal(PinKind.Live, (await svc(live.Pin)).Kind);
        Assert.Equal(PinKind.NotFound, (await svc("123")).Kind);
        Assert.Equal(PinKind.NotFound, (await svc("999999")).Kind);
        Assert.Equal(PinKind.Live, (await svc(live.Pin[..3] + " " + live.Pin[3..])).Kind); // boşluqlu PIN də qəbul olunur
    }
}
