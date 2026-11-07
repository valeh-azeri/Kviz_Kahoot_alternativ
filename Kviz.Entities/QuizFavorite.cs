namespace Kviz.Entities;

public class QuizFavorite
{
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public Guid QuizId { get; set; }
    public Quiz? Quiz { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
