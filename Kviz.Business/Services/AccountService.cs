using Kviz.Business.Abstractions;
using Kviz.Business.Common;
using Kviz.DataAccess.Repositories;
using Kviz.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kviz.Business.Services;

public class AccountService : IAccountService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly IRepository<Quiz> _quizzes;
    private readonly IRepository<GameSession> _games;
    private readonly RoleManager<IdentityRole<Guid>> _roles;
    private readonly ISettingsService _settings;
    private readonly AdminOptions _adminOptions;

    public AccountService(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
        IRepository<Quiz> quizzes, IRepository<GameSession> games, RoleManager<IdentityRole<Guid>> roles,
        ISettingsService settings, AdminOptions adminOptions)
    {
        _users = users;
        _signIn = signIn;
        _quizzes = quizzes;
        _games = games;
        _roles = roles;
        _settings = settings;
        _adminOptions = adminOptions;
    }

    /// <summary>Qeydiyyat səhifəsi üçün: sistemdə admin yoxdursa qeydiyyat həmişə açıqdır (ilk istifadəçi admin olur).</summary>
    public async Task<bool> IsRegistrationOpenAsync() =>
        await _settings.IsRegistrationOpenAsync() || (await _users.GetUsersInRoleAsync(AppRoles.Admin)).Count == 0;

    public async Task<ServiceResult> RegisterAsync(RegisterDto dto)
    {
        var name = (dto.DisplayName ?? string.Empty).Trim();
        var email = (dto.Email ?? string.Empty).Trim();
        var errors = new ServiceResult();
        if (name.Length < 2 || name.Length > 60) errors.Errors.Add(new ValidationError("displayName", "Ad 2-60 simvol olmalıdır."));
        if (!IsEmail(email)) errors.Errors.Add(new ValidationError("email", "E-poçt ünvanı düzgün deyil."));
        if (string.IsNullOrEmpty(dto.Password)) errors.Errors.Add(new ValidationError("password", "Parol daxil edin."));
        if (!errors.Success) return errors;
        if (!await IsRegistrationOpenAsync()) return ServiceResult.Fail("Qeydiyyat hazırda bağlıdır. Hesab üçün administrasiya ilə əlaqə saxlayın.");

        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = name, CreatedAt = DateTime.UtcNow };
        var result = await _users.CreateAsync(user, dto.Password);
        if (!result.Succeeded) return FromIdentity(result);

        // Konfiqurasiyadakı e-poçt administrator olur. Konfiqurasiyada heç bir admin e-poçtu yoxdursa,
        // yalnız ilk qeydiyyatdan keçən istifadəçi admin olur (yeni quraşdırmanı işə salmaq üçün).
        if (!await _roles.RoleExistsAsync(AppRoles.Admin)) await _roles.CreateAsync(new IdentityRole<Guid>(AppRoles.Admin));
        var configuredEmails = _adminOptions.Emails.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        var configured = configuredEmails.Any(e => string.Equals(e.Trim(), email, StringComparison.OrdinalIgnoreCase));
        var firstUserRule = configuredEmails.Count == 0 && (await _users.GetUsersInRoleAsync(AppRoles.Admin)).Count == 0;
        if (configured || firstUserRule)
            await _users.AddToRoleAsync(user, AppRoles.Admin);

        await _signIn.SignInAsync(user, isPersistent: true);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> LoginAsync(LoginDto dto)
    {
        var email = (dto.Email ?? string.Empty).Trim();
        var result = await _signIn.PasswordSignInAsync(email, dto.Password ?? string.Empty, dto.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded) return ServiceResult.Ok();
        if (result.IsLockedOut) return ServiceResult.Fail("Çox sayda uğursuz cəhd. Bir neçə dəqiqə sonra yenidən yoxlayın.");
        return ServiceResult.Fail("E-poçt və ya parol yanlışdır.");
    }

    public Task LogoutAsync() => _signIn.SignOutAsync();

    public async Task<ProfileDto?> GetProfileAsync(Guid userId)
    {
        var u = await _users.FindByIdAsync(userId.ToString());
        if (u == null) return null;
        return new ProfileDto
        {
            Id = u.Id, DisplayName = u.DisplayName, Email = u.Email ?? string.Empty, CreatedAt = u.CreatedAt,
            QuizCount = await _quizzes.QueryNoTracking().CountAsync(q => q.OwnerId == userId),
            GameCount = await _games.QueryNoTracking().CountAsync(g => g.HostId == userId)
        };
    }

    public async Task<ServiceResult> UpdateProfileAsync(Guid userId, string displayName)
    {
        var name = (displayName ?? string.Empty).Trim();
        if (name.Length < 2 || name.Length > 60) return ServiceResult.Fail("Ad 2-60 simvol olmalıdır.", "displayName");
        var u = await _users.FindByIdAsync(userId.ToString());
        if (u == null) return ServiceResult.Fail("İstifadəçi tapılmadı.");
        u.DisplayName = name;
        var r = await _users.UpdateAsync(u);
        return r.Succeeded ? ServiceResult.Ok() : FromIdentity(r);
    }

    public async Task<ServiceResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        var u = await _users.FindByIdAsync(userId.ToString());
        if (u == null) return ServiceResult.Fail("İstifadəçi tapılmadı.");
        var r = await _users.ChangePasswordAsync(u, currentPassword ?? string.Empty, newPassword ?? string.Empty);
        return r.Succeeded ? ServiceResult.Ok() : FromIdentity(r);
    }

    private static bool IsEmail(string s) =>
        s.Length is > 3 and <= 150 && s.Contains('@') && s.IndexOf('@') > 0 && s.LastIndexOf('.') > s.IndexOf('@') + 1 && !s.Contains(' ');

    private static ServiceResult FromIdentity(IdentityResult result)
    {
        var res = new ServiceResult();
        foreach (var e in result.Errors)
        {
            var msg = e.Code switch
            {
                "DuplicateUserName" or "DuplicateEmail" => "Bu e-poçt ilə artıq qeydiyyatdan keçilib.",
                "InvalidEmail" or "InvalidUserName" => "E-poçt ünvanı düzgün deyil.",
                "PasswordTooShort" => "Parol ən azı 6 simvol olmalıdır.",
                "PasswordRequiresDigit" => "Parolda ən azı bir rəqəm olmalıdır.",
                "PasswordRequiresLower" => "Parolda ən azı bir kiçik hərf olmalıdır.",
                "PasswordRequiresUpper" => "Parolda ən azı bir böyük hərf olmalıdır.",
                "PasswordRequiresNonAlphanumeric" => "Parolda ən azı bir xüsusi simvol olmalıdır.",
                "PasswordMismatch" => "Cari parol yanlışdır.",
                _ => "Əməliyyat alınmadı. Məlumatları yoxlayın."
            };
            var path = e.Code.StartsWith("Password") ? "password" : e.Code.Contains("Email") || e.Code.Contains("UserName") ? "email" : "";
            if (!res.Errors.Any(x => x.Message == msg)) res.Errors.Add(new ValidationError(path, msg));
        }
        if (res.Errors.Count == 0) res.Errors.Add(new ValidationError("", "Əməliyyat alınmadı."));
        return res;
    }
}
