namespace Kviz.Entities;

public class PlayerAnswer
{
    public Guid Id { get; set; }
    public Guid GamePlayerId { get; set; }
    public GamePlayer? Player { get; set; }
    public Guid GameQuestionId { get; set; }
    public GameQuestion? Question { get; set; }

    /// <summary>Cavab mətn kimi: variant indeksləri "0|2", mətn, rəqəm və s. Cavab verilməyibsə boşdur.</summary>
    public string AnswerData { get; set; } = string.Empty;
    public bool Answered { get; set; }
    public bool IsCorrect { get; set; }
    public int Points { get; set; }
    public int ResponseTimeMs { get; set; }
    public int StreakAfter { get; set; }
}
