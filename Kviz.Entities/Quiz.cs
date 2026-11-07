using Kviz.Entities.Enums;

namespace Kviz.Entities;

public class Quiz
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public ApplicationUser? Owner { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public string Language { get; set; } = "az";
    public QuizVisibility Visibility { get; set; } = QuizVisibility.Private;
    public int PlayCount { get; set; }
    /// <summary>Administrator kvizi hamıya açıqlıqdan çıxarıb: sahibi onu yenidən açıq edə bilməz.</summary>
    public bool AdminLocked { get; set; }
    public Guid? CopiedFromQuizId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Question> Questions { get; set; } = new List<Question>();
    public ICollection<QuizFavorite> Favorites { get; set; } = new List<QuizFavorite>();
    public ICollection<GameSession> Games { get; set; } = new List<GameSession>();
}
