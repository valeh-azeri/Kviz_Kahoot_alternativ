using ClosedXML.Excel;
using Kviz.Business.Abstractions;
using Kviz.Business.Games;
using Kviz.Business.Models;
using Kviz.DataAccess.Repositories;
using Kviz.Entities;
using Kviz.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Kviz.Business.Services;

public class ReportService : IReportService
{
    private readonly IGameRepository _games;
    private readonly IRepository<PlayerAnswer> _answers;

    public ReportService(IGameRepository games, IRepository<PlayerAnswer> answers)
    {
        _games = games;
        _answers = answers;
    }

    public async Task<GameReportDto?> GetReportAsync(Guid gameId, Guid userId)
    {
        var game = await _games.QueryNoTracking()
            .Include(g => g.Questions).Include(g => g.Teams).Include(g => g.Players)
            .AsSplitQuery().FirstOrDefaultAsync(g => g.Id == gameId && g.HostId == userId);
        if (game == null) return null;

        var answers = await _answers.QueryNoTracking().Where(a => a.Player!.GameSessionId == gameId).ToListAsync();
        var questions = game.Questions.OrderBy(q => q.Order).Select(GameQuestionData.FromEntity).ToList();
        var players = game.Players.Where(p => !p.IsKicked)
            .OrderByDescending(p => p.TotalScore).ThenBy(p => p.TotalResponseMs).ThenBy(p => p.JoinedAt).ToList();
        var scoredCount = questions.Count(q => q.IsScored);

        var report = new GameReportDto
        {
            GameId = game.Id, QuizTitle = game.QuizTitle, QuizId = game.QuizId, Pin = game.Pin, Mode = game.Mode, Status = game.Status,
            CreatedAt = game.CreatedAt, StartedAt = game.StartedAt, FinishedAt = game.FinishedAt, Deadline = game.Deadline,
            PlayerCount = players.Count, QuestionCount = questions.Count
        };

        var byQuestion = answers.ToLookup(a => a.GameQuestionId);
        foreach (var q in questions)
        {
            var given = byQuestion[q.Id].Where(a => a.Answered && players.Any(p => p.Id == a.GamePlayerId)).ToList();
            var stored = given.Select(a => new StoredAnswer(a.AnswerData, a.IsCorrect, a.ResponseTimeMs)).ToList();
            var result = ResultBuilder.Build(q, q.Order, stored, players.Count);
            report.Questions.Add(new ReportQuestionDto
            {
                Order = q.Order + 1, Type = q.Type, Text = string.IsNullOrWhiteSpace(q.Text) ? "(şəkilli sual)" : q.Text, ImageUrl = q.ImageUrl, TimeLimitSeconds = q.TimeLimitSeconds,
                Scored = q.IsScored, Answered = result.Answered, Correct = result.Correct,
                CorrectPercent = q.IsScored && players.Count > 0 ? result.Correct * 100.0 / players.Count : 0,
                AverageResponseMs = result.AverageResponseMs, Result = result
            });
        }

        var rank = 1;
        foreach (var p in players)
        {
            var team = p.TeamId.HasValue ? game.Teams.FirstOrDefault(t => t.Id == p.TeamId) : null;
            var mine = answers.Where(a => a.GamePlayerId == p.Id).ToDictionary(a => a.GameQuestionId);
            var rp = new ReportPlayerDto
            {
                Id = p.Id, Rank = rank++, Nickname = p.Nickname, TeamName = team?.Name, Score = p.TotalScore,
                CorrectCount = p.CorrectCount, AnsweredCount = p.AnsweredCount, ScoredQuestions = scoredCount,
                Accuracy = scoredCount == 0 ? 0 : p.CorrectCount * 100.0 / scoredCount,
                AverageResponseMs = p.AnsweredCount == 0 ? 0 : p.TotalResponseMs / (double)p.AnsweredCount, MaxStreak = p.MaxStreak
            };
            foreach (var q in questions)
            {
                mine.TryGetValue(q.Id, out var a);
                rp.Answers.Add(new ReportPlayerAnswerDto
                {
                    QuestionOrder = q.Order + 1, Answered = a?.Answered ?? false, IsCorrect = a?.IsCorrect ?? false,
                    Points = a?.Points ?? 0, ResponseTimeMs = a?.ResponseTimeMs ?? 0,
                    Display = a != null && a.Answered ? ResultBuilder.Describe(q, a.AnswerData) : "—"
                });
            }
            report.Players.Add(rp);
        }

        report.AverageAccuracy = players.Count == 0 || scoredCount == 0 ? 0 : report.Players.Average(p => p.Accuracy);
        report.AverageScore = players.Count == 0 ? 0 : players.Average(p => p.TotalScore);

        if (game.Mode == GameMode.LiveTeam)
        {
            var teams = game.Teams.OrderBy(t => t.Order).Select(t =>
            {
                var members = players.Where(p => p.TeamId == t.Id).ToList();
                return new TeamScoreDto
                {
                    TeamId = t.Id, Name = t.Name, ColorIndex = t.ColorIndex, Members = members.Count,
                    Score = members.Count == 0 ? 0 : (int)Math.Round(members.Average(m => m.TotalScore))
                };
            }).OrderByDescending(t => t.Score).ToList();
            for (var i = 0; i < teams.Count; i++) teams[i].Rank = i + 1;
            report.Teams = teams;
        }
        return report;
    }

    public static string ModeName(GameMode m) => m switch
    {
        GameMode.LiveClassic => "Canlı oyun",
        GameMode.LiveTeam => "Komanda oyunu",
        _ => "Tapşırıq"
    };

    public static string TypeName(QuestionType t) => t switch
    {
        QuestionType.MultipleChoice => "Test",
        QuestionType.TrueFalse => "Doğru / Yanlış",
        QuestionType.TypeAnswer => "Cavabı yaz",
        QuestionType.Slider => "Sürüşdürücü",
        QuestionType.Puzzle => "Puzzle",
        QuestionType.Poll => "Sorğu",
        QuestionType.WordCloud => "Söz buludu",
        _ => "Açıq sual"
    };

    // ---------- Excel ----------

    public async Task<byte[]?> ExportExcelAsync(Guid gameId, Guid userId)
    {
        var r = await GetReportAsync(gameId, userId);
        if (r == null) return null;

        using var wb = new XLWorkbook();
        var s = wb.AddWorksheet("Ümumi");
        s.Cell(1, 1).Value = r.QuizTitle; s.Cell(1, 1).Style.Font.Bold = true; s.Cell(1, 1).Style.Font.FontSize = 16;
        var info = new (string, string)[]
        {
            ("Rejim", ModeName(r.Mode)), ("PIN", r.Pin), ("Yaradılıb", r.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")),
            ("Oyunçu sayı", r.PlayerCount.ToString()), ("Sual sayı", r.QuestionCount.ToString()),
            ("Orta düzgünlük", $"{r.AverageAccuracy:0.#}%"), ("Orta xal", $"{r.AverageScore:0}")
        };
        for (var i = 0; i < info.Length; i++) { s.Cell(3 + i, 1).Value = info[i].Item1; s.Cell(3 + i, 1).Style.Font.Bold = true; s.Cell(3 + i, 2).Value = info[i].Item2; }
        s.Columns().AdjustToContents();

        var ps = wb.AddWorksheet("Oyunçular");
        var head = new[] { "Yer", "Ləqəb", "Komanda", "Xal", "Düzgün", "Cavab verilən", "Düzgünlük %", "Orta cavab müddəti (san)", "Ən uzun seriya" };
        for (var c = 0; c < head.Length; c++) ps.Cell(1, c + 1).Value = head[c];
        var row = 2;
        foreach (var p in r.Players)
        {
            ps.Cell(row, 1).Value = p.Rank; ps.Cell(row, 2).Value = p.Nickname; ps.Cell(row, 3).Value = p.TeamName ?? "";
            ps.Cell(row, 4).Value = p.Score; ps.Cell(row, 5).Value = p.CorrectCount; ps.Cell(row, 6).Value = p.AnsweredCount;
            ps.Cell(row, 7).Value = Math.Round(p.Accuracy, 1); ps.Cell(row, 8).Value = Math.Round(p.AverageResponseMs / 1000, 2);
            ps.Cell(row, 9).Value = p.MaxStreak; row++;
        }
        StyleHeader(ps.Range(1, 1, 1, head.Length)); ps.Columns().AdjustToContents();

        var qs = wb.AddWorksheet("Suallar");
        var qh = new[] { "№", "Sual", "Növ", "Cavab verən", "Düzgün", "Düzgün %", "Orta cavab müddəti (san)" };
        for (var c = 0; c < qh.Length; c++) qs.Cell(1, c + 1).Value = qh[c];
        row = 2;
        foreach (var q in r.Questions)
        {
            qs.Cell(row, 1).Value = q.Order; qs.Cell(row, 2).Value = q.Text; qs.Cell(row, 3).Value = TypeName(q.Type);
            qs.Cell(row, 4).Value = q.Answered; qs.Cell(row, 5).Value = q.Scored ? q.Correct : 0;
            qs.Cell(row, 6).Value = q.Scored ? Math.Round(q.CorrectPercent, 1) : 0; qs.Cell(row, 7).Value = Math.Round(q.AverageResponseMs / 1000, 2);
            row++;
        }
        StyleHeader(qs.Range(1, 1, 1, qh.Length)); qs.Columns().AdjustToContents();
        qs.Column(2).Width = 60; qs.Column(2).Style.Alignment.WrapText = true;

        var ans = wb.AddWorksheet("Cavablar");
        ans.Cell(1, 1).Value = "Ləqəb";
        for (var i = 0; i < r.Questions.Count; i++) ans.Cell(1, i + 2).Value = $"S{r.Questions[i].Order}";
        row = 2;
        foreach (var p in r.Players)
        {
            ans.Cell(row, 1).Value = p.Nickname;
            for (var i = 0; i < p.Answers.Count; i++)
            {
                var a = p.Answers[i];
                var cell = ans.Cell(row, i + 2);
                cell.Value = a.Answered ? $"{a.Display} ({a.Points})" : "—";
                if (r.Questions[i].Scored && a.Answered) cell.Style.Fill.BackgroundColor = a.IsCorrect ? XLColor.FromHtml("#d9f7e3") : XLColor.FromHtml("#fde0e0");
            }
            row++;
        }
        StyleHeader(ans.Range(1, 1, 1, r.Questions.Count + 1)); ans.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#46178f");
        range.Style.Font.FontColor = XLColor.White;
    }

    // ---------- PDF ----------

    private static string[]? _workingFonts;

    public async Task<byte[]?> ExportPdfAsync(Guid gameId, Guid userId)
    {
        var r = await GetReportAsync(gameId, userId);
        if (r == null) return null;
        QuestPDF.Settings.License = LicenseType.Community;
        // QuestPDF 2026.9-dan etibarən sistem şriftləri avtomatik istifadə olunmur və tapılmayan şrift xəta verir.
        QuestPDF.Settings.UseSystemFonts = true;

        // Əvvəl kompüterdəki Azərbaycan hərflərini dəstəkləyən şriftlər, alınmasa QuestPDF-in öz Lato şrifti.
        if (_workingFonts != null) return RenderPdf(r, _workingFonts);
        foreach (var candidate in new[] { new[] { "Segoe UI", "Lato" }, new[] { "Arial", "Lato" }, new[] { "Liberation Sans", "Lato" }, new[] { "DejaVu Sans", "Lato" }, new[] { "Lato" } })
        {
            try
            {
                var bytes = RenderPdf(r, candidate);
                _workingFonts = candidate;
                return bytes;
            }
            catch (QuestPDF.Drawing.Exceptions.DocumentDrawingException) { /* bu şrift mövcud deyil, növbətini yoxla */ }
        }
        return null;
    }

    private static byte[] RenderPdf(GameReportDto r, string[] fonts)
    {
        var purple = Colors.Purple.Darken3;
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily(fonts));

                page.Header().Column(h =>
                {
                    h.Item().Text(r.QuizTitle).FontSize(20).Bold().FontColor(purple);
                    h.Item().Text($"{ModeName(r.Mode)} · PIN {r.Pin} · {r.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}").FontColor(Colors.Grey.Darken1);
                    h.Item().PaddingVertical(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Row(row =>
                    {
                        void Stat(string label, string value) => row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
                        {
                            c.Item().Text(value).FontSize(18).Bold().FontColor(purple);
                            c.Item().Text(label).FontColor(Colors.Grey.Darken1);
                        });
                        Stat("Oyunçu", r.PlayerCount.ToString());
                        Stat("Sual", r.QuestionCount.ToString());
                        Stat("Orta düzgünlük", $"{r.AverageAccuracy:0}%");
                        Stat("Orta xal", $"{r.AverageScore:0}");
                    });

                    col.Item().PaddingTop(6).Text("Nəticələr").FontSize(14).Bold().FontColor(purple);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(34); c.RelativeColumn(3); c.RelativeColumn(2); c.ConstantColumn(54); c.ConstantColumn(60); c.ConstantColumn(60);
                        });
                        t.Header(h =>
                        {
                            foreach (var title in new[] { "Yer", "Ləqəb", "Komanda", "Xal", "Düzgün", "Düzgünlük" })
                                h.Cell().Background(purple).Padding(4).Text(title).FontColor(Colors.White).Bold();
                        });
                        foreach (var p in r.Players.Take(200))
                        {
                            t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(p.Rank.ToString());
                            t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(p.Nickname);
                            t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(p.TeamName ?? "—");
                            t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(p.Score.ToString());
                            t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text($"{p.CorrectCount}/{p.ScoredQuestions}");
                            t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text($"{p.Accuracy:0}%");
                        }
                    });

                    col.Item().PaddingTop(10).Text("Suallar üzrə statistika").FontSize(14).Bold().FontColor(purple);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.ConstantColumn(26); c.RelativeColumn(5); c.RelativeColumn(2); c.ConstantColumn(64); c.ConstantColumn(64); });
                        t.Header(h =>
                        {
                            foreach (var title in new[] { "№", "Sual", "Növ", "Düzgün %", "Orta vaxt" })
                                h.Cell().Background(purple).Padding(4).Text(title).FontColor(Colors.White).Bold();
                        });
                        foreach (var q in r.Questions)
                        {
                            t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(q.Order.ToString());
                            t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(q.Text);
                            t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(TypeName(q.Type));
                            t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(q.Scored ? $"{q.CorrectPercent:0}%" : "—");
                            t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text($"{q.AverageResponseMs / 1000:0.0} san");
                        }
                    });
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Səhifə ").FontColor(Colors.Grey.Darken1);
                    x.CurrentPageNumber();
                    x.Span(" / ").FontColor(Colors.Grey.Darken1);
                    x.TotalPages();
                });
            });
        });
        return doc.GeneratePdf();
    }
}
