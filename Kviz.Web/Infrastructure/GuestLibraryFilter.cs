using Kviz.Business.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Kviz.Web.Infrastructure;

/// <summary>
/// Kvizlər kitabxanasına və kviz təsvirinə baxışı idarə edir. Admin "qeydiyyatsız baxış"ı bağlayıbsa,
/// daxil olmamış ziyarətçi giriş səhifəsinə yönləndirilir. Oyuna PIN ilə qoşulmaq bu ayardan asılı deyil.
/// </summary>
public class GuestLibraryAttribute : TypeFilterAttribute
{
    public GuestLibraryAttribute() : base(typeof(GuestLibraryFilter)) { }
}

public class GuestLibraryFilter : IAsyncAuthorizationFilter
{
    private readonly ISettingsService _settings;
    public GuestLibraryFilter(ISettingsService settings) => _settings = settings;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated == true) return;
        if (await _settings.IsGuestLibraryOpenAsync()) return;
        context.Result = new ChallengeResult();
    }
}
