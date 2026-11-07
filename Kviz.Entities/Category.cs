namespace Kviz.Entities;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "🎯";
    public int SortOrder { get; set; }

    public ICollection<Quiz> Quizzes { get; set; } = new List<Quiz>();
}
