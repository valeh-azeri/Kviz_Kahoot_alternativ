using Microsoft.AspNetCore.Identity;

namespace Kviz.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Quiz> Quizzes { get; set; } = new List<Quiz>();
    public ICollection<QuizFavorite> Favorites { get; set; } = new List<QuizFavorite>();
}
