using Kviz.Business.Models;
using Kviz.Entities.Enums;

namespace Kviz.Business.Games;

public record StoredAnswer(string Data, bool IsCorrect, int ResponseMs);

public static class ResultBuilder
{
    public static QuestionPublicDto ToPublic(GameQuestionData q, int index, int total) => new()
    {
        Index = index,
        Total = total,
        Type = q.Type,
        Text = q.Text,
        ImageUrl = q.ImageUrl,
        RelatedImageUrl = q.RelatedImageUrl, RelatedImageCredit = q.RelatedImageCredit,
        TimeLimitSeconds = q.TimeLimitSeconds,
        PointsMode = q.PointsMode,
        MultiSelect = q.MultiSelect,
        Options = q.Type is QuestionType.MultipleChoice or QuestionType.TrueFalse or QuestionType.Puzzle or QuestionType.Poll
            ? q.Options.Select(o => o.Text).ToList() : new List<string>(),
        SliderMin = q.SliderMin,
        SliderMax = q.SliderMax,
        SliderStep = q.SliderStep
    };

    /// <summary>Sualın nəticə statistikasını verilmiş cavablardan qurur (canlı oyun, tapşırıq və hesabat üçün ortaq).</summary>
    public static QuestionResultDto Build(GameQuestionData q, int index, IReadOnlyCollection<StoredAnswer> answers, int totalPlayers)
    {
        var r = new QuestionResultDto
        {
            Index = index,
            Type = q.Type,
            Answered = answers.Count,
            Correct = answers.Count(a => a.IsCorrect),
            TotalPlayers = totalPlayers,
            AverageResponseMs = answers.Count == 0 ? 0 : answers.Average(a => a.ResponseMs)
        };

        switch (q.Type)
        {
            case QuestionType.MultipleChoice:
            case QuestionType.TrueFalse:
            case QuestionType.Poll:
            {
                r.OptionTexts = q.Options.Select(o => o.Text).ToList();
                var counts = new int[q.Options.Count];
                foreach (var a in answers)
                    foreach (var i in AnswerEvaluator.ParseIndexes(a.Data))
                        if (i >= 0 && i < counts.Length) counts[i]++;
                r.OptionCounts = counts.ToList();
                if (q.Type != QuestionType.Poll)
                    r.CorrectIndexes = q.Options.Select((o, i) => (o, i)).Where(x => x.o.IsCorrect).Select(x => x.i).ToList();
                break;
            }
            case QuestionType.Puzzle:
                r.OptionTexts = q.Options.Select(o => o.Text).ToList();
                r.CorrectOrder = q.Options.OrderBy(o => o.CorrectPosition).Select(o => o.Text).ToList();
                break;
            case QuestionType.TypeAnswer:
                r.AcceptedAnswers = q.Options.Where(o => o.IsCorrect).Select(o => o.Text).ToList();
                r.Words = Group(answers.Select(a => a.Data), 12);
                break;
            case QuestionType.Slider:
                r.SliderMin = q.SliderMin;
                r.SliderMax = q.SliderMax;
                r.SliderCorrect = q.SliderCorrect;
                r.SliderTolerance = q.SliderTolerance;
                r.SliderValues = answers
                    .Select(a => double.TryParse(a.Data, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
                    .Where(v => !double.IsNaN(v)).Take(1000).ToList();
                break;
            case QuestionType.WordCloud:
                r.Words = Group(answers.Select(a => a.Data), 60);
                break;
            case QuestionType.OpenEnded:
                r.Texts = answers.Select(a => a.Data).Where(t => t.Length > 0).Take(100).ToList();
                break;
        }
        return r;
    }

    private static List<WordCountDto> Group(IEnumerable<string> values, int take) =>
        values.Select(TextNormalizer.Normalize).Where(v => v.Length > 0)
            .GroupBy(v => v).Select(g => new WordCountDto { Word = g.Key, Count = g.Count() })
            .OrderByDescending(w => w.Count).ThenBy(w => w.Word).Take(take).ToList();

    /// <summary>Cavabı hesabatda göstərmək üçün oxunaqlı mətnə çevirir.</summary>
    public static string Describe(GameQuestionData q, string data)
    {
        if (string.IsNullOrEmpty(data)) return "—";
        switch (q.Type)
        {
            case QuestionType.MultipleChoice:
            case QuestionType.TrueFalse:
            case QuestionType.Poll:
                return string.Join(", ", AnswerEvaluator.ParseIndexes(data)
                    .Where(i => i >= 0 && i < q.Options.Count).Select(i => q.Options[i].Text));
            case QuestionType.Puzzle:
                return string.Join(" → ", AnswerEvaluator.ParseIndexes(data)
                    .Where(i => i >= 0 && i < q.Options.Count).Select(i => q.Options[i].Text));
            default:
                return data;
        }
    }
}
