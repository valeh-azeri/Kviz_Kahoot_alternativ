using Kviz.Business.Abstractions;
using Kviz.Business.Models;
using Kviz.Web.Infrastructure;
using Kviz.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kviz.Web.Controllers;

public class HomeController : Controller
{
    private readonly IQuizService _quizzes;
    private readonly Kviz.Business.Services.ISettingsService _settings;
    public HomeController(IQuizService quizzes, Kviz.Business.Services.ISettingsService settings)
    {
        _quizzes = quizzes;
        _settings = settings;
    }

    public async Task<IActionResult> Index()
    {
        // Admin kitabxanaya qeydiyyatsız baxışı bağlayıbsa, qonaqlara kvizlər göstərilmir
        var showLibrary = User.Identity?.IsAuthenticated == true || await _settings.IsGuestLibraryOpenAsync();
        var vm = new HomeViewModel { ShowLibrary = showLibrary };
        if (showLibrary)
        {
            var popular = await _quizzes.SearchPublicAsync(new LibraryQueryDto { PageSize = 6, Sort = "popular" }, User.GetUserIdOrNull());
            vm.Popular = popular.Items;
        }
        return View(vm);
    }

    [Route("Home/StatusCode")]
    public IActionResult StatusPage(int code = 500)
    {
        Response.StatusCode = code;
        var vm = code switch
        {
            404 => new ErrorViewModel { Code = 404, Title = "Səhifə tapılmadı", Message = "Axtardığınız səhifə mövcud deyil və ya silinib." },
            403 => new ErrorViewModel { Code = 403, Title = "İcazə yoxdur", Message = "Bu səhifəyə baxmaq üçün icazəniz yoxdur." },
            429 => new ErrorViewModel { Code = 429, Title = "Çox sürətli sorğular", Message = "Bir az gözləyib yenidən cəhd edin." },
            _ => new ErrorViewModel { Code = code }
        };
        return View("Error", vm);
    }

    [Route("Home/Error")]
    public IActionResult Error()
    {
        Response.StatusCode = 500;
        return View(new ErrorViewModel());
    }
}

public class AccountController : Controller
{
    private readonly IAccountService _accounts;
    public AccountController(IAccountService accounts) => _accounts = accounts;

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToLocal(returnUrl);
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await _accounts.LoginAsync(new LoginDto { Email = model.Email, Password = model.Password, RememberMe = model.RememberMe });
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.FirstError);
            return View(model);
        }
        return RedirectToLocal(model.ReturnUrl);
    }

    [HttpGet]
    public async Task<IActionResult> Register()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Quiz");
        if (!await _accounts.IsRegistrationOpenAsync()) return View("RegistrationClosed");
        return View(new RegisterViewModel());
    }

    [HttpPost, EnableRateLimiting("login")]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await _accounts.RegisterAsync(new RegisterDto { DisplayName = model.DisplayName, Email = model.Email, Password = model.Password });
        if (!result.Success)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(MapField(e.Path), e.Message);
            return View(model);
        }
        return RedirectToAction("Index", "Quiz");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await _accounts.LogoutAsync();
        return RedirectToAction("Index", "Home");
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> Profile()
    {
        var p = await _accounts.GetProfileAsync(User.GetUserId());
        if (p == null) return NotFound();
        ViewBag.Password = new ChangePasswordViewModel();
        return View(new ProfileViewModel { Profile = p, DisplayName = p.DisplayName });
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> UpdateProfile(ProfileViewModel model)
    {
        var p = await _accounts.GetProfileAsync(User.GetUserId());
        if (p == null) return NotFound();
        model.Profile = p;
        ViewBag.Password = new ChangePasswordViewModel();
        if (!ModelState.IsValid) return View("Profile", model);
        var r = await _accounts.UpdateProfileAsync(User.GetUserId(), model.DisplayName);
        if (!r.Success)
        {
            ModelState.AddModelError(nameof(model.DisplayName), r.FirstError);
            return View("Profile", model);
        }
        TempData["Success"] = "Profil yeniləndi.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        var p = await _accounts.GetProfileAsync(User.GetUserId());
        if (p == null) return NotFound();
        var vm = new ProfileViewModel { Profile = p, DisplayName = p.DisplayName };
        ViewBag.Password = model;
        if (!ModelState.IsValid) return View("Profile", vm);
        var r = await _accounts.ChangePasswordAsync(User.GetUserId(), model.CurrentPassword, model.NewPassword);
        if (!r.Success)
        {
            ModelState.AddModelError(r.Errors[0].Path == "password" && r.FirstError.Contains("Cari") ? nameof(model.CurrentPassword) : nameof(model.NewPassword), r.FirstError);
            return View("Profile", vm);
        }
        TempData["Success"] = "Parol dəyişdirildi.";
        return RedirectToAction(nameof(Profile));
    }

    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Quiz");

    private static string MapField(string path) => path switch
    {
        "displayName" => nameof(RegisterViewModel.DisplayName),
        "email" => nameof(RegisterViewModel.Email),
        "password" => nameof(RegisterViewModel.Password),
        _ => string.Empty
    };
}
