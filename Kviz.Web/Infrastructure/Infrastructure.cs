using System.Security.Claims;
using Kviz.Business.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace Kviz.Web.Infrastructure;

public static class UserExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    public static Guid? GetUserIdOrNull(this ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

public class SignalRGameNotifier : IGameNotifier
{
    private readonly IHubContext<Hubs.GameHub> _hub;
    public SignalRGameNotifier(IHubContext<Hubs.GameHub> hub) => _hub = hub;

    public Task ToHostAsync(Guid gameId, string eventName, object payload) =>
        _hub.Clients.Group(Hubs.GameHub.HostGroup(gameId)).SendAsync(eventName, payload);

    public Task ToPlayerAsync(Guid playerId, string eventName, object payload) =>
        _hub.Clients.Group(Hubs.GameHub.PlayerGroup(playerId)).SendAsync(eventName, payload);
}

public class LocalFileStorage : IFileStorage
{
    private readonly IWebHostEnvironment _env;
    public LocalFileStorage(IWebHostEnvironment env) => _env = env;

    public async Task<string> SaveImageAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var relDir = Path.Combine("uploads", now.ToString("yyyy"), now.ToString("MM"));
        var absDir = Path.Combine(_env.WebRootPath, relDir);
        Directory.CreateDirectory(absDir);
        var name = Guid.NewGuid().ToString("N") + extension;
        await using (var fs = File.Create(Path.Combine(absDir, name)))
            await content.CopyToAsync(fs, ct);
        return "/" + relDir.Replace('\\', '/') + "/" + name;
    }

    public async Task<string> SaveAvatarAsync(Stream content, CancellationToken ct = default)
    {
        var dir = Path.Combine(_env.WebRootPath, "uploads", "avatars");
        Directory.CreateDirectory(dir);
        var name = Guid.NewGuid().ToString("N") + ".jpg";
        await using (var fs = File.Create(Path.Combine(dir, name)))
            await content.CopyToAsync(fs, ct);
        return "/uploads/avatars/" + name;
    }

    public void DeleteAvatar(string url)
    {
        // Yalnız özümüzün yaratdığı ad formatı silinir (yol manipulyasiyasına qarşı)
        if (!Kviz.Business.Games.AvatarCatalog.IsPhoto(url)) return;
        var path = Path.Combine(_env.WebRootPath, "uploads", "avatars", Path.GetFileName(url));
        if (File.Exists(path)) File.Delete(path);
    }
}
