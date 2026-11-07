using Kviz.Entities.Enums;

namespace Kviz.Entities;

/// <summary>Oyun yaradılan anda kviz sualının surəti. Sonradan kviz dəyişsə də hesabat pozulmur.</summary>
public class GameQuestion
{
    public Guid Id { get; set; }
    public Guid GameSessionId { get; set; }
    public GameSession? GameSession { get; set; }

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

    /// <summary>Variantlar JSON kimi (göstərilmə sırası ilə). Quruluş Business qatında təyin olunur.</summary>
    public string OptionsJson { get; set; } = "[]";

    public ICollection<PlayerAnswer> Answers { get; set; } = new List<PlayerAnswer>();
}
