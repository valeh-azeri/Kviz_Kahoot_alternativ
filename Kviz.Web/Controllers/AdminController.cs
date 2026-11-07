using Kviz.Business.Common;
using Kviz.Business.Models;
using Kviz.Business.Services;
using Kviz.Entities;
using Kviz.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kviz.Web.Controllers;

/// <summary>Administrator paneli: yalnız Admin rolundakı istifadəçilər üçün.</summary>
[Authorize(Roles = AppRoles.Admin)]
[Route("Admin")]
public class AdminController : Controller
{
    private readonly IAdminService _admin;
    private readonly ISettingsService _settings;

    public AdminController(IAdminService admin, ISettingsService settings)
    {
        _admin = admin;
        _settings = settings;
    }

    private Guid Me => User.GetUserId();

    private IActionResult Back(string? returnUrl, string fallbackAction) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(fallbackAction);

    private void Flash(ServiceResult r, string ok)
    {
        TempData[r.Success ? "Success" : "Error"] = r.Success ? ok : r.FirstError;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await _admin.GetDashboardAsync());

    // ---------- İstifadəçilər ----------

    [HttpGet("Users")]
    public async Task<IActionResult> Users(string? q, string? filter, int page = 1)
    {
        ViewBag.Q = q; ViewBag.Filter = filter;
        return View(await _admin.ListUsersAsync(q, filter, page));
    }

    [HttpPost("Users/Create")]
    public async Task<IActionResult> CreateUser(string displayName, string email, string password, bool makeAdmin)
    {
        var r = await _admin.CreateUserAsync(displayName, email, password, makeAdmin);
        Flash(r, $"{email} üçün hesab yaradıldı. Parolu istifadəçiyə özünüz ötürün.");
        return RedirectToAction(nameof(Users));
    }

    [HttpPost("Users/{id:guid}/Block")]
    public async Task<IActionResult> Block(Guid id, bool blocked, string? returnUrl)
    {
        Flash(await _admin.SetBlockedAsync(Me, id, blocked), blocked ? "İstifadəçi bloklandı." : "Blok götürüldü.");
        return Back(returnUrl, nameof(Users));
    }

    [HttpPost("Users/{id:guid}/Admin")]
    public async Task<IActionResult> SetAdmin(Guid id, bool isAdmin, string? returnUrl)
    {
        Flash(await _admin.SetAdminAsync(Me, id, isAdmin), isAdmin ? "İstifadəçi administrator edildi." : "Admin rolu götürüldü.");
        return Back(returnUrl, nameof(Users));
    }

    [HttpPost("Users/{id:guid}/ResetPassword")]
    public async Task<IActionResult> ResetPassword(Guid id, string? returnUrl)
    {
        var r = await _admin.ResetPasswordAsync(Me, id);
        if (r.Success) TempData["Success"] = $"Müvəqqəti parol: {r.Value}  (yalnız indi göstərilir. İstifadəçiyə ötürün, o profildən dəyişsin.)";
        else TempData["Error"] = r.FirstError;
        return Back(returnUrl, nameof(Users));
    }

    [HttpPost("Users/{id:guid}/Delete")]
    public async Task<IActionResult> DeleteUser(Guid id, string? returnUrl)
    {
        Flash(await _admin.DeleteUserAsync(Me, id), "İstifadəçi və onun kvizləri, oyunları silindi.");
        return Back(returnUrl, nameof(Users));
    }

    // ---------- Kvizlər ----------

    [HttpGet("Quizzes")]
    public async Task<IActionResult> Quizzes(string? q, string? filter, int page = 1)
    {
        ViewBag.Q = q; ViewBag.Filter = filter;
        return View(await _admin.ListQuizzesAsync(q, filter, page));
    }

    [HttpPost("Quizzes/{id:guid}/Lock")]
    public async Task<IActionResult> LockQuiz(Guid id, bool locked, string? returnUrl)
    {
        Flash(await _admin.SetQuizLockedAsync(id, locked), locked ? "Kviz kitabxanadan çıxarıldı və kilidləndi." : "Kilid götürüldü. Sahibi onu yenidən hamıya aça bilər.");
        return Back(returnUrl, nameof(Quizzes));
    }

    [HttpPost("Quizzes/{id:guid}/Delete")]
    public async Task<IActionResult> DeleteQuiz(Guid id, string? returnUrl)
    {
        Flash(await _admin.DeleteQuizAsync(id), "Kviz silindi.");
        return Back(returnUrl, nameof(Quizzes));
    }

    // ---------- Oyunlar ----------

    [HttpGet("Games")]
    public async Task<IActionResult> Games(string? filter, int page = 1)
    {
        ViewBag.Filter = filter;
        return View(await _admin.ListGamesAsync(filter, page));
    }

    [HttpPost("Games/{id:guid}/Cancel")]
    public async Task<IActionResult> CancelGame(Guid id, string? returnUrl)
    {
        Flash(await _admin.CancelGameAsync(id), "Oyun bağlandı.");
        return Back(returnUrl, nameof(Games));
    }

    [HttpPost("Games/{id:guid}/Delete")]
    public async Task<IActionResult> DeleteGame(Guid id, string? returnUrl)
    {
        Flash(await _admin.DeleteGameAsync(id), "Oyun və hesabatı silindi.");
        return Back(returnUrl, nameof(Games));
    }

    // ---------- Kateqoriyalar ----------

    [HttpGet("Categories")]
    public async Task<IActionResult> Categories() => View(await _admin.ListCategoriesAsync());

    [HttpPost("Categories/Save")]
    public async Task<IActionResult> SaveCategory(int? id, string name, string icon)
    {
        Flash(await _admin.SaveCategoryAsync(id, name, icon), id == null ? "Kateqoriya əlavə olundu." : "Kateqoriya yeniləndi.");
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost("Categories/{id:int}/Move")]
    public async Task<IActionResult> MoveCategory(int id, bool up)
    {
        Flash(await _admin.MoveCategoryAsync(id, up), "Sıra dəyişdi.");
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost("Categories/{id:int}/Delete")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        Flash(await _admin.DeleteCategoryAsync(id), "Kateqoriya silindi. Onun kvizləri kateqoriyasız qaldı.");
        return RedirectToAction(nameof(Categories));
    }

    // ---------- Ayarlar ----------

    [HttpGet("Settings")]
    public async Task<IActionResult> Settings()
    {
        ViewBag.RegistrationOpen = await _settings.IsRegistrationOpenAsync();
        ViewBag.GuestLibrary = await _settings.IsGuestLibraryOpenAsync();
        return View();
    }

    [HttpPost("Settings")]
    public async Task<IActionResult> SaveSettings(bool registrationOpen, bool guestLibrary)
    {
        await _settings.SetRegistrationOpenAsync(registrationOpen);
        await _settings.SetGuestLibraryOpenAsync(guestLibrary);
        TempData["Success"] = "Ayarlar yadda saxlanıldı.";
        return RedirectToAction(nameof(Settings));
    }
}
