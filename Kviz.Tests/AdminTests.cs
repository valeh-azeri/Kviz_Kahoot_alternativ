using Kviz.Business.Abstractions;
using Kviz.Business.Models;
using Kviz.Business.Services;
using Kviz.Entities;
using Kviz.Entities.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Kviz.Tests;

public class AdminTests
{
    private static async Task<Guid> MakeAdmin(TestHost h)
    {
        await h.WithScope(async sp =>
        {
            var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            if (!await roles.RoleExistsAsync(AppRoles.Admin)) await roles.CreateAsync(new IdentityRole<Guid>(AppRoles.Admin));
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            await users.AddToRoleAsync((await users.FindByIdAsync(h.UserId.ToString()))!, AppRoles.Admin);
            return 0;
        });
        return h.UserId;
    }

    private static Task<T> Admin<T>(TestHost h, Func<IAdminService, Task<T>> f) => h.WithScope(sp => f(sp.GetRequiredService<IAdminService>()));

    private static async Task<Guid> CreateUser(TestHost h, string email = "ali@kviz.test")
    {
        var r = await Admin(h, a => a.CreateUserAsync("Əli", email, "Parol-123", false));
        Assert.True(r.Success, r.FirstError);
        var list = await Admin(h, a => a.ListUsersAsync(email, null, 1));
        return list.Items.Single().Id;
    }

    [Fact]
    public async Task Create_user_validates_and_rejects_duplicates()
    {
        using var h = new TestHost();
        await MakeAdmin(h);
        Assert.False((await Admin(h, a => a.CreateUserAsync("", "x", "1", false))).Success);
        Assert.True((await Admin(h, a => a.CreateUserAsync("Əli", "ali@kviz.test", "Parol-123", false))).Success);
        var dup = await Admin(h, a => a.CreateUserAsync("Əli 2", "ALI@kviz.test", "Parol-123", false));
        Assert.False(dup.Success);
        Assert.Contains("artıq", dup.FirstError);
    }

    [Fact]
    public async Task Block_unblock_and_protection_rules()
    {
        using var h = new TestHost();
        var admin = await MakeAdmin(h);
        var user = await CreateUser(h);

        Assert.False((await Admin(h, a => a.SetBlockedAsync(admin, admin, true))).Success);     // özünü bloklaya bilməz
        Assert.True((await Admin(h, a => a.SetBlockedAsync(admin, user, true))).Success);
        Assert.True((await Admin(h, a => a.ListUsersAsync(null, "blocked", 1))).Items.Single().IsBlocked);
        Assert.False((await Admin(h, a => a.SetAdminAsync(admin, user, true))).Success);        // bloklu istifadəçi admin olmur
        Assert.True((await Admin(h, a => a.SetBlockedAsync(admin, user, false))).Success);
        Assert.Empty((await Admin(h, a => a.ListUsersAsync(null, "blocked", 1))).Items);
    }

    [Fact]
    public async Task Admin_role_rules_keep_at_least_one_admin()
    {
        using var h = new TestHost();
        var admin = await MakeAdmin(h);
        var user = await CreateUser(h);

        Assert.False((await Admin(h, a => a.SetAdminAsync(admin, admin, false))).Success);      // özünü çıxara bilməz
        Assert.True((await Admin(h, a => a.SetAdminAsync(admin, user, true))).Success);
        Assert.Equal(2, (await Admin(h, a => a.ListUsersAsync(null, "admin", 1))).TotalCount);
        Assert.False((await Admin(h, a => a.SetBlockedAsync(admin, user, true))).Success);      // admini bloklamaq olmaz
        Assert.False((await Admin(h, a => a.DeleteUserAsync(admin, user))).Success);            // admini silmək olmaz
        Assert.True((await Admin(h, a => a.SetAdminAsync(admin, user, false))).Success);
    }

    [Fact]
    public async Task Reset_password_gives_a_working_temp_password()
    {
        using var h = new TestHost();
        var admin = await MakeAdmin(h);
        var user = await CreateUser(h);
        var r = await Admin(h, a => a.ResetPasswordAsync(admin, user));
        Assert.True(r.Success);
        Assert.Equal(10, r.Value!.Length);
        var ok = await h.WithScope(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            return await users.CheckPasswordAsync((await users.FindByIdAsync(user.ToString()))!, r.Value);
        });
        Assert.True(ok);
    }

    [Fact]
    public async Task Locked_quiz_leaves_library_and_cannot_be_republished()
    {
        using var h = new TestHost();
        await MakeAdmin(h);
        var quizId = await h.CreateQuizAsync(TestHost.Mc("S1", 0));
        var quizSvc = (Func<Func<IQuizService, Task<object?>>, Task<object?>>)(f => h.WithScope(sp => f(sp.GetRequiredService<IQuizService>())));
        Assert.True((await h.WithScope(sp => sp.GetRequiredService<IQuizService>().SetVisibilityAsync(quizId, h.UserId, true))).Success);
        Assert.Single((await h.WithScope(sp => sp.GetRequiredService<IQuizService>().SearchPublicAsync(new LibraryQueryDto(), null))).Items);

        Assert.True((await Admin(h, a => a.SetQuizLockedAsync(quizId, true))).Success);
        Assert.Empty((await h.WithScope(sp => sp.GetRequiredService<IQuizService>().SearchPublicAsync(new LibraryQueryDto(), null))).Items);
        var again = await h.WithScope(sp => sp.GetRequiredService<IQuizService>().SetVisibilityAsync(quizId, h.UserId, true));
        Assert.False(again.Success);
        Assert.Contains("administrator", again.FirstError);

        // Redaktordan "hamıya açıq" göndərilsə də gizli qalır
        var edit = (await h.WithScope(sp => sp.GetRequiredService<IQuizService>().GetForEditAsync(quizId, h.UserId)))!;
        Assert.True(edit.AdminLocked);
        edit.IsPublic = true;
        await h.WithScope(sp => sp.GetRequiredService<IQuizService>().SaveAsync(h.UserId, edit));
        Assert.Empty((await h.WithScope(sp => sp.GetRequiredService<IQuizService>().SearchPublicAsync(new LibraryQueryDto(), null))).Items);

        // Admin kvizin məzmununu görə bilir, adi başqa istifadəçi yox
        var stranger = h.NewUser();
        Assert.Null(await h.WithScope(sp => sp.GetRequiredService<IQuizService>().GetDetailsAsync(quizId, stranger)));
        Assert.NotNull(await h.WithScope(sp => sp.GetRequiredService<IQuizService>().GetDetailsAsync(quizId, stranger, isAdmin: true)));

        Assert.True((await Admin(h, a => a.SetQuizLockedAsync(quizId, false))).Success);
        Assert.True((await h.WithScope(sp => sp.GetRequiredService<IQuizService>().SetVisibilityAsync(quizId, h.UserId, true))).Success);
        Assert.Single((await Admin(h, a => a.ListQuizzesAsync("Test", "public", 1))).Items);
    }

    [Fact]
    public async Task Registration_setting_defaults_to_open_and_can_be_closed()
    {
        using var h = new TestHost();
        var s = (Func<Func<ISettingsService, Task<bool>>, Task<bool>>)(f => h.WithScope(sp => f(sp.GetRequiredService<ISettingsService>())));
        Assert.True(await s(x => x.IsRegistrationOpenAsync()));
        await h.WithScope(async sp => { await sp.GetRequiredService<ISettingsService>().SetRegistrationOpenAsync(false); return 0; });
        Assert.False(await s(x => x.IsRegistrationOpenAsync()));
        await h.WithScope(async sp => { await sp.GetRequiredService<ISettingsService>().SetRegistrationOpenAsync(true); return 0; });
        Assert.True(await s(x => x.IsRegistrationOpenAsync()));
    }

    [Fact]
    public async Task Guest_library_defaults_to_open_and_can_be_closed()
    {
        using var h = new TestHost();
        Task<bool> Open() => h.WithScope(sp => sp.GetRequiredService<ISettingsService>().IsGuestLibraryOpenAsync());
        Assert.True(await Open());
        await h.WithScope(async sp => { await sp.GetRequiredService<ISettingsService>().SetGuestLibraryOpenAsync(false); return 0; });
        Assert.False(await Open());
        // Qeydiyyat ayarı bundan asılı deyil
        Assert.True(await h.WithScope(sp => sp.GetRequiredService<ISettingsService>().IsRegistrationOpenAsync()));
        await h.WithScope(async sp => { await sp.GetRequiredService<ISettingsService>().SetGuestLibraryOpenAsync(true); return 0; });
        Assert.True(await Open());
    }

    [Fact]
    public async Task Categories_can_be_added_renamed_ordered_and_deleted()
    {
        using var h = new TestHost();
        await MakeAdmin(h);
        // Test bazasında hazır kateqoriyalar yoxdur (onlar yalnız miqrasiya ilə gəlir), ona görə iki dənə yaradırıq
        Assert.True((await Admin(h, a => a.SaveCategoryAsync(null, "Riyaziyyat", "➗"))).Success);
        Assert.True((await Admin(h, a => a.SaveCategoryAsync(null, "Tarix", "🏛️"))).Success);
        var start = await Admin(h, a => a.ListCategoriesAsync());
        Assert.Equal(new[] { "Riyaziyyat", "Tarix" }, start.Select(c => c.Name));

        // Yoxlamalar
        Assert.False((await Admin(h, a => a.SaveCategoryAsync(null, " ", "🎯"))).Success);
        Assert.False((await Admin(h, a => a.SaveCategoryAsync(null, start[0].Name.ToUpperInvariant(), "🎯"))).Success); // təkrar ad

        // Əlavə: sonda görünür, boş nişan 🎯 olur
        Assert.True((await Admin(h, a => a.SaveCategoryAsync(null, "  Kino   və   serial ", ""))).Success);
        var list = await Admin(h, a => a.ListCategoriesAsync());
        var added = list.Last();
        Assert.Equal("Kino və serial", added.Name);
        Assert.Equal("🎯", added.Icon);

        // Dəyişmə (öz adını saxlamaq xəta deyil)
        Assert.True((await Admin(h, a => a.SaveCategoryAsync(added.Id, "Kino və serial", "🎬"))).Success);
        Assert.Equal("🎬", (await Admin(h, a => a.ListCategoriesAsync())).Last().Icon);

        // Sıra: yuxarı çıxar
        var before = (await Admin(h, a => a.ListCategoriesAsync())).Select(c => c.Id).ToList();
        Assert.True((await Admin(h, a => a.MoveCategoryAsync(added.Id, true))).Success);
        var after = (await Admin(h, a => a.ListCategoriesAsync())).Select(c => c.Id).ToList();
        Assert.Equal(added.Id, after[^2]);
        Assert.Equal(before[^2], after[^1]);

        // Silmə: kviz silinmir, kateqoriyasız qalır
        var saved = await h.WithScope(sp => sp.GetRequiredService<IQuizService>().SaveAsync(h.UserId, new QuizEditDto
        {
            Title = "Kateqoriyalı kviz", CategoryId = added.Id, Questions = { TestHost.Mc("S1", 0) }
        }));
        Assert.True(saved.Success, saved.FirstError);
        var quizId = saved.Value;
        Assert.Equal(1, (await Admin(h, a => a.ListCategoriesAsync())).Single(c => c.Id == added.Id).QuizCount);
        Assert.True((await Admin(h, a => a.DeleteCategoryAsync(added.Id))).Success);
        Assert.DoesNotContain(await Admin(h, a => a.ListCategoriesAsync()), c => c.Id == added.Id);
        var details = await h.WithScope(sp => sp.GetRequiredService<IQuizService>().GetDetailsAsync(quizId, h.UserId));
        Assert.NotNull(details);
        Assert.Null(details!.Card.CategoryId);
        Assert.False((await Admin(h, a => a.DeleteCategoryAsync(added.Id))).Success);
    }

    [Fact]
    public async Task Dashboard_counts()
    {
        using var h = new TestHost();
        await MakeAdmin(h);
        await CreateUser(h);
        var quiz = await h.CreateQuizAsync(TestHost.Mc("S1", 0));
        await h.CreateGameAsync(quiz);
        var d = await Admin(h, a => a.GetDashboardAsync());
        Assert.Equal(2, d.Users);
        Assert.Equal(1, d.Admins);
        Assert.Equal(1, d.Quizzes);
        Assert.Equal(1, d.Games);
        Assert.Equal(1, d.ActiveGames);
        Assert.True(d.RegistrationOpen);
        Assert.Single(d.RecentGames);
    }
}
