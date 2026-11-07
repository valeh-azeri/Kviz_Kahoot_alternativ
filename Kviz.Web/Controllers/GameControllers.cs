using Kviz.Business.Abstractions;
using Kviz.Business.Common;
using Kviz.Business.Games;
using Kviz.Business.Models;
using Kviz.Business.Services;
using Kviz.Entities.Enums;
using Kviz.Web.Infrastructure;
using Kviz.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kviz.Web.Controllers;

/// <summary>Host (müəllim / aparıcı) tərəfi: oyun yaratmaq, idarə etmək, hesabatlar.</summary>
[Authorize]
public class GameController : Controller
{
    private readonly IGameService _games;
    private readonly IQuizService _quizzes;
    private readonly IReportService _reports;
    private readonly IJoinLinkService _links;

    public GameController(IGameService games, IQuizService quizzes, IReportService reports, IJoinLinkService links)
    {
        _games = games;
        _quizzes = quizzes;
        _reports = reports;
        _links = links;
    }

    private JoinInfoDto BuildJoin(string pin, string? preferred = null) =>
        _links.Build(Request.Scheme, Request.Host.Host, Request.Host.Port, pin, preferred);

    [HttpGet]
    public async Task<IActionResult> Setup(Guid quizId, GameMode mode = GameMode.LiveClassic)
    {
        var d = await _quizzes.GetDetailsAsync(quizId, User.GetUserId());
        if (d == null) return NotFound();
        if (d.Card.QuestionCount == 0)
        {
            TempData["Error"] = "Əvvəlcə kvizə sual əlavə edin.";
            return RedirectToAction("Edit", "Quiz", new { id = quizId });
        }
        return View(new GameSetupViewModel { Quiz = d.Card, Options = new CreateGameDto { QuizId = quizId, Mode = mode } });
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateGameDto dto)
    {
        var r = await _games.CreateAsync(User.GetUserId(), dto);
        if (!r.Success)
        {
            TempData["Error"] = r.FirstError;
            return RedirectToAction(nameof(Setup), new { quizId = dto.QuizId, mode = dto.Mode });
        }
        return RedirectToAction(dto.Mode == GameMode.Challenge ? nameof(Challenge) : nameof(Host), new { id = r.Value!.GameId });
    }

    [HttpGet]
    public async Task<IActionResult> Host(Guid id)
    {
        var g = await _games.GetHostGameAsync(id, User.GetUserId());
        if (g == null) return NotFound();
        if (g.Mode == GameMode.Challenge) return RedirectToAction(nameof(Challenge), new { id });
        if (g.Status is GameStatus.Finished or GameStatus.Cancelled) return RedirectToAction(nameof(Report), new { id });
        return View(new HostViewModel { Game = g, Join = BuildJoin(g.Pin) });
    }

    /// <summary>Host səhifəsi üçün qoşulma ünvanı (IP seçimi dəyişəndə).</summary>
    [HttpGet]
    public async Task<IActionResult> JoinInfo(Guid id, string? baseUrl)
    {
        var g = await _games.GetHostGameAsync(id, User.GetUserId());
        return g == null ? NotFound() : Json(BuildJoin(g.Pin, baseUrl));
    }

    [HttpGet]
    public async Task<IActionResult> Challenge(Guid id)
    {
        var g = await _games.GetHostGameAsync(id, User.GetUserId());
        if (g == null) return NotFound();
        if (g.Mode != GameMode.Challenge) return RedirectToAction(nameof(Host), new { id });
        return View(new ChallengeManageViewModel
        {
            Game = g, Join = BuildJoin(g.Pin), Board = await _games.GetChallengeLeaderboardAsync(id, User.GetUserId())
        });
    }

    [HttpGet]
    public async Task<IActionResult> ChallengeBoard(Guid id) =>
        Json(await _games.GetChallengeLeaderboardAsync(id, User.GetUserId()));

    [HttpPost]
    public async Task<IActionResult> CloseChallenge(Guid id)
    {
        var r = await _games.CloseChallengeAsync(id, User.GetUserId());
        TempData[r.Success ? "Success" : "Error"] = r.Success ? "Tapşırıq bağlandı. Nəticələr hesabatdadır." : r.FirstError;
        return r.Success ? RedirectToAction(nameof(Report), new { id }) : RedirectToAction(nameof(Challenge), new { id });
    }

    // ---------- Hesabatlar ----------

    [HttpGet]
    public async Task<IActionResult> History(int page = 1) =>
        View(new HistoryViewModel { Result = await _games.ListHostGamesAsync(User.GetUserId(), page) });

    [HttpGet]
    public async Task<IActionResult> Report(Guid id)
    {
        var r = await _reports.GetReportAsync(id, User.GetUserId());
        return r == null ? NotFound() : View(r);
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(Guid id)
    {
        var bytes = await _reports.ExportExcelAsync(id, User.GetUserId());
        return bytes == null ? NotFound() : File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"hesabat-{id.ToString()[..8]}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportPdf(Guid id)
    {
        var bytes = await _reports.ExportPdfAsync(id, User.GetUserId());
        return bytes == null ? NotFound() : File(bytes, "application/pdf", $"hesabat-{id.ToString()[..8]}.pdf");
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id)
    {
        var r = await _games.DeleteAsync(id, User.GetUserId());
        TempData[r.Success ? "Success" : "Error"] = r.Success ? "Hesabat silindi." : r.FirstError;
        return RedirectToAction(nameof(History));
    }
}

/// <summary>Oyunçu tərəfi: heç bir hesab tələb olunmur.</summary>
public class PlayController : Controller
{
    private readonly IGameService _games;
    public PlayController(IGameService games) => _games = games;

    [HttpGet("join")]
    public async Task<IActionResult> Join(string? pin)
    {
        if (!string.IsNullOrWhiteSpace(pin))
        {
            var r = await _games.ResolvePinAsync(pin);
            if (r.Kind == PinKind.Live) return Redirect($"/play/{r.Pin}");
            if (r.Kind == PinKind.Challenge) return Redirect($"/c/{r.Pin}");
            return View(new JoinViewModel { Pin = pin, Error = r.Message });
        }
        return View(new JoinViewModel());
    }

    [HttpPost("join"), EnableRateLimiting("join")]
    public async Task<IActionResult> JoinPost(string? pin)
    {
        var r = await _games.ResolvePinAsync(pin ?? string.Empty);
        return r.Kind switch
        {
            PinKind.Live => Redirect($"/play/{r.Pin}"),
            PinKind.Challenge => Redirect($"/c/{r.Pin}"),
            _ => View("Join", new JoinViewModel { Pin = pin, Error = r.Message ?? "Bu PIN ilə oyun tapılmadı." })
        };
    }

    [HttpGet("play/{pin}")]
    public async Task<IActionResult> Live(string pin)
    {
        var r = await _games.ResolvePinAsync(pin);
        if (r.Kind == PinKind.Challenge) return Redirect($"/c/{r.Pin}");
        if (r.Kind != PinKind.Live) return View("Join", new JoinViewModel { Pin = pin, Error = r.Message });
        return View(new PlayViewModel { Pin = r.Pin, QuizTitle = r.QuizTitle, AllowPhotoAvatars = r.AllowPhotoAvatars });
    }

    [HttpGet("c/{pin}")]
    public async Task<IActionResult> ChallengePlay(string pin)
    {
        var r = await _games.ResolvePinAsync(pin);
        if (r.Kind == PinKind.Live) return Redirect($"/play/{r.Pin}");
        if (r.Kind != PinKind.Challenge) return View("Join", new JoinViewModel { Pin = pin, Error = r.Message });
        return View(new PlayViewModel { Pin = r.Pin, QuizTitle = r.QuizTitle });
    }
}

/// <summary>
/// Oyunçunun avatar şəkli: hesab yoxdur, oyunçu gizli açarı (token) ilə tanınır. Brauzer şəkli əvvəlcədən 160×160 JPEG-ə
/// kiçildir, server isə yenə də real JPEG olduğunu, ölçüsünü və 80 KB-dan böyük olmadığını yoxlayır.
/// </summary>
[Route("play/avatar"), IgnoreAntiforgeryToken]
public class AvatarController : Controller
{
    private readonly ILiveGameEngine _engine;
    private readonly IAvatarService _avatars;
    public AvatarController(ILiveGameEngine engine, IAvatarService avatars) { _engine = engine; _avatars = avatars; }

    [HttpPost(""), EnableRateLimiting("avatars"), RequestSizeLimit(200_000)]
    public async Task<IActionResult> Upload([FromForm] Guid gameId, [FromForm] Guid playerId, [FromForm] Guid token, IFormFile? file)
    {
        var can = await _engine.CanUploadPhotoAsync(gameId, playerId, token);
        if (!can.Ok) return Ok(new { ok = false, error = can.Error });
        if (file == null || file.Length == 0) return Ok(new { ok = false, error = "Şəkil seçilməyib." });

        await using var s = file.OpenReadStream();
        var saved = await _avatars.SavePhotoAsync(s, file.Length, HttpContext.RequestAborted);
        if (!saved.Success) return Ok(new { ok = false, error = saved.FirstError });

        var set = await _engine.SetPhotoAvatarAsync(gameId, playerId, token, saved.Value!);
        if (!set.Ok)
        {
            _avatars.DeletePhotos(new[] { saved.Value });   // yüklənib, amma təyin olunmadı: faylı saxlamırıq
            return Ok(new { ok = false, error = set.Error });
        }
        return Ok(new { ok = true, avatar = saved.Value });
    }
}

public class ChallengeJoinRequest
{
    public string Pin { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
}

public class ChallengeTokenRequest
{
    public Guid Token { get; set; }
}

public class ChallengeAnswerRequest
{
    public Guid Token { get; set; }
    public AnswerSubmission Answer { get; set; } = new();
}

/// <summary>Tapşırıq rejimi üçün sadə HTTP API (oyunçu gizli açar ilə tanınır).</summary>
[ApiController, Route("api/challenge"), IgnoreAntiforgeryToken]
public class ChallengeApiController : ControllerBase
{
    private readonly IChallengeService _challenge;
    public ChallengeApiController(IChallengeService challenge) => _challenge = challenge;

    [HttpPost("join"), EnableRateLimiting("join")]
    public async Task<IActionResult> Join(ChallengeJoinRequest request) =>
        Ok(await _challenge.JoinAsync(request.Pin, request.Nickname));

    [HttpPost("state")]
    public async Task<IActionResult> State(ChallengeTokenRequest request)
    {
        var s = await _challenge.GetStateAsync(request.Token);
        return s == null ? NotFound(new { error = "Oyunçu tapılmadı." }) : Ok(s);
    }

    [HttpPost("answer")]
    public async Task<IActionResult> Answer(ChallengeAnswerRequest request) =>
        Ok(await _challenge.AnswerAsync(request.Token, request.Answer));
}

[Route("qr")]
public class QrController : Controller
{
    private readonly IQrService _qr;
    public QrController(IQrService qr) => _qr = qr;

    [HttpGet("")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult Get(string data)
    {
        if (string.IsNullOrWhiteSpace(data) || data.Length > 300 ||
            !(data.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || data.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            return BadRequest();
        return Content(_qr.GenerateSvg(data), "image/svg+xml");
    }
}
