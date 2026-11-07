using System.ComponentModel.DataAnnotations;
using Kviz.Business.Abstractions;
using Kviz.Business.Common;
using Kviz.Business.Models;

namespace Kviz.Web.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "E-poçt ünvanını daxil edin.")]
    [EmailAddress(ErrorMessage = "E-poçt ünvanı düzgün deyil.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Parolu daxil edin.")]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; } = true;
    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel
{
    [Required(ErrorMessage = "Adınızı daxil edin.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "Ad 2-60 simvol olmalıdır.")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-poçt ünvanını daxil edin.")]
    [EmailAddress(ErrorMessage = "E-poçt ünvanı düzgün deyil.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Parol təyin edin.")]
    [MinLength(6, ErrorMessage = "Parol ən azı 6 simvol olmalıdır.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Parolu təkrar yazın.")]
    [Compare(nameof(Password), ErrorMessage = "Parollar eyni deyil.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class ProfileViewModel
{
    public ProfileDto Profile { get; set; } = new();

    [Required(ErrorMessage = "Adınızı daxil edin.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "Ad 2-60 simvol olmalıdır.")]
    public string DisplayName { get; set; } = string.Empty;
}

public class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Cari parolu daxil edin.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni parolu daxil edin.")]
    [MinLength(6, ErrorMessage = "Parol ən azı 6 simvol olmalıdır.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni parolu təkrar yazın.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Parollar eyni deyil.")]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}

public class HomeViewModel
{
    public List<QuizCardDto> Popular { get; set; } = new();
    public bool ShowLibrary { get; set; } = true;
}

public class LibraryViewModel
{
    public PagedResult<QuizCardDto> Result { get; set; } = new();
    public LibraryQueryDto Query { get; set; } = new();
    public List<CategoryDto> Categories { get; set; } = new();
}

public class MyQuizzesViewModel
{
    public PagedResult<QuizCardDto> Result { get; set; } = new();
    public string? Search { get; set; }
    /// <summary>mine | fav</summary>
    public string Tab { get; set; } = "mine";
}

public class QuizEditorViewModel
{
    public QuizEditDto Quiz { get; set; } = new();
    public List<CategoryDto> Categories { get; set; } = new();
}

public class QuizImportViewModel
{
    [Required(ErrorMessage = "Kvizə ad verin.")]
    [StringLength(120, ErrorMessage = "Ad 120 simvoldan uzun ola bilməz.")]
    public string Title { get; set; } = string.Empty;
    public IFormFile? File { get; set; }
}

public class GameSetupViewModel
{
    public QuizCardDto Quiz { get; set; } = new();
    public CreateGameDto Options { get; set; } = new();
}

public class HostViewModel
{
    public HostGameDto Game { get; set; } = new();
    public JoinInfoDto Join { get; set; } = new();
}

public class ChallengeManageViewModel
{
    public HostGameDto Game { get; set; } = new();
    public JoinInfoDto Join { get; set; } = new();
    public List<LeaderboardEntryDto> Board { get; set; } = new();
}

public class HistoryViewModel
{
    public PagedResult<GameHistoryItemDto> Result { get; set; } = new();
}

public class PlayViewModel
{
    public string Pin { get; set; } = string.Empty;
    public string QuizTitle { get; set; } = string.Empty;
    public bool AllowPhotoAvatars { get; set; } = true;
}

public class JoinViewModel
{
    public string? Pin { get; set; }
    public string? Error { get; set; }
}

public class ErrorViewModel
{
    public int Code { get; set; } = 500;
    public string Title { get; set; } = "Xəta baş verdi";
    public string Message { get; set; } = "Gözlənilməz xəta oldu. Bir az sonra yenidən yoxlayın.";
}
