using GuildDashboard.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace GuildDashboard.Server.Controllers;

[ApiController]
[Route("api/frontier/journal")]
public class FrontierJournalController : ControllerBase
{
    private readonly FrontierJournalBackfillService _backfill;

    public FrontierJournalController(FrontierJournalBackfillService backfill)
    {
        _backfill = backfill;
    }

    /// <summary>POST /api/frontier/journal/backfill/start — démarre ou reprend le backfill journal.</summary>
    [HttpPost("backfill/start")]
    public IActionResult StartBackfill()
    {
        var started = _backfill.Start();
        if (!started)
            return BadRequest(new { success = false, message = "Backfill déjà en cours ou token Frontier absent." });
        return Ok(new { success = true, message = "Backfill démarré." });
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
}
