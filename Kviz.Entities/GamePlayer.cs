using Kviz.Entities.Enums;

namespace Kviz.Entities;

public class GamePlayer
{
    public Guid Id { get; set; }
    public Guid GameSessionId { get; set; }
    public GameSession? GameSession { get; set; }
    public Guid? TeamId { get; set; }
    public GameTeam? Team { get; set; }

    public string Nickname { get; set; } = string.Empty;
    /// <summary>Avatar: hazır üçün "e:🐱", yüklənmiş şəkil üçün "/uploads/avatars/….jpg". Tapşırıq rejimində boşdur.</summary>
    public string? Avatar { get; set; }
    /// <summary>Yenidən qoşulma üçün gizli açar.</summary>
    public Guid Token { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public bool IsKicked { get; set; }

    public int TotalScore { get; set; }
    public int CorrectCount { get; set; }
    public int AnsweredCount { get; set; }
    public int Streak { get; set; }
    public int MaxStreak { get; set; }
    public long TotalResponseMs { get; set; }
    public int FinalRank { get; set; }
    public PowerUpFlags UsedPowerUps { get; set; }

    // Tapşırıq rejimi üçün: oyunçu sualları öz tempi ilə keçir
    public int CurrentQuestionIndex { get; set; }
    public DateTime? QuestionServedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public ICollection<PlayerAnswer> Answers { get; set; } = new List<PlayerAnswer>();
}
