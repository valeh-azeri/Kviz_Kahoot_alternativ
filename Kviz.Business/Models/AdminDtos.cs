using Kviz.Entities.Enums;

namespace Kviz.Business.Models;

public class AdminUserDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsBlocked { get; set; }
    public int QuizCount { get; set; }
    public int GameCount { get; set; }
}

public class AdminQuizDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public int QuestionCount { get; set; }
    public int PlayCount { get; set; }
    public bool IsPublic { get; set; }
    public bool AdminLocked { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AdminGameDto
{
    public Guid Id { get; set; }
    public string QuizTitle { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
    public GameMode Mode { get; set; }
    public GameStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public int PlayerCount { get; set; }
}

public class AdminCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public int QuizCount { get; set; }
}

public class AdminDashboardDto
{
    public int Users { get; set; }
    public int Admins { get; set; }
    public int BlockedUsers { get; set; }
    public int NewUsers7Days { get; set; }
    public int Quizzes { get; set; }
    public int PublicQuizzes { get; set; }
    public int LockedQuizzes { get; set; }
    public int Games { get; set; }
    public int GamesToday { get; set; }
    public int ActiveGames { get; set; }
    public int Players { get; set; }
    public bool RegistrationOpen { get; set; }
    public List<AdminUserDto> RecentUsers { get; set; } = new();
    public List<AdminGameDto> RecentGames { get; set; } = new();
}
