using Kviz.Entities.Enums;

namespace Kviz.Business.Models;

public class CreateGameDto
{
    public Guid QuizId { get; set; }
    public GameMode Mode { get; set; } = GameMode.LiveClassic;
    public bool ShuffleQuestions { get; set; }
    public bool ShuffleAnswers { get; set; } = true;
    public bool ShowLeaderboard { get; set; } = true;
    /// <summary>Liderlər cədvəli hər neçə sualdan sonra (1, 2, 3, 5, 10...). 0 = göstərmə.</summary>
    public int LeaderboardEvery { get; set; } = 1;
    /// <summary>Canlı oyun: cədvəl və podium əvvəl yalnız hostda görünür, oyunçulara host açır.</summary>
    public bool HostRevealsBoard { get; set; }
    /// <summary>Canlı oyun: oyunçular öz şəklini avatar kimi yükləyə bilər.</summary>
    public bool AllowPhotoAvatars { get; set; } = true;
    public bool PowerUpsEnabled { get; set; }
    public bool StreakBonusEnabled { get; set; } = true;
    public int TeamCount { get; set; } = 2;
    /// <summary>Tapşırıq rejimi üçün: bitmə vaxtı (UTC). Boşdursa 7 gün.</summary>
    public DateTime? DeadlineUtc { get; set; }
}

public class GameCreatedDto
{
    public Guid GameId { get; set; }
    public string Pin { get; set; } = string.Empty;
    public GameMode Mode { get; set; }
}

public enum PinKind { NotFound, Live, Challenge, Closed }

public class PinResolveDto
{
    public PinKind Kind { get; set; }
    public Guid GameId { get; set; }
    public string Pin { get; set; } = string.Empty;
    public string QuizTitle { get; set; } = string.Empty;
    public string? Message { get; set; }
    public List<TeamDto> Teams { get; set; } = new();
    public bool IsTeamMode { get; set; }
    public bool AllowPhotoAvatars { get; set; } = true;
}

public class HostGameDto
{
    public Guid GameId { get; set; }
    public string Pin { get; set; } = string.Empty;
    public GameMode Mode { get; set; }
    public GameStatus Status { get; set; }
    public string QuizTitle { get; set; } = string.Empty;
    public int QuestionCount { get; set; }
    public DateTime? Deadline { get; set; }
    public bool PowerUpsEnabled { get; set; }
}

// ---------- Canlı oyun ----------

public class QuestionPublicDto
{
    public int Index { get; set; }
    public int Total { get; set; }
    public QuestionType Type { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? RelatedImageUrl { get; set; }
    public string? RelatedImageCredit { get; set; }
    public int TimeLimitSeconds { get; set; }
    public PointsMode PointsMode { get; set; }
    public List<string> Options { get; set; } = new();
    public bool MultiSelect { get; set; }
    public double? SliderMin { get; set; }
    public double? SliderMax { get; set; }
    public double? SliderStep { get; set; }
}

public class LobbyPlayerDto
{
    public Guid Id { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public Guid? TeamId { get; set; }
    public bool Connected { get; set; }
}

public class TeamDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ColorIndex { get; set; }
    public int MemberCount { get; set; }
}

public class LeaderboardEntryDto
{
    public Guid PlayerId { get; set; }
    public int Rank { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public string? TeamName { get; set; }
    public int TeamColor { get; set; }
    public int Score { get; set; }
    public int Delta { get; set; }
    public int Streak { get; set; }
    public int Correct { get; set; }
}

public class TeamScoreDto
{
    public Guid TeamId { get; set; }
    public int Rank { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ColorIndex { get; set; }
    public int Score { get; set; }
    public int Members { get; set; }
}

public class WordCountDto
{
    public string Word { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>Bir variantı seçənlər (yalnız host görür). Komanda rejimində komandalara görə qruplaşır.</summary>
public class OptionVotersDto
{
    public List<string> Players { get; set; } = new();
    public List<VoterTeamDto> Teams { get; set; } = new();
}

public class VoterTeamDto
{
    public string Name { get; set; } = string.Empty;
    public int ColorIndex { get; set; }
    public List<string> Players { get; set; } = new();
}

public class QuestionResultDto
{
    /// <summary>Hər variant üçün onu seçənlər (yalnız canlı oyunda, host üçün).</summary>
    public List<OptionVotersDto> OptionVoters { get; set; } = new();
    public int Index { get; set; }
    public QuestionType Type { get; set; }
    public int Answered { get; set; }
    public int Correct { get; set; }
    public int TotalPlayers { get; set; }
    public List<string> OptionTexts { get; set; } = new();
    public List<int> OptionCounts { get; set; } = new();
    public List<int> CorrectIndexes { get; set; } = new();
    /// <summary>Puzzle: düzgün ardıcıllıq (mətnlər).</summary>
    public List<string> CorrectOrder { get; set; } = new();
    /// <summary>"Cavabı yaz": qəbul olunan cavablar.</summary>
    public List<string> AcceptedAnswers { get; set; } = new();
    public double? SliderMin { get; set; }
    public double? SliderMax { get; set; }
    public double? SliderCorrect { get; set; }
    public double SliderTolerance { get; set; }
    public List<double> SliderValues { get; set; } = new();
    /// <summary>"Cavabı yaz" üçün verilmiş cavablar, söz buludu üçün sözlər.</summary>
    public List<WordCountDto> Words { get; set; } = new();
    public List<string> Texts { get; set; } = new();
    public double AverageResponseMs { get; set; }
}

public class HostStateDto
{
    public string Phase { get; set; } = "lobby";
    public Guid GameId { get; set; }
    public string Pin { get; set; } = string.Empty;
    public GameMode Mode { get; set; }
    public string QuizTitle { get; set; } = string.Empty;
    public int TotalQuestions { get; set; }
    public int QuestionIndex { get; set; } = -1;
    public bool Locked { get; set; }
    public bool PowerUps { get; set; }
    public bool ShowLeaderboard { get; set; }
    /// <summary>Host "növbəti"-yə basanda liderlər cədvəli açılacaq (yoxsa birbaşa növbəti sual).</summary>
    public bool LeaderboardNext { get; set; }
    /// <summary>Bu oyunda cədvəli oyunçulara host açır.</summary>
    public bool HostReveals { get; set; }
    /// <summary>Host cədvəli oyunçulara artıq açıb.</summary>
    public bool Revealed { get; set; }
    public bool IsLastQuestion { get; set; }
    public List<LobbyPlayerDto> Players { get; set; } = new();
    public List<TeamDto> Teams { get; set; } = new();
    public QuestionPublicDto? Question { get; set; }
    public int RemainingMs { get; set; }
    public int AnsweredCount { get; set; }
    public int PlayerCount { get; set; }
    public QuestionResultDto? Result { get; set; }
    public List<LeaderboardEntryDto> Leaderboard { get; set; } = new();
    public List<TeamScoreDto> TeamLeaderboard { get; set; } = new();
}

public class PlayerYouDto
{
    public Guid Id { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public Guid? TeamId { get; set; }
    public string? TeamName { get; set; }
    public int TeamColor { get; set; }
    public int Score { get; set; }
    public int Rank { get; set; }
    public int Streak { get; set; }
    public int Correct { get; set; }
    public bool DoubleUsed { get; set; }
    public bool FiftyUsed { get; set; }
    public bool ShieldUsed { get; set; }
    public bool DoubleArmed { get; set; }
    public bool ShieldArmed { get; set; }
}

public class PlayerResultDto
{
    public bool Scored { get; set; }
    public bool Answered { get; set; }
    public bool Correct { get; set; }
    public int Points { get; set; }
    public int StreakBonus { get; set; }
    public int Streak { get; set; }
    public int Score { get; set; }
    public int Rank { get; set; }
    public int? PointsBehind { get; set; }
    public string? NextNickname { get; set; }
}

public class PlayerStateDto
{
    public string Phase { get; set; } = "lobby";
    public string Pin { get; set; } = string.Empty;
    public string QuizTitle { get; set; } = string.Empty;
    public GameMode Mode { get; set; }
    public int TotalQuestions { get; set; }
    public int PlayerCount { get; set; }
    public bool PowerUps { get; set; }
    /// <summary>Host nəticələri hələ açmayıb: yer, ümumi xal və cədvəl oyunçuya göndərilmir.</summary>
    public bool Concealed { get; set; }
    /// <summary>Gözləmə otağında oyunçu öz şəklini yükləyə bilər.</summary>
    public bool AllowPhotoAvatars { get; set; }
    public PlayerYouDto You { get; set; } = new();
    public QuestionPublicDto? Question { get; set; }
    public int RemainingMs { get; set; }
    public bool Answered { get; set; }
    public List<int> HiddenOptions { get; set; } = new();
    public PlayerResultDto? Result { get; set; }
    public List<LeaderboardEntryDto> Leaderboard { get; set; } = new();
    public List<TeamScoreDto> TeamLeaderboard { get; set; } = new();
}

public class JoinResultDto
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Guid GameId { get; set; }
    public Guid PlayerId { get; set; }
    public Guid Token { get; set; }
    public PlayerStateDto? State { get; set; }
}

public class AckDto
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public static AckDto Success() => new() { Ok = true };
    public static AckDto Fail(string error) => new() { Error = error };
}

// ---------- Tapşırıq (Challenge) ----------

public class ChallengeStateDto
{
    /// <summary>question | finished | closed</summary>
    public string Phase { get; set; } = "question";
    public string QuizTitle { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public Guid PlayerId { get; set; }
    public QuestionPublicDto? Question { get; set; }
    public int RemainingMs { get; set; }
    public int Score { get; set; }
    public int Streak { get; set; }
    public int Correct { get; set; }
    public int TotalQuestions { get; set; }
    public int Rank { get; set; }
    public int PlayerCount { get; set; }
    public DateTime? Deadline { get; set; }
    public List<LeaderboardEntryDto> Leaderboard { get; set; } = new();
    public bool ShowLeaderboard { get; set; }
    public string? Message { get; set; }
}

public class ChallengeJoinResultDto
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Guid Token { get; set; }
}

public class ChallengeFeedbackDto
{
    public bool Accepted { get; set; }
    public string? Error { get; set; }
    public bool Scored { get; set; }
    public bool TimedOut { get; set; }
    public bool Correct { get; set; }
    public int Points { get; set; }
    public int StreakBonus { get; set; }
    public int Streak { get; set; }
    public int Score { get; set; }
    public QuestionResultDto? Reveal { get; set; }
}
