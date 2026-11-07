using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kviz.Business.Common;
using Kviz.Business.Games;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Kviz.Business.Services;

public class ImageSearchOptions
{
    /// <summary>
    /// Saytın sahibinin əlaqə ünvanı (e-poçt və ya sayt). Wikimedia API qaydalarına görə sorğu göndərən proqram
    /// özünü və əlaqə məlumatını göstərməlidir. Olmasa sorğular tez-tez "çox sorğu" (429) ilə rədd edilir.
    /// </summary>
    public string? Contact { get; set; }
}

/// <summary>Şəkil mənbəyi müvəqqəti olaraq çox sorğu görüb və ya cavab vermir.</summary>
public class ImageSourceBusyException : Exception
{
    public ImageSourceBusyException(string message) : base(message) { }
}

public class ImageSearchResultDto
{
    public string Title { get; set; } = string.Empty;
    public string ThumbUrl { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Müəllif və lisenziya, ekranda şəklin altında göstərilir.</summary>
    public string Credit { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    /// <summary>Şəklin tapıldığı mənbə: "Openverse" və ya "Wikimedia".</summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>Tək-tək axtarışın bir səhifəsi.</summary>
public class ImageSearchPageDto
{
    public List<ImageSearchResultDto> Items { get; set; } = new();
    public bool HasMore { get; set; }
    /// <summary>Mənbələrdən biri cavab vermədikdə istifadəçiyə göstərilən izah.</summary>
    public string? Notice { get; set; }
}

public class ImageSearchRequest
{
    /// <summary>İstifadəçi əl ilə axtarış sözü yazıbsa yalnız o istifadə olunur.</summary>
    public string? Query { get; set; }
    public string? Text { get; set; }
    public List<string> Answers { get; set; } = new();
    /// <summary>Tək-tək axtarışda səhifə nömrəsi (1-dən başlayır).</summary>
    public int Page { get; set; } = 1;
}

public interface IImageSearchService
{
    /// <summary>Avtomatik axtarış (bütün suallar üçün): sualın cavabı və açar sözləri ilə, az sorğu ilə.</summary>
    Task<ServiceResult<List<ImageSearchResultDto>>> SearchAsync(ImageSearchRequest request, CancellationToken ct = default);
    /// <summary>Tək-tək axtarış: yalnız yazılan sözə görə, Openverse + Wikimedia, səhifələmə ilə (Google kimi).</summary>
    Task<ServiceResult<ImageSearchPageDto>> SearchManualAsync(string query, int page, CancellationToken ct = default);
    /// <summary>Seçilmiş şəkli internetdən yükləyib serverdə saxlayır (oyun zamanı internet lazım olmasın).</summary>
    Task<ServiceResult<string>> ImportAsync(string url, CancellationToken ct = default);
}

/// <summary>Sualın mətnindən və düzgün cavabından axtarış sorğuları hazırlayır.</summary>
public static class ImageQueryBuilder
{
    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "hansı", "hansılar", "hansıdır", "hansılardır", "nədir", "nə", "nələr", "nələrdir", "kimdir", "kim", "kimlər", "neçə", "neçədir", "neçəncidir",
        "harada", "haradadır", "hara", "haradan", "necə", "niyə", "niyədir", "nə", "vaxt", "zaman", "və", "ilə", "ya", "yaxud", "da", "də", "bu", "o",
        "bir", "olan", "olur", "olub", "edir", "edilir", "edilib", "verilir", "adlanır", "adlandırılır", "dir", "dır", "dur", "dür", "ən", "çox",
        "aşağıdakılardan", "aşağıdakı", "düzgün", "yanlış", "cavab", "sual", "seçin", "tapın", "yazın", "göstərin", "bilirsiniz", "məşhur", "məşhurdur",
        "the", "is", "of", "what", "which", "who", "where", "how", "many", "a", "an", "in", "on", "and", "or", "to"
    };

    private static readonly Regex Token = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);

    /// <summary>Ən çox 3 sorğu: düzgün cavab(lar) və sualın açar sözləri.</summary>
    public static List<string> Build(string? text, IEnumerable<string>? answers)
    {
        var result = new List<string>();
        void Add(string? q)
        {
            q = (q ?? "").Trim();
            if (q.Length < 2 || q.Length > 80) return;
            if (!result.Contains(q, StringComparer.OrdinalIgnoreCase)) result.Add(q);
        }

        // 1) Düzgün cavab ən yaxşı mövzu göstəricisidir ("Azərbaycanın paytaxtı?" → "Bakı"). "Doğru/Yanlış" və rəqəmlər çıxarılır.
        foreach (var a in (answers ?? Enumerable.Empty<string>()).Take(2))
        {
            var t = (a ?? "").Trim();
            if (t.Length < 3 || double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) continue;
            if (TextNormalizer.Normalize(t) is "dogru" or "yanlis" or "doğru" or "yanliş" or "yanlış") continue;
            Add(t);
        }

        // 2) Sualın açar sözləri
        var words = Token.Matches(text ?? "").Select(m => m.Value)
            .Where(w => w.Length >= 3 && !Stop.Contains(w) && !double.TryParse(w, out _)).Take(4).ToList();
        if (words.Count > 0) Add(string.Join(' ', words));
        // 3) Cavab + əsas söz birlikdə
        if (result.Count > 0 && words.Count > 0) Add(result[0] + " " + words[0]);
        return result.Take(3).ToList();
    }
}

public class ImageSearchService : IImageSearchService
{
    public const long MaxDownloadBytes = 5 * 1024 * 1024;
    private static readonly string[] AllowedMime = { "image/jpeg", "image/png", "image/webp", "image/gif" };
    private static readonly Regex Tags = new("<.*?>", RegexOptions.Compiled | RegexOptions.Singleline);

    private readonly IHttpClientFactory _http;
    private readonly IImageUploadService _upload;
    private readonly ILogger<ImageSearchService> _log;
    private readonly IMemoryCache _cache;

    // Wikimedia API-yə sorğular ardıcıl göndərilir (paralel yox) və aralarında qısa fasilə qoyulur: server yüklənməsin.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(30);

    public ImageSearchService(IHttpClientFactory http, IImageUploadService upload, ILogger<ImageSearchService> log, IMemoryCache cache)
    {
        _http = http;
        _upload = upload;
        _log = log;
        _cache = cache;
    }

    /// <summary>Sorğunu göndərir. 429/503 gələndə "Retry-After" qədər (ən çox 8 san) bir dəfə gözləyib təkrarlayır.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpClient client, string url, HttpCompletionOption option, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var res = await client.GetAsync(url, option, ct);
                if (res.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(3);
                    res.Dispose();
                    if (attempt >= 1 || wait > TimeSpan.FromSeconds(8))
                        throw new ImageSourceBusyException("Şəkil mənbəyi hazırda çox sorğu alır.");
                    await Task.Delay(wait, ct);
                    continue;
                }
                await Task.Delay(120, ct);
                return res;
            }
        }
        finally { Gate.Release(); }
    }

    private async Task<string> GetJsonAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var res = await SendAsync(client, url, HttpCompletionOption.ResponseContentRead, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(ct);
    }

    public async Task<ServiceResult<List<ImageSearchResultDto>>> SearchAsync(ImageSearchRequest request, CancellationToken ct = default)
    {
        var queries = !string.IsNullOrWhiteSpace(request.Query)
            ? new List<string> { request.Query.Trim()[..Math.Min(80, request.Query.Trim().Length)] }
            : ImageQueryBuilder.Build(request.Text, request.Answers);
        if (queries.Count == 0)
            return ServiceResult<List<ImageSearchResultDto>>.Fail("Axtarış üçün söz tapılmadı. Sual və ya cavab yazın, ya da axtarış sözünü özünüz daxil edin.");

        // Eyni sual təkrar axtarılanda internetə çıxmırıq
        var cacheKey = "imgsearch:" + string.Join("|", queries).ToLowerInvariant();
        if (_cache.TryGetValue(cacheKey, out List<ImageSearchResultDto>? cached) && cached != null)
            return ServiceResult<List<ImageSearchResultDto>>.Ok(cached);

        try
        {
            var client = _http.CreateClient("images");
            queries = queries.Take(2).ToList(); // ən uyğun 2 sorğu: Wikimedia-ya az sorğu
            var all = new List<List<ImageSearchResultDto>>();
            foreach (var q in queries)
            {
                // Əvvəl Azərbaycan Vikipediyası, nəticə azdırsa ingilis dili; sonra Commons fayl axtarışı
                var wiki = await SearchWikipediaAsync(client, "az", q, ct);
                // İngilis dili yalnız Azərbaycanca heç nə tapılmayanda: əks halda yad sözlər səs-küylü nəticə verir
                if (wiki.Count == 0) wiki = await SearchWikipediaAsync(client, "en", q, ct);
                all.Add(wiki);
                all.Add(await SearchCommonsAsync(client, q, ct));
            }

            // Sıra mənbənin uyğunluğuna görədir: ilk sorğunun (düzgün cavabın) Vikipediya əsas şəkilləri, sonra Commons
            // fayl axtarışı, sonra ikinci sorğu. Təkrarlar atılır.
            var merged = new List<ImageSearchResultDto>();
            var seen = new HashSet<string>();
            foreach (var item in all.SelectMany(l => l))
                if (merged.Count < 12 && seen.Add(item.Title)) merged.Add(item);

            if (merged.Count > 0) _cache.Set(cacheKey, merged, CacheTime);
            return ServiceResult<List<ImageSearchResultDto>>.Ok(merged);
        }
        catch (ImageSourceBusyException)
        {
            return ServiceResult<List<ImageSearchResultDto>>.Fail("Şəkil mənbəyi hazırda çox sorğu alır. Bir neçə saniyə sonra yenidən yoxlayın.", "busy");
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
        {
            // Əlaqə var, amma mənbə xəta qaytarıb: bu "internet yoxdur" deyil
            _log.LogWarning(ex, "Şəkil mənbəyi {Status} qaytardı", ex.StatusCode);
            return ServiceResult<List<ImageSearchResultDto>>.Fail("Şəkil mənbəyi hazırda cavab vermir. Bir az sonra yenidən yoxlayın.", "busy");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            _log.LogInformation(ex, "Şəkil axtarışı alınmadı (internet yoxdur?)");
            return ServiceResult<List<ImageSearchResultDto>>.Fail("İnternetə qoşulmaq mümkün olmadı. Şəkli kompüterdən yükləyə bilərsiniz.", "offline");
        }
    }

    // ---------- Wikipedia: mövzu üzrə əsas səhifə şəkilləri (ən uyğun nəticələr) ----------

    private async Task<List<ImageSearchResultDto>> SearchWikipediaAsync(HttpClient client, string lang, string query, CancellationToken ct)
    {
        var url = $"https://{lang}.wikipedia.org/w/api.php?action=query&format=json&generator=search&gsrnamespace=0&gsrlimit=6" +
                  $"&gsrsearch={Uri.EscapeDataString(query)}&prop=pageimages&piprop=name&redirects=1";
        // 1) Dəqiq mövzu səhifəsi ("Bakı" → Bakı səhifəsindəki fotolar): ən uyğun mənbə
        var exactUrl = $"https://{lang}.wikipedia.org/w/api.php?action=query&format=json&redirects=1&prop=images|langlinks&imlimit=40&lllang=en&titles={Uri.EscapeDataString(query)}";
        var exactJson = await GetJsonAsync(client, exactUrl, ct);
        var exact = ParsePagePhotos(exactJson);

        // Səhifənin ingilis dilindəki qarşılığı (Bakı → Baku) adətən çox daha zəngin foto kolleksiyasına malikdir
        var enTitle = lang == "az" ? ParseEnglishTitle(exactJson) : null;
        if (enTitle != null)
        {
            var enUrl = $"https://en.wikipedia.org/w/api.php?action=query&format=json&redirects=1&prop=images&imlimit=40&titles={Uri.EscapeDataString(enTitle)}";
            exact = exact.Concat(ParsePagePhotos(await GetJsonAsync(client, enUrl, ct))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // 2) Tam mətn axtarışının nəticələrinin əsas şəkilləri
        var json = await GetJsonAsync(client, url, ct);
        var files = exact.Concat(ParseWikipediaPageImages(json)).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        if (files.Count == 0) return new();
        // Vikipediya fayl adlarını alt xətlə ("Baku_skyline.jpg"), Commons isə boşluqla qaytarır: sıranı itirməmək üçün eyniləşdiririk
        return await FetchImageInfoAsync(client, files.Select(f => "File:" + f.Replace('_', ' ')).ToList(), ct);
    }

    private static readonly Regex NotAPhoto = new(@"(icon|logo|flag|coat|coa[ _\-.0-9]|\bcoa\b|gerb|arms|map|symbol|commons|wiki|button|stub|pictogram|ambox|question|edit-|loupe|disambig|signature|seal|emblem|sprite|template|crystal|nuvola|padlock|increase|decrease|steady|locator|location|diagram|chart|graph|banner|portal|speaker|audio|sound)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// "prop=images" cavabından səhifədəki foto şəkillərini çıxarır: yalnız jpg/png/webp, ikon, bayraq, xəritə, loqo kimi
    /// bəzək elementləri atılır. Fayl adlarındakı yerli ad sahəsi ("Fayl:") "File:" ilə əvəz olunmur, yalnız ad qaytarılır.
    /// </summary>
    public static List<string> ParsePagePhotos(string json)
    {
        var result = new List<string>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("query", out var q) || !q.TryGetProperty("pages", out var pages)) return result;
        foreach (var page in pages.EnumerateObject())
        {
            if (!page.Value.TryGetProperty("images", out var images)) continue;
            foreach (var img in images.EnumerateArray())
            {
                var title = img.TryGetProperty("title", out var t) ? t.GetString() : null;
                if (string.IsNullOrEmpty(title)) continue;
                var name = title[(title.IndexOf(':') + 1)..].Trim(); // "Fayl:Baku.jpg" → "Baku.jpg"
                var ext = Path.GetExtension(name).ToLowerInvariant();
                if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp")) continue;
                if (NotAPhoto.IsMatch(name)) continue;
                result.Add(name);
                if (result.Count >= 6) return result;
            }
        }
        return result;
    }

    /// <summary>"prop=langlinks&amp;lllang=en" cavabından səhifənin ingilis dilindəki başlığını çıxarır.</summary>
    public static string? ParseEnglishTitle(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("query", out var q) || !q.TryGetProperty("pages", out var pages)) return null;
        foreach (var page in pages.EnumerateObject())
        {
            if (!page.Value.TryGetProperty("langlinks", out var links)) continue;
            foreach (var l in links.EnumerateArray())
            {
                if (l.TryGetProperty("lang", out var lg) && lg.GetString() == "en")
                {
                    var title = l.TryGetProperty("*", out var s) ? s.GetString() : l.TryGetProperty("title", out var t2) ? t2.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(title)) return title;
                }
            }
        }
        return null;
    }

    /// <summary>Wikipedia cavabından səhifələrin əsas şəkil fayl adlarını (axtarış sırası ilə) çıxarır.</summary>
    public static List<string> ParseWikipediaPageImages(string json)
    {
        var list = new List<(int index, string file)>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("query", out var q) || !q.TryGetProperty("pages", out var pages)) return new();
        foreach (var page in pages.EnumerateObject())
        {
            var p = page.Value;
            if (p.TryGetProperty("pageimage", out var pi) && pi.ValueKind == JsonValueKind.String)
            {
                var idx = p.TryGetProperty("index", out var ix) && ix.TryGetInt32(out var n) ? n : 99;
                var file = pi.GetString()!;
                if (!file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) list.Add((idx, file));
            }
        }
        return list.OrderBy(x => x.index).Select(x => x.file).Distinct().Take(5).ToList();
    }

    // ---------- Wikimedia Commons: fayl axtarışı ----------

    private async Task<List<ImageSearchResultDto>> SearchCommonsAsync(HttpClient client, string query, CancellationToken ct)
    {
        var url = "https://commons.wikimedia.org/w/api.php?action=query&format=json&generator=search&gsrnamespace=6&gsrlimit=8" +
                  $"&gsrsearch={Uri.EscapeDataString(query + " filetype:bitmap")}&prop=imageinfo&iiprop=url|mime|extmetadata" +
                  "&iiurlwidth=640&iiextmetadatafilter=Artist|LicenseShortName";
        return ParseImageInfo(await GetJsonAsync(client, url, ct));
    }

    private async Task<List<ImageSearchResultDto>> FetchImageInfoAsync(HttpClient client, List<string> titles, CancellationToken ct)
    {
        var url = "https://commons.wikimedia.org/w/api.php?action=query&format=json&prop=imageinfo&iiprop=url|mime|extmetadata" +
                  $"&iiurlwidth=640&iiextmetadatafilter=Artist|LicenseShortName&titles={Uri.EscapeDataString(string.Join('|', titles))}";
        return OrderByTitles(ParseImageInfo(await GetJsonAsync(client, url, ct)), titles);
    }

    /// <summary>Nəticələri Vikipediya axtarışındakı (ən uyğundan başlayan) fayl sırasına salır; tanınmayanlar sona gedir.</summary>
    public static List<ImageSearchResultDto> OrderByTitles(List<ImageSearchResultDto> items, List<string> titles)
    {
        int Rank(ImageSearchResultDto p)
        {
            var i = titles.FindIndex(t => string.Equals(t.Replace('_', ' '), p.Title.Replace('_', ' '), StringComparison.OrdinalIgnoreCase));
            return i < 0 ? int.MaxValue : i;
        }
        return items.OrderBy(Rank).ToList();
    }

    /// <summary>Commons "imageinfo" cavabını sadə nəticələrə çevirir (yalnız jpeg/png/webp/gif).</summary>
    public static List<ImageSearchResultDto> ParseImageInfo(string json)
    {
        var result = new List<(int index, ImageSearchResultDto dto)>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("query", out var q) || !q.TryGetProperty("pages", out var pages)) return new();

        foreach (var page in pages.EnumerateObject())
        {
            var p = page.Value;
            if (!p.TryGetProperty("imageinfo", out var infos) || infos.GetArrayLength() == 0) continue;
            var info = infos[0];
            var mime = info.TryGetProperty("mime", out var m) ? m.GetString() : null;
            if (mime == null || !AllowedMime.Contains(mime)) continue;
            var thumb = info.TryGetProperty("thumburl", out var t) ? t.GetString() : info.TryGetProperty("url", out var u) ? u.GetString() : null;
            if (string.IsNullOrEmpty(thumb) || !IsAllowedImageHost(thumb)) continue;

            string Meta(string key)
            {
                if (info.TryGetProperty("extmetadata", out var ext) && ext.TryGetProperty(key, out var k) && k.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
                    return CleanHtml(v.GetString() ?? "");
                return "";
            }
            var artist = Meta("Artist");
            var license = Meta("LicenseShortName");
            var credit = string.Join(" · ", new[] { artist.Length > 0 ? artist : null, license.Length > 0 ? license : null, "Wikimedia Commons" }.Where(x => x != null));
            if (credit.Length > 300) credit = credit[..300];

            var title = p.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "";
            result.Add((p.TryGetProperty("index", out var ix) && ix.TryGetInt32(out var n) ? n : 99, new ImageSearchResultDto
            {
                Title = title,
                ThumbUrl = thumb,
                Width = info.TryGetProperty("thumbwidth", out var w) && w.TryGetInt32(out var wi) ? wi : 0,
                Height = info.TryGetProperty("thumbheight", out var h) && h.TryGetInt32(out var hi) ? hi : 0,
                Credit = credit,
                SourceUrl = info.TryGetProperty("descriptionurl", out var d) ? d.GetString() ?? "" : "",
                Source = "Wikimedia"
            }));
        }
        return result.OrderBy(x => x.index).Select(x => x.dto).ToList();
    }

    private static string CleanHtml(string html)
    {
        var s = WebUtility.HtmlDecode(Tags.Replace(html, " "));
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length > 120 ? s[..120].TrimEnd() + "…" : s;
    }

    private static readonly Regex OpenverseThumbPath = new(@"^/v1/images/[0-9a-fA-F\-]{36}/thumb/?$", RegexOptions.Compiled);

    /// <summary>
    /// SSRF-dən qorunmaq üçün yalnız iki dəqiq ünvana icazə verilir: Wikimedia-nın şəkil serveri və
    /// Openverse-in şəkil kiçiltmə ünvanı (/v1/images/{id}/thumb/). Başqa heç nə yüklənmir.
    /// </summary>
    public static bool IsAllowedImageHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(u.UserInfo)) return false;
        if (string.Equals(u.Host, "upload.wikimedia.org", StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(u.Host, "api.openverse.org", StringComparison.OrdinalIgnoreCase) && OpenverseThumbPath.IsMatch(u.AbsolutePath);
    }

    // ---------- Tək-tək axtarış: yalnız yazılan söz, Openverse + Wikimedia Commons ----------

    private const int OpenversePageSize = 20;
    private const int CommonsPageSize = 15;

    public async Task<ServiceResult<ImageSearchPageDto>> SearchManualAsync(string query, int page, CancellationToken ct = default)
    {
        query = (query ?? "").Trim();
        if (query.Length < 2) return ServiceResult<ImageSearchPageDto>.Fail("Axtarış sözünü yazın.");
        if (query.Length > 80) query = query[..80];
        page = Math.Clamp(page, 1, 10);

        var cacheKey = $"imgmanual:{page}:{query.ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out ImageSearchPageDto? cached) && cached != null)
            return ServiceResult<ImageSearchPageDto>.Ok(cached);

        var client = _http.CreateClient("images");
        var notices = new List<string>();
        var openverse = new List<ImageSearchResultDto>();
        var commons = new List<ImageSearchResultDto>();
        bool moreOpenverse = false, moreCommons = false, openverseOk = false, commonsOk = false, offline = false, busy = false;

        // Hər mənbə ayrıca yoxlanılır: biri limitə çatsa və ya cavab verməsə, digəri işləməyə davam edir
        try
        {
            var url = $"https://api.openverse.org/v1/images/?q={Uri.EscapeDataString(query)}&page={page}&page_size={OpenversePageSize}&mature=false";
            (openverse, moreOpenverse) = ParseOpenverse(await GetJsonAsync(client, url, ct), page);
            openverseOk = true;
        }
        catch (ImageSourceBusyException) { busy = true; notices.Add("Openverse hazırda sorğu limitinə çatıb"); }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue) { busy = true; notices.Add("Openverse hazırda cavab vermir"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException or JsonException) { offline = true; }

        try
        {
            var url = "https://commons.wikimedia.org/w/api.php?action=query&format=json&generator=search&gsrnamespace=6" +
                      $"&gsrlimit={CommonsPageSize}&gsroffset={(page - 1) * CommonsPageSize}&gsrsearch={Uri.EscapeDataString(query + " filetype:bitmap")}" +
                      "&prop=imageinfo&iiprop=url|mime|extmetadata&iiurlwidth=640&iiextmetadatafilter=Artist|LicenseShortName";
            var json = await GetJsonAsync(client, url, ct);
            commons = ParseImageInfo(json);
            foreach (var c in commons) c.Source = "Wikimedia";
            moreCommons = HasContinue(json);
            commonsOk = true;
        }
        catch (ImageSourceBusyException) { busy = true; notices.Add("Wikimedia hazırda sorğu limitinə çatıb"); }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue) { busy = true; notices.Add("Wikimedia hazırda cavab vermir"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException or JsonException) { offline = true; }

        if (!openverseOk && !commonsOk)
        {
            return busy
                ? ServiceResult<ImageSearchPageDto>.Fail("Şəkil mənbələri hazırda çox sorğu alır. Bir neçə saniyə sonra yenidən yoxlayın.", "busy")
                : ServiceResult<ImageSearchPageDto>.Fail("İnternetə qoşulmaq mümkün olmadı. Şəkli kompüterdən yükləyə bilərsiniz.", "offline");
        }
        _ = offline;

        // Mənbələr növbələşir: bir Openverse, bir Wikimedia... Təkrarlar atılır
        var items = new List<ImageSearchResultDto>();
        var seen = new HashSet<string>();
        for (var i = 0; i < Math.Max(openverse.Count, commons.Count); i++)
        {
            if (i < openverse.Count && seen.Add(openverse[i].ThumbUrl)) items.Add(openverse[i]);
            if (i < commons.Count && seen.Add(commons[i].ThumbUrl)) items.Add(commons[i]);
        }

        var result = new ImageSearchPageDto
        {
            Items = items,
            HasMore = moreOpenverse || moreCommons,
            Notice = notices.Count > 0 ? string.Join(", ", notices) + ". Digər mənbənin nəticələri göstərilir." : null
        };
        if (items.Count > 0 && result.Notice == null) _cache.Set(cacheKey, result, CacheTime);
        return ServiceResult<ImageSearchPageDto>.Ok(result);
    }

    /// <summary>Commons cavabında "continue" varsa, daha çox nəticə var.</summary>
    public static bool HasContinue(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("continue", out _);
    }

    /// <summary>Openverse "images" cavabını sadə nəticələrə çevirir. İkinci dəyər: sonrakı səhifə varmı.</summary>
    public static (List<ImageSearchResultDto> items, bool hasMore) ParseOpenverse(string json, int page)
    {
        var items = new List<ImageSearchResultDto>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return (items, false);

        foreach (var r in results.EnumerateArray())
        {
            string? Str(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var id = Str("id");
            var thumb = Str("thumbnail");
            if (string.IsNullOrEmpty(thumb) && !string.IsNullOrEmpty(id)) thumb = $"https://api.openverse.org/v1/images/{id}/thumb/";
            if (string.IsNullOrEmpty(thumb) || !IsAllowedImageHost(thumb)) continue;

            var type = (Str("filetype") ?? "").ToLowerInvariant();
            if (type is "svg" or "tif" or "tiff" or "bmp") continue; // brauzer və yükləmə yoxlaması üçün uyğun deyil

            var license = FormatLicense(Str("license"), Str("license_version"));
            var creator = Str("creator");
            var source = Str("source") ?? Str("provider");
            var credit = string.Join(" · ", new[] { string.IsNullOrWhiteSpace(creator) ? null : Trim(creator, 80), license, string.IsNullOrWhiteSpace(source) ? "Openverse" : Capitalize(source) }
                .Where(x => !string.IsNullOrEmpty(x)));

            items.Add(new ImageSearchResultDto
            {
                Title = Str("title") ?? "",
                ThumbUrl = thumb,
                Width = r.TryGetProperty("width", out var w) && w.TryGetInt32(out var wi) ? wi : 0,
                Height = r.TryGetProperty("height", out var h) && h.TryGetInt32(out var hi) ? hi : 0,
                Credit = Trim(credit, 300),
                SourceUrl = Str("foreign_landing_url") ?? "",
                Source = "Openverse"
            });
        }
        var pageCount = root.TryGetProperty("page_count", out var pc) && pc.TryGetInt32(out var p) ? p : 0;
        return (items, pageCount > page);
    }

    /// <summary>"by-sa" + "4.0" → "CC BY-SA 4.0"; "cc0" → "CC0"; "pdm" → "Public domain".</summary>
    public static string FormatLicense(string? license, string? version)
    {
        if (string.IsNullOrWhiteSpace(license)) return "";
        var l = license.Trim().ToLowerInvariant();
        if (l == "cc0") return "CC0";
        if (l == "pdm") return "Public domain";
        return "CC " + l.ToUpperInvariant() + (string.IsNullOrWhiteSpace(version) ? "" : " " + version.Trim());
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";
    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ---------- Yükləmə (lokal saxlama) ----------

    public async Task<ServiceResult<string>> ImportAsync(string url, CancellationToken ct = default)
    {
        if (!IsAllowedImageHost(url ?? "")) return ServiceResult<string>.Fail("Bu şəkil mənbəyinə icazə verilmir.");
        try
        {
            var client = _http.CreateClient("images");
            // Avtomatik yönləndirmə söndürülüb: yönləndirmə yalnız yenə də icazə verilən ünvana ola bilər (SSRF qorunması)
            var current = url;
            var res = await SendAsync(client, current, HttpCompletionOption.ResponseHeadersRead, ct);
            for (var hop = 0; hop < 3 && (int)res.StatusCode is >= 300 and < 400 && res.Headers.Location != null; hop++)
            {
                var next = new Uri(new Uri(current), res.Headers.Location).ToString();
                res.Dispose();
                if (!IsAllowedImageHost(next)) return ServiceResult<string>.Fail("Bu şəkil mənbəyinə icazə verilmir.");
                current = next;
                res = await SendAsync(client, current, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            using var _ = res;
            if (!res.IsSuccessStatusCode) return ServiceResult<string>.Fail("Şəkil yüklənmədi. Başqasını seçin.");
            if (res.Content.Headers.ContentLength is long len && len > MaxDownloadBytes) return ServiceResult<string>.Fail("Şəkil çox böyükdür.");

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxDownloadBytes) return ServiceResult<string>.Fail("Şəkil çox böyükdür.");
            }
            buffer.Position = 0;
            // Faylın real formatı ImageUploadService-də yoxlanılır (uzantıya güvənilmir)
            return await _upload.SaveAsync(buffer, "image.jpg", buffer.Length, ct);
        }
        catch (ImageSourceBusyException)
        {
            return ServiceResult<string>.Fail("Şəkil mənbəyi hazırda çox sorğu alır. Bir neçə saniyə sonra yenidən yoxlayın.", "busy");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            _log.LogInformation(ex, "Şəkil yüklənmədi (internet yoxdur?)");
            return ServiceResult<string>.Fail("İnternetə qoşulmaq mümkün olmadı.", "offline");
        }
    }
}
