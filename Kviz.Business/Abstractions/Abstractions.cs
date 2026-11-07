using Kviz.Business.Common;
using Kviz.Business.Games;
using Kviz.Business.Models;

namespace Kviz.Business.Abstractions;

/// <summary>Canlı oyun hadisələrini müştərilərə çatdıran qapı. Web qatında SignalR ilə həyata keçirilir.</summary>
public interface IGameNotifier
{
    Task ToHostAsync(Guid gameId, string eventName, object payload);
    Task ToPlayerAsync(Guid playerId, string eventName, object payload);
}

/// <summary>Yüklənmiş şəkilləri saxlayır. Web qatında fayl sistemi ilə həyata keçirilir.</summary>
public interface IFileStorage
{
    Task<string> SaveImageAsync(Stream content, string extension, CancellationToken ct = default);
    /// <summary>Oyunçu avatarını (JPEG) /uploads/avatars/ qovluğuna yazır, ünvanını qaytarır.</summary>
    Task<string> SaveAvatarAsync(Stream content, CancellationToken ct = default);
    /// <summary>Avatar faylını silir (yalnız /uploads/avatars/ ünvanları qəbul olunur).</summary>
    void DeleteAvatar(string url);
}

public interface IAccountService
{
    Task<bool> IsRegistrationOpenAsync();
    Task<ServiceResult> RegisterAsync(RegisterDto dto);
    Task<ServiceResult> LoginAsync(LoginDto dto);
    Task LogoutAsync();
    Task<ProfileDto?> GetProfileAsync(Guid userId);
    Task<ServiceResult> UpdateProfileAsync(Guid userId, string displayName);
    Task<ServiceResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);
}

public class RegisterDto
{
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
}

public class ProfileDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int QuizCount { get; set; }
    public int GameCount { get; set; }
}

public interface IQuizService
{
    Task<List<CategoryDto>> GetCategoriesAsync();
    Task<QuizEditDto?> GetForEditAsync(Guid quizId, Guid userId);
    QuizEditDto NewDraft();
    Task<ServiceResult<Guid>> SaveAsync(Guid userId, QuizEditDto dto);
    Task<QuizDetailsDto?> GetDetailsAsync(Guid quizId, Guid? userId, bool isAdmin = false);
    Task<PagedResult<QuizCardDto>> ListMineAsync(Guid userId, string? search, int page, int pageSize = 12);
    Task<PagedResult<QuizCardDto>> ListFavoritesAsync(Guid userId, int page, int pageSize = 12);
    Task<PagedResult<QuizCardDto>> SearchPublicAsync(LibraryQueryDto query, Guid? userId);
    Task<ServiceResult> DeleteAsync(Guid quizId, Guid userId);
    Task<ServiceResult<Guid>> CopyAsync(Guid quizId, Guid userId);
    Task<ServiceResult> SetVisibilityAsync(Guid quizId, Guid userId, bool isPublic);
    Task<bool> ToggleFavoriteAsync(Guid quizId, Guid userId);
}

public interface IQuizExcelService
{
    byte[] BuildTemplate();
    Task<ServiceResult<ImportResultDto>> ImportAsync(Guid userId, string title, Stream file);
    Task<byte[]?> ExportQuizAsync(Guid quizId, Guid userId);
}

public interface IGameService
{
    Task<ServiceResult<GameCreatedDto>> CreateAsync(Guid userId, CreateGameDto dto);
    Task<PinResolveDto> ResolvePinAsync(string pin);
    Task<HostGameDto?> GetHostGameAsync(Guid gameId, Guid userId);
    Task<PagedResult<GameHistoryItemDto>> ListHostGamesAsync(Guid userId, int page, int pageSize = 15);
    Task<ServiceResult> DeleteAsync(Guid gameId, Guid userId);
    Task<ServiceResult> CloseChallengeAsync(Guid gameId, Guid userId);
    Task<List<LeaderboardEntryDto>> GetChallengeLeaderboardAsync(Guid gameId, Guid userId, int take = 50);
}

public interface IChallengeService
{
    Task<ChallengeJoinResultDto> JoinAsync(string pin, string nickname);
    Task<ChallengeStateDto?> GetStateAsync(Guid token);
    Task<ChallengeFeedbackDto> AnswerAsync(Guid token, AnswerSubmission submission);
}

public interface IReportService
{
    Task<GameReportDto?> GetReportAsync(Guid gameId, Guid userId);
    Task<byte[]?> ExportExcelAsync(Guid gameId, Guid userId);
    Task<byte[]?> ExportPdfAsync(Guid gameId, Guid userId);
}

public interface IJoinLinkService
{
    JoinInfoDto Build(string scheme, string host, int? port, string pin, string? preferredBaseUrl = null);
}

public interface IQrService
{
    string GenerateSvg(string text);
}
