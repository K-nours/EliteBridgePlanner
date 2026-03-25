using GuildDashboard.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace GuildDashboard.Server.Controllers;

[ApiController]
[Route("api/frontier/journal")]
public class FrontierJournalController : ControllerBase
{
    private readonly FrontierJournalBackfillService _backfill;
    private readonly FrontierJournalParseService _parse;

    public FrontierJournalController(FrontierJournalBackfillService backfill, FrontierJournalParseService parse)
    {
        _backfill = backfill;
        _parse = parse;
    }

    /// <summary>POST /api/frontier/journal/backfill/start — démarre ou reprend le backfill journal. Query recentDays (1–366) = seulement ces derniers jours UTC depuis hier.</summary>
    [HttpPost("backfill/start")]
    public IActionResult StartBackfill([FromQuery] int? recentDays = null)
    {
        if (recentDays is < 1 or > 366)
            return BadRequest(new { success = false, message = "recentDays doit être entre 1 et 366." });

        var started = _backfill.Start(recentDays);
        if (!started)
            return BadRequest(new { success = false, message = "Backfill déjà en cours ou token Frontier absent." });
        var hint = recentDays is >= 1 and <= 366 ? $" Fenêtre {recentDays} jour(s)." : "";
        return Ok(new { success = true, message = "Backfill démarré." + hint });
    }

    /// <summary>POST /api/frontier/journal/backfill/stop — arrête le backfill ou retry en cours.</summary>
    [HttpPost("backfill/stop")]
    public IActionResult StopBackfill()
    {
        var stopped = _backfill.Stop();
        return Ok(new { success = stopped, message = stopped ? "Backfill arrêté." : "Aucun backfill en cours." });
    }

    /// <summary>GET /api/frontier/journal/backfill/status — état du backfill.</summary>
    [HttpGet("backfill/status")]
    public IActionResult GetStatus()
    {
        var status = _backfill.GetStatus();
        return Ok(status);
    }

    /// <summary>GET /api/frontier/journal/backfill/retry-errors — nombre d'erreurs 401 à retraiter.</summary>
    [HttpGet("backfill/retry-errors")]
    public IActionResult GetRetryErrorsCount()
    {
        var dates = _backfill.GetDatesToRetry();
        return Ok(new { count = dates.Count, datesToRetry = dates });
    }

    /// <summary>POST /api/frontier/journal/backfill/retry-errors — lance le retry uniquement sur les erreurs 401.</summary>
    [HttpPost("backfill/retry-errors")]
    public IActionResult StartRetryErrors()
    {
        var started = _backfill.StartRetryErrors();
        if (!started)
            return BadRequest(new { success = false, message = "Retry impossible : déjà en cours, aucune erreur 401, ou token Frontier absent. Reconnectez-vous puis réessayez." });
        return Ok(new { success = true, message = "Retry des erreurs 401 démarré." });
    }

    /// <summary>POST /api/frontier/journal/parse/incremental — parse un lot de jours (non bloquant côté client si lot court).</summary>
    [HttpPost("parse/incremental")]
    public IActionResult StartIncrementalParse([FromQuery] int batchSize = 40)
    {
        var started = _parse.StartIncrementalParse(batchSize);
        if (!started)
            return BadRequest(new { success = false, message = "Parsing déjà en cours." });
        return Ok(new { success = true, message = "Parsing incrémental démarré." });
    }

    /// <summary>GET /api/frontier/journal/parse/status — état du parsing dérivé.</summary>
    [HttpGet("parse/status")]
    public IActionResult GetParseStatus()
    {
        return Ok(_parse.GetParseStatus());
    }

    /// <summary>GET /api/frontier/journal/derived/systems — agrégats par système pour la carte.</summary>
    [HttpGet("derived/systems")]
    public IActionResult GetDerivedSystems()
    {
        return Ok(_parse.GetDerivedForMap());
    }
}
