using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Kviz.Business;
using Kviz.Business.Abstractions;
using Kviz.DataAccess;
using Kviz.Entities;
using Kviz.Web.Hubs;
using Kviz.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddDataAccess(config);
builder.Services.AddBusiness(config);

// İnternetdə (hosting) tətbiq yenidən başlayanda giriş cookie-ləri etibarsız olmasın deyə açarlar qovluqda saxlanıla bilər.
var keysPath = config["Kviz:DataProtectionPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
    builder.Services.AddDataProtection().SetApplicationName("Kvizo").PersistKeysToFileSystem(new DirectoryInfo(keysPath));

builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(o =>
    {
        o.Password.RequiredLength = 6;
        o.Password.RequireDigit = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail = true;
        o.Lockout.MaxFailedAccessAttempts = 8;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddEntityFrameworkStores<KvizDbContext>()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

// Bloklanan istifadəçi və ya dəyişən rol 1 dəqiqə ərzində açıq sessiyalarda da qüvvəyə minir
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/Login";
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    o.SlidingExpiration = true;
    o.Cookie.Name = "kviz.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    // HTTP (lokal şəbəkə) və HTTPS (internet) üçün eyni proqram: cookie sorğunun sxeminə uyğun qurulur.
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    // Ajax/SignalR sorğularında yönləndirmə əvəzinə 401 qaytar
    o.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/gamehub") || ctx.Request.Headers.XRequestedWith == "XMLHttpRequest" ||
            ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Headers.Accept.ToString().Contains("application/json"))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
});

builder.Services.AddControllersWithViews(o => o.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()))
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");

builder.Services.AddSingleton<IGameNotifier, SignalRGameNotifier>();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // PIN-i təxmin etməyə qarşı sadə məhdudiyyət (IP başına dəqiqədə 40 cəhd)
    o.AddPolicy("join", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 40, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("images", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
    // Avatar şəkilləri: sinif eyni IP-nin (NAT) arxasında ola bilər, ona görə limit genişdir
    o.AddPolicy("avatars", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

if (config.GetValue<bool>("Kviz:UseForwardedHeaders"))
{
    var fh = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost };
    fh.KnownIPNetworks.Clear();
    fh.KnownProxies.Clear();
    app.UseForwardedHeaders(fh);
}

if (config.GetValue<bool>("Kviz:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<KvizDbContext>();
    try { await db.Database.MigrateAsync(); }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "Baza yaradıla/yenilənə bilmədi. ConnectionStrings:Default ayarını və SQL Server-in işlədiyini yoxlayın.");
        throw;
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
if (config.GetValue<bool>("Kviz:RedirectToHttps")) app.UseHttpsRedirection();

app.UseStatusCodePagesWithReExecute("/Home/StatusCode", "?code={0}");
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHub<GameHub>("/gamehub");
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

// Başlayanda oyunçuların qoşulacağı ünvanları konsola yazır (lokal şəbəkədə işləyərkən faydalıdır).
app.Lifetime.ApplicationStarted.Register(() =>
{
    var publicUrl = config["Kviz:PublicBaseUrl"];
    try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* konsol dəstəkləmirsə keç */ }

    // Baslat.bat "Kviz__OpenBrowser=true" ilə işlədir: sayt hazır olan kimi brauzer özü açılır.
    // (Server artıq qulaq asdığı üçün səhifə dərhal açılır, gözləmə dövrəsi lazım deyil.)
    if (config.GetValue<bool>("Kviz:OpenBrowser"))
    {
        try
        {
            var first = app.Urls.FirstOrDefault() ?? "http://localhost:5080";
            var openUrl = new Uri(first.Replace("0.0.0.0", "localhost").Replace("[::]", "localhost").Replace("+", "localhost").Replace("*", "localhost")).GetLeftPart(UriPartial.Authority);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(openUrl) { UseShellExecute = true });
        }
        catch (Exception ex) { Console.WriteLine($"  Brauzer avtomatik açılmadı ({ex.Message}). Yuxarıdakı ünvanı brauzerdə əl ilə açın."); }
    }
    Console.WriteLine();
    Console.WriteLine("  Kvizo işləyir.");
    if (!string.IsNullOrWhiteSpace(publicUrl))
    {
        Console.WriteLine($"  İctimai ünvan: {publicUrl}");
        return;
    }
    foreach (var address in app.Urls)
    {
        var uri = new Uri(address.Replace("0.0.0.0", "localhost").Replace("[::]", "localhost").Replace("+", "localhost").Replace("*", "localhost"));
        Console.WriteLine($"  Bu kompüterdə:        http://localhost:{uri.Port}");
        foreach (var ip in Kviz.Business.Services.JoinLinkService.GetLanAddresses())
            Console.WriteLine($"  Şəbəkədəki telefonlar: http://{ip}:{uri.Port}");
    }
    Console.WriteLine("  Telefonlar eyni Wi-Fi-a (və ya bu kompüterin hotspot-una) qoşulmalıdır.");
    Console.WriteLine();
});

app.Run();

public partial class Program { }
