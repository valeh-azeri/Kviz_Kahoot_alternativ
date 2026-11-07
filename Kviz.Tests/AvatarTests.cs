using Kviz.Business.Common;
using Kviz.Business.Games;
using Kviz.Business.Models;
using Kviz.Business.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kviz.Tests;

public class AvatarTests
{
    /// <summary>Yalnız başlığı olan minimal JPEG (SOF0 ilə ölçü). Serverin yoxlaması üçün kifayətdir.</summary>
    private static byte[] Jpeg(int w, int h, int extra = 0)
    {
        var b = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,          // SOI + APP0
            0xFF, 0xC0, 0x00, 0x0B, 0x08, (byte)(h >> 8), (byte)h, (byte)(w >> 8), (byte)w, 0x01, 0x01, 0x11, 0x00, // SOF0
            0xFF, 0xD9 };
        b.AddRange(new byte[extra]);
        return b.ToArray();
    }

    private static async Task<(Guid Id, Guid Token)> Join(TestHost h, Guid gameId, string pin, string name, string? avatar = null)
    {
        var r = await h.Engine.JoinAsync(pin, name, null, avatar);
        Assert.True(r.Success, r.Error);
        return (r.PlayerId, r.Token);
    }

    [Fact]
    public async Task Join_uses_chosen_preset_or_assigns_a_random_one_and_rejects_made_up_values()
    {
        using var h = new TestHost();
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0));
        var game = await h.CreateGameAsync(quiz);

        await Join(h, game.GameId, game.Pin, "Aysel", "e:🐱");
        await Join(h, game.GameId, game.Pin, "Rəşad");                                  // seçməyib
        await Join(h, game.GameId, game.Pin, "Nigar", "e:<script>");                    // uydurma
        await Join(h, game.GameId, game.Pin, "Tural", "/uploads/avatars/" + new string('a', 32) + ".jpg"); // şəkil yalnız yükləmə ilə

        var players = (await h.Engine.GetHostStateAsync(game.GameId, h.UserId))!.Players.ToDictionary(p => p.Nickname);
        Assert.Equal("e:🐱", players["Aysel"].Avatar);
        Assert.True(AvatarCatalog.IsPreset(players["Rəşad"].Avatar));
        Assert.True(AvatarCatalog.IsPreset(players["Nigar"].Avatar));
        Assert.True(AvatarCatalog.IsPreset(players["Tural"].Avatar));
    }

    [Fact]
    public void Catalog_validates_formats_strictly()
    {
        Assert.True(AvatarCatalog.IsPreset("e:🦊"));
        Assert.False(AvatarCatalog.IsPreset("e:💩x"));
        Assert.False(AvatarCatalog.IsPreset("🦊"));
        Assert.False(AvatarCatalog.IsPreset(null));
        Assert.True(AvatarCatalog.IsPhoto("/uploads/avatars/" + new string('0', 32) + ".jpg"));
        Assert.False(AvatarCatalog.IsPhoto("/uploads/avatars/../../appsettings.json"));
        Assert.False(AvatarCatalog.IsPhoto("/uploads/avatars/" + new string('0', 32) + ".png"));
        Assert.False(AvatarCatalog.IsPhoto("https://evil.example/a.jpg"));
        Assert.True(AvatarCatalog.Groups.SelectMany(g => g.Items).Distinct().Count() >= 36);
    }

    [Fact]
    public async Task Player_can_change_preset_only_in_lobby_and_host_list_updates()
    {
        using var h = new TestHost(introMs: 5);
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0));
        var game = await h.CreateGameAsync(quiz);
        var a = await Join(h, game.GameId, game.Pin, "Aysel", "e:🐱");

        Assert.True((await h.Engine.SetPresetAvatarAsync(game.GameId, a.Id, "e:🐼")).Ok);
        Assert.False((await h.Engine.SetPresetAvatarAsync(game.GameId, a.Id, "e:nope")).Ok);
        Assert.Equal("e:🐼", h.Notifier.LastState<PlayerStateDto>("player", a.Id)!.You.Avatar);
        Assert.Contains(h.Notifier.Events, e => e.Target == "host" && e.Name == "playerJoined" && ((LobbyPlayerDto)e.Payload).Avatar == "e:🐼");

        await h.Engine.StartAsync(game.GameId, h.UserId);
        Assert.False((await h.Engine.SetPresetAvatarAsync(game.GameId, a.Id, "e:🦊")).Ok); // oyun başladı
    }

    [Fact]
    public async Task Photo_upload_is_checked_stored_and_replaced_and_old_file_is_deleted()
    {
        using var h = new TestHost();
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0));
        var game = await h.CreateGameAsync(quiz);
        var a = await Join(h, game.GameId, game.Pin, "Aysel");
        var b = await Join(h, game.GameId, game.Pin, "Rəşad");
        var storage = (FakeFileStorage)h.Provider.GetRequiredService<Kviz.Business.Abstractions.IFileStorage>();

        async Task<ServiceResult<string>> Save(byte[] bytes) => await h.WithScope(sp =>
            sp.GetRequiredService<IAvatarService>().SavePhotoAsync(new MemoryStream(bytes), bytes.Length));

        // Yoxlamalar
        Assert.False((await Save(new byte[] { 1, 2, 3, 4, 5 })).Success);                    // JPEG deyil
        Assert.False((await Save(Jpeg(2000, 2000))).Success);                                // çox böyük ölçü
        Assert.False((await Save(Jpeg(160, 160, extra: AvatarService.MaxBytes))).Success);   // çox ağır fayl
        Assert.Empty(storage.Avatars);

        // Uğurlu yükləmə
        var first = await Save(Jpeg(160, 160, 3000));
        Assert.True(first.Success, first.FirstError);
        Assert.StartsWith("/uploads/avatars/", first.Value);
        Assert.False((await h.Engine.SetPhotoAvatarAsync(game.GameId, a.Id, Guid.NewGuid(), first.Value!)).Ok);   // yanlış açar
        Assert.False((await h.Engine.SetPhotoAvatarAsync(game.GameId, a.Id, b.Token, first.Value!)).Ok);         // başqasının açarı
        Assert.True((await h.Engine.SetPhotoAvatarAsync(game.GameId, a.Id, a.Token, first.Value!)).Ok);
        Assert.Equal(first.Value, h.Notifier.LastState<PlayerStateDto>("player", a.Id)!.You.Avatar);

        // Dəyişmə: köhnə şəkil faylı silinir
        var second = await Save(Jpeg(160, 160, 3000));
        Assert.True((await h.Engine.SetPhotoAvatarAsync(game.GameId, a.Id, a.Token, second.Value!)).Ok);
        Assert.Contains(first.Value!, storage.DeletedAvatars);
        // Hazır avatara qayıdanda da şəkil silinir
        Assert.True((await h.Engine.SetPresetAvatarAsync(game.GameId, a.Id, "e:🐸")).Ok);
        Assert.Contains(second.Value!, storage.DeletedAvatars);

        // Uydurma ünvan (məsələn başqa fayl) təyin oluna bilməz
        Assert.False((await h.Engine.SetPhotoAvatarAsync(game.GameId, a.Id, a.Token, "/uploads/avatars/../x.jpg")).Ok);
    }

    [Fact]
    public async Task Host_can_forbid_photo_avatars()
    {
        using var h = new TestHost();
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0));
        var closed = await h.CreateGameAsync(quiz, tweak: d => d.AllowPhotoAvatars = false);
        var c = await Join(h, closed.GameId, closed.Pin, "Aysel");
        var denied = await h.Engine.CanUploadPhotoAsync(closed.GameId, c.Id, c.Token);
        Assert.False(denied.Ok);
        Assert.Contains("bağlayıb", denied.Error);
        Assert.False((await h.Engine.RejoinAsync(closed.GameId, c.Token)).State!.AllowPhotoAvatars);
        Assert.False((await h.WithScope(sp => sp.GetRequiredService<Kviz.Business.Abstractions.IGameService>().ResolvePinAsync(closed.Pin))).AllowPhotoAvatars);

        // İcazə verilən oyunda şəkil qəbul olunur (hazır avatar seçimi isə hər iki halda işləyir)
        var open = await h.CreateGameAsync(quiz);
        var o = await Join(h, open.GameId, open.Pin, "Rəşad");
        Assert.True((await h.Engine.CanUploadPhotoAsync(open.GameId, o.Id, o.Token)).Ok);
        Assert.True((await h.Engine.SetPresetAvatarAsync(closed.GameId, c.Id, "e:🐙")).Ok);
        // (Oyunun silinməsi zamanı fayl təmizliyi ExecuteDelete işlədir, InMemory bazada test olunmur: real SQL Server-də yoxlanılır.)
    }

    [Fact]
    public async Task Leaderboard_carries_avatars()
    {
        using var h = new TestHost(introMs: 5);
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0), TestHost.Mc("S2", 0));
        var game = await h.CreateGameAsync(quiz);
        var a = await Join(h, game.GameId, game.Pin, "Aysel", "e:🦁");
        await h.Engine.StartAsync(game.GameId, h.UserId);
        await h.WaitHostPhase(game.GameId, "question");
        await h.Engine.SubmitAnswerAsync(game.GameId, a.Id, new AnswerSubmission { Choices = new[] { 0 } });
        await h.WaitHostPhase(game.GameId, "results");
        await h.Engine.AdvanceAsync(game.GameId, h.UserId);
        var lb = await h.WaitHostPhase(game.GameId, "leaderboard");
        Assert.Equal("e:🦁", lb.Leaderboard[0].Avatar);
        Assert.Equal("e:🦁", h.Notifier.LastState<PlayerStateDto>("player", a.Id)!.Leaderboard[0].Avatar);
    }
}
