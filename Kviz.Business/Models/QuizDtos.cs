using Kviz.Entities.Enums;

namespace Kviz.Business.Models;

public class OptionEditDto
{
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
}

public class QuestionEditDto
{
    public QuestionType Type { get; set; } = QuestionType.MultipleChoice;
    public string Text { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? RelatedImageUrl { get; set; }
    public string? RelatedImageCredit { get; set; }
    public int TimeLimitSeconds { get; set; } = 20;
    public PointsMode PointsMode { get; set; } = PointsMode.Standard;
    public bool MultiSelect { get; set; }
    public double? SliderMin { get; set; }
    public double? SliderMax { get; set; }
    public double? SliderStep { get; set; }
    public double? SliderCorrect { get; set; }
    public int SliderTolerancePercent { get; set; }
    public List<OptionEditDto> Options { get; set; } = new();
}

public class QuizEditDto
{
    public Guid? Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }
    public int? CategoryId { get; set; }
    public string Language { get; set; } = "az";
    public bool IsPublic { get; set; }
    /// <summary>Yalnız oxumaq üçün: administrator kvizi hamıya açıqlıqdan çıxarıb.</summary>
    public bool AdminLocked { get; set; }
    public List<QuestionEditDto> Questions { get; set; } = new();
}

public class CategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
}

public class QuizCardDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryIcon { get; set; }
    public string Language { get; set; } = "az";
    public int QuestionCount { get; set; }
    public int PlayCount { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public bool AdminLocked { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsOwner { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class QuizQuestionSummaryDto
{
    public int Order { get; set; }
    public QuestionType Type { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int TimeLimitSeconds { get; set; }
    public PointsMode PointsMode { get; set; }
    /// <summary>Yalnız kvizin sahibinə göstərilir.</summary>
    public List<OptionEditDto>? Options { get; set; }
}

public class QuizDetailsDto
{
    public QuizCardDto Card { get; set; } = new();
    public List<QuizQuestionSummaryDto> Questions { get; set; } = new();
    public int TotalSeconds { get; set; }
    public List<GameHistoryItemDto> RecentGames { get; set; } = new();
}

public class LibraryQueryDto
{
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public string? Language { get; set; }
    /// <summary>popular | new</summary>
    public string Sort { get; set; } = "popular";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;
}

public class GameHistoryItemDto
{
    public Guid Id { get; set; }
    public string QuizTitle { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
    public Entities.Enums.GameMode Mode { get; set; }
    public Entities.Enums.GameStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public int PlayerCount { get; set; }
    public int QuestionCount { get; set; }
    public double AverageAccuracy { get; set; }
}

public class ImportResultDto
{
    public Guid QuizId { get; set; }
    public int ImportedQuestions { get; set; }
    public List<string> Warnings { get; set; } = new();
}
