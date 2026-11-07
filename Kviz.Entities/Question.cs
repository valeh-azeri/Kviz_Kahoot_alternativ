using Kviz.Entities.Enums;

namespace Kviz.Entities;

public class Question
{
    public Guid Id { get; set; }
    public Guid QuizId { get; set; }
    public Quiz? Quiz { get; set; }

    public int Order { get; set; }
    public QuestionType Type { get; set; }
    public string Text { get; set; } = string.Empty;
    /// <summary>Sualın şəkli: yapışdırılmış, yüklənmiş və ya ekrandan kəsilmiş. Sualın özü ola bilər.</summary>
    public string? ImageUrl { get; set; }
    /// <summary>Sualla bağlı əlavə şəkil (adətən internetdən gətirilən illüstrasiya). Sualın şəklindən ayrıdır.</summary>
    public string? RelatedImageUrl { get; set; }
    /// <summary>Əlavə şəklin müəllifi və lisenziyası (ekranda göstərilir).</summary>
    public string? RelatedImageCredit { get; set; }
    public int TimeLimitSeconds { get; set; } = 20;
    public PointsMode PointsMode { get; set; } = PointsMode.Standard;

    /// <summary>Test sualında birdən çox düzgün cavab seçməyə icazə.</summary>
    public bool MultiSelect { get; set; }

    // Sürüşdürücü (Slider) sualı üçün
    public double? SliderMin { get; set; }
    public double? SliderMax { get; set; }
    public double? SliderStep { get; set; }
    public double? SliderCorrect { get; set; }
    /// <summary>Düzgün cavabdan icazə verilən kənarlaşma (aralığın faizi ilə).</summary>
    public int SliderTolerancePercent { get; set; }

    public ICollection<AnswerOption> Options { get; set; } = new List<AnswerOption>();
}
