using System.Globalization;
using System.Text.Json;
using Kviz.Entities;
using Kviz.Entities.Enums;

namespace Kviz.Business.Games;

/// <summary>Snapshot-dakı bir variant.</summary>
public class GameOptionData
{
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    /// <summary>Puzzle üçün düzgün mövqe (0-dan başlayır).</summary>
    public int CorrectPosition { get; set; }
}

/// <summary>GameQuestion entity-sinin işlənmiş (variantları açılmış) forması.</summary>
public class GameQuestionData
{
    public Guid Id { get; set; }
    public int Order { get; set; }
    public QuestionType Type { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? RelatedImageUrl { get; set; }
    public string? RelatedImageCredit { get; set; }
    public int TimeLimitSeconds { get; set; }
    public PointsMode PointsMode { get; set; }
    public bool MultiSelect { get; set; }
    public double? SliderMin { get; set; }
    public double? SliderMax { get; set; }
    public double? SliderStep { get; set; }
    public double? SliderCorrect { get; set; }
    public int SliderTolerancePercent { get; set; }
    public List<GameOptionData> Options { get; set; } = new();

    /// <summary>Düzgün/yanlış cavabı olan sual növü.</summary>
    public bool IsScored => Type is QuestionType.MultipleChoice or QuestionType.TrueFalse
        or QuestionType.TypeAnswer or QuestionType.Slider or QuestionType.Puzzle;

    public bool AwardsPoints => IsScored && PointsMode != PointsMode.NoPoints;

    public double SliderTolerance =>
        (SliderMax ?? 0) - (SliderMin ?? 0) is var range && range > 0 ? range * SliderTolerancePercent / 100.0 : 0;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string SerializeOptions(IEnumerable<GameOptionData> options) => JsonSerializer.Serialize(options, Json);

    public static GameQuestionData FromEntity(GameQuestion q) => new()
    {
        Id = q.Id,
        Order = q.Order,
        Type = q.Type,
        Text = q.Text,
        ImageUrl = q.ImageUrl,
        RelatedImageUrl = q.RelatedImageUrl, RelatedImageCredit = q.RelatedImageCredit,
        TimeLimitSeconds = q.TimeLimitSeconds,
        PointsMode = q.PointsMode,
        MultiSelect = q.MultiSelect,
        SliderMin = q.SliderMin,
        SliderMax = q.SliderMax,
        SliderStep = q.SliderStep,
        SliderCorrect = q.SliderCorrect,
        SliderTolerancePercent = q.SliderTolerancePercent,
        Options = JsonSerializer.Deserialize<List<GameOptionData>>(q.OptionsJson, Json) ?? new()
    };
}

/// <summary>Müştəridən gələn xam cavab.</summary>
public class AnswerSubmission
{
    public int[]? Choices { get; set; }
    public string? Text { get; set; }
    public double? Number { get; set; }
}

public static class TextNormalizer
{
    /// <summary>Müqayisə üçün: boşluqları yığır, I/İ/ı hərflərini eyniləşdirir, kiçik hərfə çevirir.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var joined = string.Join(' ', parts)
            .Replace('İ', 'i').Replace('I', 'i').Replace('ı', 'i');
        return joined.ToLowerInvariant();
    }
}

public static class ScoreCalculator
{
    public const int StandardPoints = 1000;

    /// <summary>Sürətə görə xal: dərhal cavab = tam xal, vaxtın sonunda cavab = yarım xal.</summary>
    public static int QuestionPoints(PointsMode mode, int timeLimitMs, int responseMs)
    {
        if (mode == PointsMode.NoPoints || timeLimitMs <= 0) return 0;
        var max = mode == PointsMode.Double ? StandardPoints * 2 : StandardPoints;
        var ratio = Math.Clamp(responseMs / (double)timeLimitMs, 0, 1);
        return (int)Math.Round(max * (1 - ratio / 2));
    }

    /// <summary>Ardıcıl düzgün cavab bonusu: 2-ci cavabdan başlayır, hər pillə +100, maksimum +500.</summary>
    public static int StreakBonus(int streakAfter) => streakAfter >= 2 ? Math.Min(streakAfter - 1, 5) * 100 : 0;
}

public static class AnswerEvaluator
{
    public const int MaxTextAnswer = 100;
    public const int MaxWord = 25;
    public const int MaxOpenText = 300;

    /// <summary>Gələn cavabı yoxlayır və saxlanılacaq mətn formasına salır.</summary>
    public static bool TryNormalize(GameQuestionData q, AnswerSubmission? sub, out string data, out string error)
    {
        data = string.Empty;
        error = string.Empty;
        if (sub == null) { error = "Cavab boşdur."; return false; }

        switch (q.Type)
        {
            case QuestionType.MultipleChoice:
            case QuestionType.TrueFalse:
            case QuestionType.Poll:
            {
                var c = sub.Choices?.Distinct().OrderBy(x => x).ToArray();
                if (c == null || c.Length == 0) { error = "Variant seçilməyib."; return false; }
                if (c.Any(i => i < 0 || i >= q.Options.Count)) { error = "Yanlış variant."; return false; }
                var allowMany = q.Type == QuestionType.MultipleChoice && q.MultiSelect;
                if (!allowMany && c.Length != 1) { error = "Yalnız bir variant seçilə bilər."; return false; }
                data = string.Join('|', c);
                return true;
            }
            case QuestionType.Puzzle:
            {
                var c = sub.Choices;
                if (c == null || c.Length != q.Options.Count || c.Distinct().Count() != c.Length
                    || c.Any(i => i < 0 || i >= q.Options.Count))
                { error = "Sıralama düzgün deyil."; return false; }
                data = string.Join('|', c);
                return true;
            }
            case QuestionType.TypeAnswer:
            case QuestionType.WordCloud:
            case QuestionType.OpenEnded:
            {
                var t = string.Join(' ', (sub.Text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                var max = q.Type == QuestionType.TypeAnswer ? MaxTextAnswer : q.Type == QuestionType.WordCloud ? MaxWord : MaxOpenText;
                if (t.Length == 0) { error = "Cavab boşdur."; return false; }
                if (t.Length > max) t = t[..max];
                data = t;
                return true;
            }
            case QuestionType.Slider:
            {
                if (sub.Number is not double n || double.IsNaN(n) || double.IsInfinity(n)) { error = "Rəqəm daxil edilməyib."; return false; }
                var min = q.SliderMin ?? 0;
                var max = q.SliderMax ?? 100;
                if (n < min || n > max) { error = "Rəqəm aralıqdan kənardır."; return false; }
                data = n.ToString("R", CultureInfo.InvariantCulture);
                return true;
            }
        }
        error = "Naməlum sual növü.";
        return false;
    }

    /// <summary>Saxlanılmış cavabın düzgünlüyü. Qiymətləndirilməyən növlər üçün həmişə false.</summary>
    public static bool IsCorrect(GameQuestionData q, string data)
    {
        if (!q.IsScored || string.IsNullOrEmpty(data)) return false;

        switch (q.Type)
        {
            case QuestionType.MultipleChoice:
            case QuestionType.TrueFalse:
            {
                var chosen = ParseIndexes(data).OrderBy(x => x).ToArray();
                var correct = q.Options.Select((o, i) => (o, i)).Where(x => x.o.IsCorrect).Select(x => x.i).OrderBy(x => x).ToArray();
                return correct.Length > 0 && chosen.SequenceEqual(correct);
            }
            case QuestionType.Puzzle:
            {
                var order = ParseIndexes(data);
                if (order.Length != q.Options.Count) return false;
                for (var i = 0; i < order.Length; i++)
                {
                    if (order[i] < 0 || order[i] >= q.Options.Count || q.Options[order[i]].CorrectPosition != i) return false;
                }
                return true;
            }
            case QuestionType.TypeAnswer:
            {
                var given = TextNormalizer.Normalize(data);
                return q.Options.Any(o => o.IsCorrect && TextNormalizer.Normalize(o.Text) == given);
            }
            case QuestionType.Slider:
            {
                if (!double.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || q.SliderCorrect is not double c) return false;
                return Math.Abs(v - c) <= q.SliderTolerance + 1e-9;
            }
        }
        return false;
    }

    public static int[] ParseIndexes(string data) =>
        data.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var i) ? i : -1).ToArray();
}
