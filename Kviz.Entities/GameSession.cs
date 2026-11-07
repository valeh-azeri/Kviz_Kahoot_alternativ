using Kviz.Entities.Enums;

namespace Kviz.Entities;

/// <summary>Bir oyun (canlı, komanda və ya tapşırıq). Kvizin snapshot-ı GameQuestion-larda saxlanılır.</summary>
public class GameSession
{
    public Guid Id { get; set; }
    public Guid? QuizId { get; set; }
    public Quiz? Quiz { get; set; }
    public string QuizTitle { get; set; } = string.Empty;
    public Guid HostId { get; set; }
    public ApplicationUser? Host { get; set; }

    public string Pin { get; set; } = string.Empty;
    public GameMode Mode { get; set; }
    public GameStatus Status { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    // Ayarlar
    public bool ShuffleQuestions { get; set; }
    public bool ShuffleAnswers { get; set; } = true;
    public bool ShowLeaderboard { get; set; } = true;
    /// <summary>Canlı oyunda liderlər cədvəli hər neçə sualdan sonra göstərilir (1 = hər sualdan sonra).</summary>
    public int LeaderboardEvery { get; set; } = 1;
    /// <summary>Canlı oyunda liderlər cədvəli və podium əvvəl yalnız hostda görünür, host "oyunçulara göstər" deyəndə oyunçulara açılır.</summary>
    public bool HostRevealsBoard { get; set; }
    /// <summary>Canlı oyunda oyunçular avatar kimi öz şəklini yükləyə bilər (söndürülsə yalnız hazır avatarlar).</summary>
    public bool AllowPhotoAvatars { get; set; } = true;
    public bool PowerUpsEnabled { get; set; }
    public bool StreakBonusEnabled { get; set; } = true;
    public int TeamCount { get; set; }
    /// <summary>Tapşırıq rejimində bitmə vaxtı (UTC).</summary>
    public DateTime? Deadline { get; set; }

    public ICollection<GameQuestion> Questions { get; set; } = new List<GameQuestion>();
    public ICollection<GamePlayer> Players { get; set; } = new List<GamePlayer>();
    public ICollection<GameTeam> Teams { get; set; } = new List<GameTeam>();
}
