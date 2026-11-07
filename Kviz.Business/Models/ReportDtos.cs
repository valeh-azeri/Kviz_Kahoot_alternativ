using Kviz.Entities.Enums;

namespace Kviz.Business.Models;

public class ReportPlayerDto
{
    public Guid Id { get; set; }
    public int Rank { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string? TeamName { get; set; }
    public int Score { get; set; }
    public int CorrectCount { get; set; }
    public int AnsweredCount { get; set; }
    public int ScoredQuestions { get; set; }
    public double Accuracy { get; set; }
    public double AverageResponseMs { get; set; }
    public int MaxStreak { get; set; }
    public List<ReportPlayerAnswerDto> Answers { get; set; } = new();
}

public class ReportPlayerAnswerDto
{
    public int QuestionOrder { get; set; }
    public bool Answered { get; set; }
    public bool IsCorrect { get; set; }
    public int Points { get; set; }
    public int ResponseTimeMs { get; set; }
    public string Display { get; set; } = string.Empty;
}

public class ReportQuestionDto
{
    public int Order { get; set; }
    public QuestionType Type { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int TimeLimitSeconds { get; set; }
    public bool Scored { get; set; }
    public int Answered { get; set; }
    public int Correct { get; set; }
    public double CorrectPercent { get; set; }
    public double AverageResponseMs { get; set; }
    public QuestionResultDto Result { get; set; } = new();
}

public class GameReportDto
{
    public Guid GameId { get; set; }
    public string QuizTitle { get; set; } = string.Empty;
    public Guid? QuizId { get; set; }
    public string Pin { get; set; } = string.Empty;
    public GameMode Mode { get; set; }
    public GameStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime? Deadline { get; set; }
    public int PlayerCount { get; set; }
    public int QuestionCount { get; set; }
    public double AverageAccuracy { get; set; }
    public double AverageScore { get; set; }
    public List<ReportPlayerDto> Players { get; set; } = new();
    public List<ReportQuestionDto> Questions { get; set; } = new();
    public List<TeamScoreDto> Teams { get; set; } = new();
}
