namespace Kviz.Entities;

public class AnswerOption
{
    public Guid Id { get; set; }
    public Guid QuestionId { get; set; }
    public Question? Question { get; set; }

    /// <summary>Göstərilmə sırası. Puzzle sualında düzgün ardıcıllıq da budur.</summary>
    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
}
