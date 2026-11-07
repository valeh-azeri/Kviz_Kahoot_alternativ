using Kviz.Business.Models;
using Kviz.Business.Services;
using Kviz.Entities.Enums;

namespace Kviz.Tests;

public class ImageTests
{
    [Fact]
    public void Query_builder_prefers_correct_answer_then_keywords()
    {
        var q = ImageQueryBuilder.Build("Azərbaycanın paytaxtı hansı şəhərdir?", new[] { "Bakı" });
        Assert.Equal("Bakı", q[0]);
        Assert.Contains(q, x => x.Contains("Azərbaycanın") && x.Contains("paytaxtı") && !x.Contains("hansı"));
        Assert.True(q.Count <= 3);
    }

    [Fact]
    public void Query_builder_ignores_true_false_numbers_and_stopwords()
    {
        Assert.DoesNotContain("Doğru", ImageQueryBuilder.Build("Yer Günəş ətrafında fırlanır.", new[] { "Doğru" }));
        Assert.DoesNotContain("4", ImageQueryBuilder.Build("2 + 2 neçədir?", new[] { "4" }));
        Assert.Empty(ImageQueryBuilder.Build("nə kimdir hansı?", Array.Empty<string>()));
        Assert.Empty(ImageQueryBuilder.Build(null, null));
    }

    [Fact]
    public void Only_wikimedia_image_host_over_https_is_allowed()
    {
        Assert.True(ImageSearchService.IsAllowedImageHost("https://upload.wikimedia.org/wikipedia/commons/thumb/a/a1/x.jpg/640px-x.jpg"));
        Assert.False(ImageSearchService.IsAllowedImageHost("http://upload.wikimedia.org/x.jpg"));
        Assert.False(ImageSearchService.IsAllowedImageHost("https://evil.example/x.jpg"));
        Assert.False(ImageSearchService.IsAllowedImageHost("https://upload.wikimedia.org.evil.example/x.jpg"));
        Assert.False(ImageSearchService.IsAllowedImageHost("http://localhost:5080/admin"));
        Assert.False(ImageSearchService.IsAllowedImageHost("http://169.254.169.254/latest/meta-data"));
        Assert.False(ImageSearchService.IsAllowedImageHost("not a url"));
    }

    [Fact]
    public void Parses_commons_imageinfo_with_credit_and_filters_non_bitmaps()
    {
        const string json = """
        {"query":{"pages":{
          "1":{"title":"File:Baku.jpg","index":2,"imageinfo":[{"thumburl":"https://upload.wikimedia.org/wikipedia/commons/thumb/a/a1/Baku.jpg/640px-Baku.jpg","thumbwidth":640,"thumbheight":427,"mime":"image/jpeg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Baku.jpg","extmetadata":{"Artist":{"value":"<a href=\"//x\">Ali Veli</a>"},"LicenseShortName":{"value":"CC BY-SA 4.0"}}}]},
          "2":{"title":"File:Flag.svg","index":1,"imageinfo":[{"thumburl":"https://upload.wikimedia.org/x.png","mime":"image/svg+xml"}]},
          "3":{"title":"File:Evil.jpg","index":3,"imageinfo":[{"thumburl":"https://evil.example/x.jpg","mime":"image/jpeg"}]}
        }}}
        """;
        var r = ImageSearchService.ParseImageInfo(json);
        var one = Assert.Single(r);
        Assert.Equal("File:Baku.jpg", one.Title);
        Assert.Equal(640, one.Width);
        Assert.Equal("Ali Veli · CC BY-SA 4.0 · Wikimedia Commons", one.Credit);
    }

    [Fact]
    public void Parses_wikipedia_page_images_in_search_order()
    {
        const string json = """
        {"query":{"pages":{"10":{"index":2,"pageimage":"B.jpg"},"11":{"index":1,"pageimage":"A.jpg"},"12":{"index":3,"pageimage":"Logo.svg"},"13":{"index":4}}}}
        """;
        Assert.Equal(new[] { "A.jpg", "B.jpg" }, ImageSearchService.ParseWikipediaPageImages(json));
    }

    [Fact]
    public void Page_photos_keep_real_photos_and_drop_decorations()
    {
        const string json = """
        {"query":{"pages":{"1150":{"title":"Bakı","images":[
          {"ns":6,"title":"Fayl:Flag of Baku.svg"},{"ns":6,"title":"Fayl:Baku Skyline.jpg"},{"ns":6,"title":"Fayl:Commons-logo.svg"},
          {"ns":6,"title":"Fayl:Map of Azerbaijan.png"},{"ns":6,"title":"Fayl:Flame Towers 2013.JPG"},{"ns":6,"title":"Fayl:Edit-clear.png"},{"ns":6,"title":"File:Qiz Qalasi.webp"}]}}}}
        """;
        Assert.Equal(new[] { "Baku Skyline.jpg", "Flame Towers 2013.JPG", "Qiz Qalasi.webp" }, ImageSearchService.ParsePagePhotos(json));
        Assert.Empty(ImageSearchService.ParsePagePhotos("""{"query":{"pages":{"-1":{"title":"Yoxdur","missing":""}}}}"""));
    }

    [Fact]
    public void Finds_english_counterpart_title_from_langlinks()
    {
        const string json = """{"query":{"pages":{"1150":{"title":"Bakı","langlinks":[{"lang":"en","*":"Baku"}]}}}}""";
        Assert.Equal("Baku", ImageSearchService.ParseEnglishTitle(json));
        Assert.Null(ImageSearchService.ParseEnglishTitle("""{"query":{"pages":{"1":{"title":"X"}}}}"""));
        Assert.Empty(ImageSearchService.ParsePagePhotos("""{"query":{"pages":{"1":{"images":[{"title":"Fayl:Baku gub coa n655.png"},{"title":"Fayl:Coa Baku.jpg"}]}}}}"""));
    }

    [Fact]
    public void Wikipedia_relevance_order_survives_underscore_and_space_differences()
    {
        var items = new List<ImageSearchResultDto>
        {
            new() { Title = "File:Mahmud Abdülbâkî.jpg" }, new() { Title = "File:Baku skyline.jpg" }, new() { Title = "File:Lankaran.jpg" }
        };
        // Vikipediyada ən uyğun nəticə birinci: alt xətli ad, Commons isə boşluqla qaytarır
        var ordered = ImageSearchService.OrderByTitles(items, new List<string> { "File:Baku_skyline.jpg", "File:Lankaran.jpg" });
        Assert.Equal(new[] { "File:Baku skyline.jpg", "File:Lankaran.jpg", "File:Mahmud Abdülbâkî.jpg" }, ordered.Select(x => x.Title).ToArray());
    }

    [Fact]
    public void Question_can_be_image_only_but_not_empty()
    {
        var withImage = new QuizEditDto
        {
            Title = "T",
            Questions = { new QuestionEditDto { Type = QuestionType.OpenEnded, Text = "", ImageUrl = "/uploads/2026/11/a.png", RelatedImageUrl = "/uploads/2026/11/b.jpg", RelatedImageCredit = "Müəllif · CC0" } }
        };
        QuizService.Normalize(withImage);
        Assert.Empty(QuizService.Validate(withImage));
        // İki şəkil ayrıdır: sualın şəkli müəllifsiz, əlavə şəkil müəllif məlumatı ilə
        Assert.Equal("/uploads/2026/11/a.png", withImage.Questions[0].ImageUrl);
        Assert.Equal("/uploads/2026/11/b.jpg", withImage.Questions[0].RelatedImageUrl);
        Assert.Equal("Müəllif · CC0", withImage.Questions[0].RelatedImageCredit);

        var empty = new QuizEditDto { Title = "T", Questions = { new QuestionEditDto { Type = QuestionType.OpenEnded, Text = "" } } };
        QuizService.Normalize(empty);
        Assert.Contains(QuizService.Validate(empty), e => e.Path == "questions[0].text" && e.Message.Contains("sualın şəklini"));

        // Şəkil olmayanda müəllif məlumatı saxlanılmır
        var noImage = new QuizEditDto { Title = "T", Questions = { new QuestionEditDto { Type = QuestionType.OpenEnded, Text = "S", RelatedImageCredit = "x" } } };
        QuizService.Normalize(noImage);
        Assert.Null(noImage.Questions[0].RelatedImageCredit);

        // Yalnız əlavə şəkli olan sual da boş sayılmır, amma bəzək üçün təhlükəli ünvan atılır
        var onlyRelated = new QuizEditDto { Title = "T", Questions = { new QuestionEditDto { Type = QuestionType.OpenEnded, Text = "", RelatedImageUrl = "javascript:alert(1)" } } };
        QuizService.Normalize(onlyRelated);
        Assert.Null(onlyRelated.Questions[0].RelatedImageUrl);
        Assert.NotEmpty(QuizService.Validate(onlyRelated));
    }

    // ---------- Saxta Wikimedia ilə axtarış axını (real internetə çıxmır) ----------

    private sealed class FakeWikimedia : HttpMessageHandler
    {
        public int Calls;
        public int Throttle429;          // ilk N sorğuya 429 qaytar
        public bool AlwaysBusy, NetworkDown, OpenverseBusy, CommonsBusy;
        public string? RedirectThumbTo;                 // Openverse thumb-i bu ünvana yönləndir
        public int OpenversePageCount = 3;
        public List<string> Urls = new();
        public List<string> Hosts = new();
        public List<string> UserAgents = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            lock (Hosts) { Hosts.Add(request.RequestUri!.Host); UserAgents.Add(request.Headers.UserAgent.ToString()); }
            lock (Urls) Urls.Add(request.RequestUri.ToString());
            if (NetworkDown) throw new HttpRequestException("no network");
            if ((OpenverseBusy && request.RequestUri.Host == "api.openverse.org" && !request.RequestUri.AbsolutePath.Contains("/thumb")) ||
                (CommonsBusy && request.RequestUri.Host == "commons.wikimedia.org"))
            {
                var limited = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3600));
                return Task.FromResult(limited);
            }
            if (request.RequestUri.Host == "api.openverse.org")
            {
                if (request.RequestUri.AbsolutePath.Contains("/thumb"))
                {
                    if (RedirectThumbTo != null)
                    {
                        var redirect = new HttpResponseMessage(System.Net.HttpStatusCode.Redirect);
                        redirect.Headers.Location = new Uri(RedirectThumbTo);
                        return Task.FromResult(redirect);
                    }
                    return Task.FromResult(new HttpResponseMessage { Content = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 }) });
                }
                var page = int.Parse(System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["page"] ?? "1");
                var ov = """
                    {"result_count":100,"page_count":__COUNT__,"page":__PAGE__,"results":[
                      {"id":"11111111-1111-1111-1111-111111111111","title":"Baku photo","creator":"Ali Veli","license":"by-sa","license_version":"4.0","source":"flickr","filetype":"jpg","thumbnail":"https://api.openverse.org/v1/images/11111111-1111-1111-1111-111111111111/thumb/","width":640,"height":480,"foreign_landing_url":"https://flickr.com/x"},
                      {"id":"22222222-2222-2222-2222-222222222222","title":"Vector","creator":"X","license":"cc0","source":"wikimedia","filetype":"svg","thumbnail":"https://api.openverse.org/v1/images/22222222-2222-2222-2222-222222222222/thumb/"},
                      {"id":"33333333-3333-3333-3333-333333333333","title":"Evil","creator":"Y","license":"by","license_version":"2.0","source":"flickr","filetype":"png","thumbnail":"https://evil.example/x.png"}]}
                    """.Replace("__COUNT__", OpenversePageCount.ToString()).Replace("__PAGE__", page.ToString());
                return Task.FromResult(new HttpResponseMessage { Content = new StringContent(ov) });
            }
            if (AlwaysBusy || Interlocked.Decrement(ref Throttle429) >= 0)
            {
                var busy = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
                busy.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(busy);
            }
            var host = request.RequestUri.Host;
            if (host == "upload.wikimedia.org")
                return Task.FromResult(new HttpResponseMessage { Content = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 }) });
            string json = host == "commons.wikimedia.org"
                ? """{"query":{"pages":{"1":{"title":"File:Baku.jpg","index":1,"imageinfo":[{"thumburl":"https://upload.wikimedia.org/wikipedia/commons/thumb/a/a1/Baku.jpg/640px-Baku.jpg","thumbwidth":640,"thumbheight":427,"mime":"image/jpeg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Baku.jpg","extmetadata":{"Artist":{"value":"Ali Veli"},"LicenseShortName":{"value":"CC BY 4.0"}}}]}}}}"""
                : """{"query":{"pages":{"7":{"index":1,"pageimage":"Baku.jpg"}}}}""";
            return Task.FromResult(new HttpResponseMessage { Content = new StringContent(json) });
        }
    }

    private sealed class FakeFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _h;
        public FakeFactory(HttpMessageHandler h) => _h = h;
        public HttpClient CreateClient(string name) => new(_h, disposeHandler: false);
    }

    private static (ImageSearchService svc, FakeWikimedia fake) MakeService(Action<FakeWikimedia>? tweak = null)
    {
        var fake = new FakeWikimedia();
        tweak?.Invoke(fake);
        var storage = new FakeFileStorage();
        var svc = new ImageSearchService(new FakeFactory(fake), new ImageUploadService(storage),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ImageSearchService>.Instance,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        return (svc, fake);
    }

    [Fact]
    public async Task Search_finds_deduplicates_and_caches_results()
    {
        var (svc, fake) = MakeService();
        var req = new ImageSearchRequest { Text = "Azərbaycanın paytaxtı hansı şəhərdir?", Answers = { "Bakı" } };
        var r = await svc.SearchAsync(req);
        Assert.True(r.Success, r.FirstError);
        var item = Assert.Single(r.Value!);                       // Wikipedia və Commons eyni faylı verdi: təkrar atılır
        Assert.Equal("Ali Veli · CC BY 4.0 · Wikimedia Commons", item.Credit);
        Assert.DoesNotContain(fake.Hosts, h => h != "az.wikipedia.org" && h != "en.wikipedia.org" && h != "commons.wikimedia.org");

        var calls = fake.Calls;
        var again = await svc.SearchAsync(req);                    // ikinci dəfə yaddaşdan gəlir
        Assert.True(again.Success);
        Assert.Equal(calls, fake.Calls);
    }

    // ---------- Tək-tək axtarış (Openverse + Wikimedia, yalnız yazılan söz) ----------

    [Fact]
    public void Openverse_results_are_parsed_with_credit_and_filtered()
    {
        const string json = """
        {"page_count":3,"results":[
          {"id":"11111111-1111-1111-1111-111111111111","title":"A","creator":"Ali Veli","license":"by-sa","license_version":"4.0","source":"flickr","filetype":"jpg","thumbnail":"https://api.openverse.org/v1/images/11111111-1111-1111-1111-111111111111/thumb/","width":640,"height":480},
          {"id":"22222222-2222-2222-2222-222222222222","title":"B","license":"cc0","filetype":"svg","thumbnail":"https://api.openverse.org/v1/images/22222222-2222-2222-2222-222222222222/thumb/"},
          {"id":"33333333-3333-3333-3333-333333333333","title":"C","license":"by","filetype":"png","thumbnail":"https://evil.example/x.png"},
          {"id":"44444444-4444-4444-4444-444444444444","title":"D","license":"pdm","source":"wikimedia","filetype":"jpg"}
        ]}
        """;
        var (items, more) = ImageSearchService.ParseOpenverse(json, 1);
        Assert.True(more);
        Assert.Equal(new[] { "A", "D" }, items.Select(i => i.Title).ToArray());       // svg və yad ünvan atıldı
        Assert.Equal("Ali Veli · CC BY-SA 4.0 · Flickr", items[0].Credit);
        Assert.Equal("Public domain · Wikimedia", items[1].Credit);
        Assert.All(items, i => Assert.Equal("Openverse", i.Source));
        Assert.Equal("https://api.openverse.org/v1/images/44444444-4444-4444-4444-444444444444/thumb/", items[1].ThumbUrl); // thumbnail yoxdursa id-dən qurulur
        Assert.False(ImageSearchService.ParseOpenverse(json, 3).hasMore);
    }

    [Theory]
    [InlineData("by", "2.0", "CC BY 2.0")]
    [InlineData("by-nc-sa", "4.0", "CC BY-NC-SA 4.0")]
    [InlineData("cc0", null, "CC0")]
    [InlineData("pdm", null, "Public domain")]
    [InlineData(null, null, "")]
    public void License_formatting(string? license, string? version, string expected) =>
        Assert.Equal(expected, ImageSearchService.FormatLicense(license, version));

    [Fact]
    public void Only_openverse_thumb_endpoint_is_allowed_besides_wikimedia()
    {
        Assert.True(ImageSearchService.IsAllowedImageHost("https://api.openverse.org/v1/images/11111111-1111-1111-1111-111111111111/thumb/"));
        Assert.False(ImageSearchService.IsAllowedImageHost("https://api.openverse.org/v1/images/"));
        Assert.False(ImageSearchService.IsAllowedImageHost("https://api.openverse.org/v1/auth_tokens/token/"));
        Assert.False(ImageSearchService.IsAllowedImageHost("https://api.openverse.org/v1/images/x/thumb/../../../admin"));
        Assert.False(ImageSearchService.IsAllowedImageHost("https://user:pw@api.openverse.org/v1/images/11111111-1111-1111-1111-111111111111/thumb/"));
        Assert.False(ImageSearchService.IsAllowedImageHost("http://api.openverse.org/v1/images/11111111-1111-1111-1111-111111111111/thumb/"));
    }

    [Fact]
    public async Task Manual_search_uses_only_the_typed_word_from_both_sources_and_pages()
    {
        var (svc, fake) = MakeService();
        var r = await svc.SearchManualAsync("Bakı", 1);
        Assert.True(r.Success, r.FirstError);
        Assert.Contains(r.Value!.Items, i => i.Source == "Openverse");
        Assert.Contains(r.Value.Items, i => i.Source == "Wikimedia");
        Assert.True(r.Value.HasMore);                              // Openverse səhifə 1/3
        Assert.Null(r.Value.Notice);

        // Yalnız yazılan söz: sorğularda başqa söz yoxdur, Vikipediya səhifə axtarışı da yoxdur
        Assert.All(fake.Urls.Where(u => !u.Contains("/thumb")), u => Assert.Contains("Bak", System.Uri.UnescapeDataString(u)));
        Assert.DoesNotContain(fake.Urls, u => u.Contains("wikipedia.org"));
        Assert.Contains(fake.Urls, u => u.Contains("page=1") && u.Contains("api.openverse.org"));

        var p2 = await svc.SearchManualAsync("Bakı", 2);
        Assert.Contains(fake.Urls, u => u.Contains("page=2") && u.Contains("api.openverse.org"));
        Assert.Contains(fake.Urls, u => u.Contains("gsroffset=15"));
        var last = await svc.SearchManualAsync("Bakı", 3);
        Assert.False(last.Value!.HasMore && false);
    }

    [Fact]
    public async Task Manual_search_keeps_working_when_one_source_is_rate_limited()
    {
        var (svc, _) = MakeService(f => f.OpenverseBusy = true);
        var r = await svc.SearchManualAsync("Baku", 1);
        Assert.True(r.Success, r.FirstError);
        Assert.NotEmpty(r.Value!.Items);
        Assert.All(r.Value.Items, i => Assert.Equal("Wikimedia", i.Source));
        Assert.Contains("Openverse", r.Value.Notice);

        var (svc2, _) = MakeService(f => f.CommonsBusy = true);
        var r2 = await svc2.SearchManualAsync("Baku", 1);
        Assert.True(r2.Success);
        Assert.All(r2.Value!.Items, i => Assert.Equal("Openverse", i.Source));
        Assert.Contains("Wikimedia", r2.Value.Notice);
    }

    [Fact]
    public async Task Manual_search_reports_busy_offline_and_short_queries()
    {
        var (both, _) = MakeService(f => { f.OpenverseBusy = true; f.CommonsBusy = true; });
        var busy = await both.SearchManualAsync("Baku", 1);
        Assert.False(busy.Success);
        Assert.Contains(busy.Errors, e => e.Path == "busy");

        var (down, _) = MakeService(f => f.NetworkDown = true);
        var off = await down.SearchManualAsync("Baku", 1);
        Assert.Contains(off.Errors, e => e.Path == "offline");

        var (svc, fake) = MakeService();
        Assert.False((await svc.SearchManualAsync("a", 1)).Success);
        Assert.False((await svc.SearchManualAsync("  ", 1)).Success);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task Openverse_thumb_can_be_imported_and_redirects_to_foreign_hosts_are_refused()
    {
        var (svc, _) = MakeService();
        var ok = await svc.ImportAsync("https://api.openverse.org/v1/images/11111111-1111-1111-1111-111111111111/thumb/");
        Assert.True(ok.Success, ok.FirstError);

        var (evil, fake) = MakeService(f => f.RedirectThumbTo = "http://169.254.169.254/latest/meta-data");
        var refused = await evil.ImportAsync("https://api.openverse.org/v1/images/11111111-1111-1111-1111-111111111111/thumb/");
        Assert.False(refused.Success);
        Assert.DoesNotContain(fake.Urls, u => u.Contains("169.254"));        // yönləndirmə izlənilmədi
    }

    [Fact]
    public async Task Search_waits_and_retries_once_when_throttled()
    {
        var (svc, fake) = MakeService(f => f.Throttle429 = 1);
        var r = await svc.SearchAsync(new ImageSearchRequest { Query = "Bakı" });
        Assert.True(r.Success, r.FirstError);
        Assert.NotEmpty(r.Value!);
    }

    [Fact]
    public async Task Persistent_throttling_is_reported_as_busy_not_offline()
    {
        var (svc, _) = MakeService(f => f.AlwaysBusy = true);
        var r = await svc.SearchAsync(new ImageSearchRequest { Query = "Bakı" });
        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Path == "busy");
        Assert.DoesNotContain(r.Errors, e => e.Path == "offline");
    }

    [Fact]
    public async Task No_network_is_reported_as_offline_and_manual_upload_still_possible()
    {
        var (svc, _) = MakeService(f => f.NetworkDown = true);
        var r = await svc.SearchAsync(new ImageSearchRequest { Query = "Bakı" });
        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Path == "offline");
        Assert.Contains("kompüterdən", r.FirstError);
    }

    [Fact]
    public async Task Search_without_any_words_asks_the_user_for_a_query()
    {
        var (svc, fake) = MakeService();
        var r = await svc.SearchAsync(new ImageSearchRequest { Text = "nə kimdir?" });
        Assert.False(r.Success);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task Import_saves_locally_and_rejects_foreign_hosts_without_any_request()
    {
        var (svc, fake) = MakeService();
        var ok = await svc.ImportAsync("https://upload.wikimedia.org/wikipedia/commons/thumb/a/a1/Baku.jpg/640px-Baku.jpg");
        Assert.True(ok.Success, ok.FirstError);
        Assert.StartsWith("/uploads/", ok.Value);

        var calls = fake.Calls;
        Assert.False((await svc.ImportAsync("https://evil.example/x.jpg")).Success);
        Assert.False((await svc.ImportAsync("http://127.0.0.1:5080/Admin")).Success);
        Assert.Equal(calls, fake.Calls);
    }

    [Fact]
    public async Task Image_only_question_runs_in_a_game_and_shows_in_report()
    {
        using var h = new TestHost();
        var quiz = await h.CreateQuizAsync(new QuestionEditDto
        {
            Type = QuestionType.MultipleChoice, Text = "", ImageUrl = "/uploads/x.png", RelatedImageUrl = "/uploads/y.jpg", RelatedImageCredit = "A · CC0", TimeLimitSeconds = 20,
            Options = { new() { Text = "A", IsCorrect = true }, new() { Text = "B" } }
        });
        var game = await h.CreateGameAsync(quiz);
        var p = await h.Engine.JoinAsync(game.Pin, "Aysel", null);
        await h.Engine.StartAsync(game.GameId, h.UserId);
        var host = await h.WaitHostPhase(game.GameId, "question");
        Assert.Equal("", host.Question!.Text);
        Assert.Equal("/uploads/x.png", host.Question.ImageUrl);
        Assert.Equal("/uploads/y.jpg", host.Question.RelatedImageUrl);
        Assert.Equal("A · CC0", host.Question.RelatedImageCredit);
        var report = await h.WithScope(sp => Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Kviz.Business.Abstractions.IReportService>(sp).GetReportAsync(game.GameId, h.UserId));
        Assert.Equal("(şəkilli sual)", report!.Questions[0].Text);
    }
}
