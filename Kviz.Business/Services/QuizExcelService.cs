using ClosedXML.Excel;
using Kviz.Business.Abstractions;
using Kviz.Business.Common;
using Kviz.Business.Models;
using Kviz.Entities.Enums;

namespace Kviz.Business.Services;

/// <summary>Kvizi Excel-dən idxal / Excel-ə ixrac. Sütunlar: Sual, Növ, Vaxt, Xal, A-F variantlar, Düzgün cavab.</summary>
public class QuizExcelService : IQuizExcelService
{
    private static readonly string[] Headers =
        { "Sual", "Növ", "Vaxt (san)", "Xal", "Variant A", "Variant B", "Variant C", "Variant D", "Variant E", "Variant F", "Düzgün cavab" };

    private readonly IQuizService _quizzes;

    public QuizExcelService(IQuizService quizzes) => _quizzes = quizzes;

    public byte[] BuildTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Suallar");
        for (var c = 0; c < Headers.Length; c++) ws.Cell(1, c + 1).Value = Headers[c];
        Style(ws.Range(1, 1, 1, Headers.Length));

        void Row(int r, params object[] v) { for (var i = 0; i < v.Length; i++) ws.Cell(r, i + 1).Value = XLCellValue.FromObject(v[i]); }
        Row(2, "Azərbaycanın paytaxtı hansı şəhərdir?", "Test", 20, "Standart", "Gəncə", "Bakı", "Sumqayıt", "Şəki", "", "", "B");
        Row(3, "Aşağıdakılardan hansılar tək ədəddir?", "Test", 30, "Standart", "2", "3", "5", "8", "", "", "B,C");
        Row(4, "Yer Günəş ətrafında fırlanır.", "Doğru/Yanlış", 10, "Standart", "", "", "", "", "", "", "Doğru");
        Row(5, "2 + 2 neçədir?", "Cavabı yaz", 20, "Standart", "4", "dörd", "", "", "", "", "");
        Row(6, "Fəsilləri düzgün ardıcıllıqla düzün", "Puzzle", 30, "Standart", "Yaz", "Payız", "Qış", "Yaz sonu", "", "", "");
        Row(7, "Ən sevdiyiniz meyvə?", "Sorğu", 20, "Xalsız", "Alma", "Armud", "Banan", "Üzüm", "", "", "");
        Row(8, "Bu mövzu haqqında bir sözlə nə deyərdiniz?", "Söz buludu", 30, "Xalsız", "", "", "", "", "", "", "");
        Row(9, "Dərs barədə fikriniz?", "Açıq sual", 60, "Xalsız", "", "", "", "", "", "", "");
        ws.Columns().AdjustToContents();
        ws.Column(1).Width = 50;

        var help = wb.AddWorksheet("Izah");
        var lines = new[]
        {
            "Növ: Test, Doğru/Yanlış, Cavabı yaz, Puzzle, Sorğu, Söz buludu, Açıq sual",
            "Xal: Standart, İkiqat, Xalsız (Sorğu, Söz buludu və Açıq sual həmişə xalsızdır)",
            "Test: Variant sütunlarını doldurun, düzgün cavab sütununa variantın hərfini yazın (məs. B və ya B,C).",
            "Doğru/Yanlış: Düzgün cavab sütununa Doğru və ya Yanlış yazın.",
            "Cavabı yaz: Qəbul olunan cavabları Variant A-D sütunlarına yazın (ən çox 4).",
            "Puzzle: Variantları DÜZGÜN sıra ilə yazın, oyunda qarışdırılacaq.",
            "Sorğu: Variantları yazın, düzgün cavab yoxdur.",
            "Vaxt boşdursa 20 saniyə götürülür. Sürüşdürücü sualı redaktorda əlavə edilir."
        };
        for (var i = 0; i < lines.Length; i++) help.Cell(i + 1, 1).Value = lines[i];
        help.Column(1).Width = 110;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<ServiceResult<ImportResultDto>> ImportAsync(Guid userId, string title, Stream file)
    {
        XLWorkbook wb;
        try { wb = new XLWorkbook(file); }
        catch { return ServiceResult<ImportResultDto>.Fail("Fayl oxunmadı. Zəhmət olmasa .xlsx formatında Excel faylı seçin."); }

        using (wb)
        {
            var ws = wb.Worksheets.FirstOrDefault();
            if (ws == null) return ServiceResult<ImportResultDto>.Fail("Excel faylında vərəq tapılmadı.");

            var dto = new QuizEditDto { Title = string.IsNullOrWhiteSpace(title) ? "Excel-dən kviz" : title.Trim(), Language = "az" };
            var warnings = new List<string>();
            var last = ws.LastRowUsed()?.RowNumber() ?? 1;

            for (var r = 2; r <= last; r++)
            {
                string Get(int c) => ws.Cell(r, c).GetString().Trim();
                var text = Get(1);
                if (text.Length == 0) continue;

                var type = ParseType(Get(2));
                if (type == null) { warnings.Add($"{r}-ci sətir keçildi: naməlum növ \"{Get(2)}\"."); continue; }

                var q = new QuestionEditDto { Type = type.Value, Text = text };
                q.TimeLimitSeconds = int.TryParse(Get(3), out var sec) && sec is >= 5 and <= 600 ? sec : 20;
                q.PointsMode = ParsePoints(Get(4));

                var opts = Enumerable.Range(5, 6).Select(Get).ToList();
                var correct = Get(11);

                switch (type)
                {
                    case QuestionType.MultipleChoice:
                    {
                        var letters = correct.ToUpperInvariant().Split(new[] { ',', ';', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s[0] - 'A').ToHashSet();
                        for (var i = 0; i < opts.Count; i++)
                            if (opts[i].Length > 0) q.Options.Add(new OptionEditDto { Text = opts[i], IsCorrect = letters.Contains(i) });
                        q.MultiSelect = q.Options.Count(o => o.IsCorrect) > 1;
                        break;
                    }
                    case QuestionType.TrueFalse:
                    {
                        var isTrue = correct.Equals("doğru", StringComparison.OrdinalIgnoreCase) || correct.Equals("true", StringComparison.OrdinalIgnoreCase)
                                     || correct.Equals("d", StringComparison.OrdinalIgnoreCase) || correct == "1";
                        q.Options.Add(new OptionEditDto { Text = "Doğru", IsCorrect = isTrue });
                        q.Options.Add(new OptionEditDto { Text = "Yanlış", IsCorrect = !isTrue });
                        break;
                    }
                    case QuestionType.TypeAnswer:
                        foreach (var o in opts.Take(4).Where(o => o.Length > 0)) q.Options.Add(new OptionEditDto { Text = o, IsCorrect = true });
                        break;
                    case QuestionType.Puzzle:
                    case QuestionType.Poll:
                        foreach (var o in opts.Where(o => o.Length > 0)) q.Options.Add(new OptionEditDto { Text = o });
                        break;
                }
                dto.Questions.Add(q);
            }

            if (dto.Questions.Count == 0) return ServiceResult<ImportResultDto>.Fail("Faylda idxal ediləcək sual tapılmadı. Şablona baxın.");

            var saved = await _quizzes.SaveAsync(userId, dto);
            if (!saved.Success)
            {
                var res = new ServiceResult<ImportResultDto> { Errors = saved.Errors };
                return res;
            }
            return ServiceResult<ImportResultDto>.Ok(new ImportResultDto
            {
                QuizId = saved.Value, ImportedQuestions = dto.Questions.Count, Warnings = warnings
            });
        }
    }

    public async Task<byte[]?> ExportQuizAsync(Guid quizId, Guid userId)
    {
        var q = await _quizzes.GetForEditAsync(quizId, userId);
        if (q == null) return null;

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Suallar");
        for (var c = 0; c < Headers.Length; c++) ws.Cell(1, c + 1).Value = Headers[c];
        Style(ws.Range(1, 1, 1, Headers.Length));
        var r = 2;
        foreach (var x in q.Questions)
        {
            ws.Cell(r, 1).Value = x.Text;
            ws.Cell(r, 2).Value = x.Type switch
            {
                QuestionType.MultipleChoice => "Test", QuestionType.TrueFalse => "Doğru/Yanlış", QuestionType.TypeAnswer => "Cavabı yaz",
                QuestionType.Puzzle => "Puzzle", QuestionType.Poll => "Sorğu", QuestionType.WordCloud => "Söz buludu",
                QuestionType.OpenEnded => "Açıq sual", _ => "Sürüşdürücü"
            };
            ws.Cell(r, 3).Value = x.TimeLimitSeconds;
            ws.Cell(r, 4).Value = x.PointsMode switch { PointsMode.Double => "İkiqat", PointsMode.NoPoints => "Xalsız", _ => "Standart" };
            for (var i = 0; i < Math.Min(6, x.Options.Count); i++) ws.Cell(r, 5 + i).Value = x.Options[i].Text;
            if (x.Type == QuestionType.MultipleChoice)
                ws.Cell(r, 11).Value = string.Join(",", x.Options.Select((o, i) => (o, i)).Where(t => t.o.IsCorrect).Select(t => (char)('A' + t.i)));
            else if (x.Type == QuestionType.TrueFalse)
                ws.Cell(r, 11).Value = x.Options.Count > 0 && x.Options[0].IsCorrect ? "Doğru" : "Yanlış";
            else if (x.Type == QuestionType.Slider)
                ws.Cell(r, 5).Value = $"Sürüşdürücü: {x.SliderMin}-{x.SliderMax}, düzgün {x.SliderCorrect} (idxal olunmur)";
            r++;
        }
        ws.Columns().AdjustToContents();
        ws.Column(1).Width = 50;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static QuestionType? ParseType(string s)
    {
        var n = Games.TextNormalizer.Normalize(s).Replace(" ", "").Replace("/", "").Replace("-", "");
        return n switch
        {
            "test" or "mc" or "multiplechoice" or "çoxseçimli" => QuestionType.MultipleChoice,
            "doğruyanlış" or "doğruyanliş" or "dogruyanlis" or "truefalse" or "dy" => QuestionType.TrueFalse,
            "cavabıyaz" or "cavabiyaz" or "yaz" or "typeanswer" => QuestionType.TypeAnswer,
            "puzzle" or "sıralama" or "siralama" => QuestionType.Puzzle,
            "sorğu" or "sorgu" or "poll" => QuestionType.Poll,
            "sözbuludu" or "sozbuludu" or "wordcloud" => QuestionType.WordCloud,
            "açıqsual" or "acıqsual" or "açiqsual" or "openended" => QuestionType.OpenEnded,
            _ => null
        };
    }

    private static PointsMode ParsePoints(string s)
    {
        var n = Games.TextNormalizer.Normalize(s);
        return n switch { "ikiqat" or "double" or "2x" => PointsMode.Double, "xalsız" or "xalsiz" or "nopoints" or "0" => PointsMode.NoPoints, _ => PointsMode.Standard };
    }

    private static void Style(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#46178f");
        range.Style.Font.FontColor = XLColor.White;
    }
}
