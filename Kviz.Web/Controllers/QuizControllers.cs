using Kviz.Business.Abstractions;
using Kviz.Web.Infrastructure;
using Kviz.Business.Models;
using Kviz.Business.Services;
using Kviz.Web.Infrastructure;
using Kviz.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kviz.Web.Controllers;

public class ImageImportRequest
{
    public string? Url { get; set; }
}

[GuestLibrary]
public class LibraryController : Controller
{
    private readonly IQuizService _quizzes;
    public LibraryController(IQuizService quizzes) => _quizzes = quizzes;

    public async Task<IActionResult> Index([FromQuery] LibraryQueryDto query)
    {
        query.PageSize = 12;
        var result = await _quizzes.SearchPublicAsync(query, User.GetUserIdOrNull());
        return View(new LibraryViewModel { Result = result, Query = query, Categories = await _quizzes.GetCategoriesAsync() });
    }
}

[Authorize]
public class QuizController : Controller
{
    private readonly IQuizService _quizzes;
    private readonly IQuizExcelService _excel;
    private readonly IImageUploadService _images;
    private readonly IImageSearchService _imageSearch;

    public QuizController(IQuizService quizzes, IQuizExcelService excel, IImageUploadService images, IImageSearchService imageSearch)
    {
        _quizzes = quizzes;
        _excel = excel;
        _images = images;
        _imageSearch = imageSearch;
    }

    public async Task<IActionResult> Index(string tab = "mine", string? q = null, int page = 1)
    {
        var uid = User.GetUserId();
        var result = tab == "fav" ? await _quizzes.ListFavoritesAsync(uid, page) : await _quizzes.ListMineAsync(uid, q, page);
        return View(new MyQuizzesViewModel { Result = result, Search = q, Tab = tab == "fav" ? "fav" : "mine" });
    }

    [AllowAnonymous, GuestLibrary]
    public async Task<IActionResult> Details(Guid id)
    {
        var d = await _quizzes.GetDetailsAsync(id, User.GetUserIdOrNull(), User.IsInRole(Kviz.Entities.AppRoles.Admin));
        if (d == null) return NotFound();
        return View(d);
    }

    // ---------- Redaktor ----------

    public async Task<IActionResult> Create() =>
        View("Editor", new QuizEditorViewModel { Quiz = _quizzes.NewDraft(), Categories = await _quizzes.GetCategoriesAsync() });

    public async Task<IActionResult> Edit(Guid id)
    {
        var quiz = await _quizzes.GetForEditAsync(id, User.GetUserId());
        if (quiz == null) return NotFound();
        return View("Editor", new QuizEditorViewModel { Quiz = quiz, Categories = await _quizzes.GetCategoriesAsync() });
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] QuizEditDto dto)
    {
        if (dto == null) return BadRequest(new { ok = false, errors = new[] { new { path = "", message = "Məlumat oxunmadı." } } });
        var result = await _quizzes.SaveAsync(User.GetUserId(), dto);
        if (!result.Success)
            return Ok(new { ok = false, errors = result.Errors.Select(e => new { e.Path, e.Message }).Select(e => new { path = e.Path, message = e.Message }) });
        return Ok(new { ok = true, id = result.Value, detailsUrl = Url.Action(nameof(Details), new { id = result.Value }), editUrl = Url.Action(nameof(Edit), new { id = result.Value }) });
    }

    // ---------- Əməliyyatlar ----------

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id, string? returnTab)
    {
        var r = await _quizzes.DeleteAsync(id, User.GetUserId());
        TempData[r.Success ? "Success" : "Error"] = r.Success ? "Kviz silindi." : r.FirstError;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Copy(Guid id)
    {
        var r = await _quizzes.CopyAsync(id, User.GetUserId());
        if (!r.Success)
        {
            TempData["Error"] = r.FirstError;
            return RedirectToAction("Index", "Library");
        }
        TempData["Success"] = "Kviz kopyalandı. İndi onu redaktə edə bilərsiniz.";
        return RedirectToAction(nameof(Details), new { id = r.Value });
    }

    [HttpPost]
    public async Task<IActionResult> Visibility(Guid id, bool isPublic)
    {
        var r = await _quizzes.SetVisibilityAsync(id, User.GetUserId(), isPublic);
        TempData[r.Success ? "Success" : "Error"] = r.Success
            ? (isPublic ? "Kviz hamıya açıqdır və kitabxanada görünür." : "Kviz gizli edildi.") : r.FirstError;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Favorite(Guid id)
    {
        var fav = await _quizzes.ToggleFavoriteAsync(id, User.GetUserId());
        return Json(new { isFavorite = fav });
    }

    // ---------- Excel ----------

    [HttpGet]
    public IActionResult Import() => View(new QuizImportViewModel());

    [HttpPost, RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> Import(QuizImportViewModel model)
    {
        if (model.File == null || model.File.Length == 0) ModelState.AddModelError(nameof(model.File), "Excel faylını seçin.");
        else if (!model.File.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) ModelState.AddModelError(nameof(model.File), "Yalnız .xlsx faylı qəbul olunur.");
        else if (model.File.Length > 5 * 1024 * 1024) ModelState.AddModelError(nameof(model.File), "Fayl 5 MB-dan böyük ola bilməz.");
        if (!ModelState.IsValid) return View(model);

        await using var stream = model.File!.OpenReadStream();
        var r = await _excel.ImportAsync(User.GetUserId(), model.Title, stream);
        if (!r.Success)
        {
            foreach (var e in r.Errors) ModelState.AddModelError(e.Path.StartsWith("title") ? nameof(model.Title) : string.Empty, e.Message);
            return View(model);
        }
        TempData["Success"] = $"{r.Value!.ImportedQuestions} sual idxal edildi." + (r.Value.Warnings.Count > 0 ? $" Diqqət: {string.Join(" ", r.Value.Warnings.Take(3))}" : "");
        return RedirectToAction(nameof(Edit), new { id = r.Value.QuizId });
    }

    [HttpGet]
    public IActionResult Template() =>
        File(_excel.BuildTemplate(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "kviz-sablon.xlsx");

    [HttpGet]
    public async Task<IActionResult> ExportExcel(Guid id)
    {
        var bytes = await _excel.ExportQuizAsync(id, User.GetUserId());
        return bytes == null ? NotFound() : File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "kviz.xlsx");
    }

    // ---------- Şəkil ----------

    /// <summary>Sualın mətninə/cavabına uyğun şəkilləri internetdən (Wikipedia, Wikimedia Commons) tapır.</summary>
    [HttpPost, Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("images")]
    public async Task<IActionResult> ImageSearch([FromBody] ImageSearchRequest request)
    {
        request ??= new ImageSearchRequest();

        // Axtarış sözü əl ilə yazılıbsa: yalnız həmin söz, Openverse + Wikimedia, səhifələmə ilə
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var m = await _imageSearch.SearchManualAsync(request.Query, request.Page, HttpContext.RequestAborted);
            if (!m.Success)
                return Ok(new { ok = false, offline = m.Errors.Any(e => e.Path == "offline"), busy = m.Errors.Any(e => e.Path == "busy"), error = m.FirstError });
            return Ok(new { ok = true, results = m.Value!.Items, hasMore = m.Value.HasMore, notice = m.Value.Notice, page = Math.Clamp(request.Page, 1, 10) });
        }

        // Əks halda avtomatik təkliflər (sualın cavabı və açar sözləri ilə)
        var r = await _imageSearch.SearchAsync(request, HttpContext.RequestAborted);
        if (!r.Success)
            return Ok(new { ok = false, offline = r.Errors.Any(e => e.Path == "offline"), busy = r.Errors.Any(e => e.Path == "busy"), error = r.FirstError });
        return Ok(new { ok = true, results = r.Value });
    }

    /// <summary>Seçilmiş internet şəklini serverə yükləyir ki, oyun zamanı internet lazım olmasın.</summary>
    [HttpPost, Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("images")]
    public async Task<IActionResult> ImageImport([FromBody] ImageImportRequest request)
    {
        var r = await _imageSearch.ImportAsync(request?.Url ?? "", HttpContext.RequestAborted);
        return r.Success ? Ok(new { ok = true, url = r.Value }) : Ok(new { ok = false, error = r.FirstError });
    }

    [HttpPost, RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> UploadImage(IFormFile? file)
    {
        if (file == null) return BadRequest(new { ok = false, error = "Fayl seçilməyib." });
        await using var s = file.OpenReadStream();
        var r = await _images.SaveAsync(s, file.FileName, file.Length, HttpContext.RequestAborted);
        return r.Success ? Ok(new { ok = true, url = r.Value }) : BadRequest(new { ok = false, error = r.FirstError });
    }
}
