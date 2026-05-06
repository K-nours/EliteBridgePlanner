using GuildDashboard.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace GuildDashboard.Server.Controllers;

[ApiController]
[Route("api/frontier/journal")]
public class FrontierJournalController : ControllerBase
{
    private readonly FrontierJournalUnifiedSyncService _unified;
    private readonly FrontierJournalParseService _parse;
    private readonly FrontierJournalImportExportService _importExport;

    public FrontierJournalController(
        FrontierJournalUnifiedSyncService unified,
        FrontierJournalParseService parse,
        FrontierJournalImportExportService importExport)
    {
        _unified = unified;
        _parse = parse;
        _importExport = importExport;
    }

    /// <summary>GET /api/frontier/journal/export — archive ZIP du journal local du CMDR connecté.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportJournal(CancellationToken ct)
    {
        var pack = await _importExport.ExportAsync(ct);
        if (pack == null)
            return BadRequest(new { message = "Aucun profil Frontier : impossible d’exporter le journal." });
        return File(pack.Value.ZipBytes, "application/zip", pack.Value.DownloadFileName);
    }

    /// <summary>
    /// POST /api/frontier/journal/import — multipart : file, strategy=replace|merge, duplicatePolicy=skip|import (fusion seulement).
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(512 * 1024 * 1024)]
    public async Task<IActionResult> ImportJournal(
        [FromForm] IFormFile? file,
        [FromForm] string? strategy,
        [FromForm] string? duplicatePolicy,
        CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new FrontierJournalImportResultDto { Success = false, Message = "Fichier manquant." });
        await using var stream = file.OpenReadStream();
        var result = await _importExport.ImportAsync(stream, strategy ?? "replace", duplicatePolicy ?? "skip", ct);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    /// <summary>POST /api/frontier/journal/sync — fetch incrémental + parsing (une seule entrée métier).</summary>
    [HttpPost("sync")]
    public IActionResult StartSync()
    {
        var started = _unified.Start();
        if (!started)
            return BadRequest(new { success = false, message = "Synchronisation journal déjà en cours." });
        return Ok(new { success = true, message = "Journal Frontier : synchronisation démarrée." });
    }

    [HttpPost("sync/stop")]
    public IActionResult StopSync()
    {
        var stopped = _unified.Stop();
        return Ok(new { success = stopped, message = stopped ? "Journal Frontier : arrêt demandé." : "Aucune synchro en cours." });
    }

    [HttpGet("sync/status")]
    public IActionResult GetSyncStatus() => Ok(_unified.GetStatusSnapshot());

    /// <summary>Agrégats carte pour le CMDR actuellement identifié (profil Frontier).</summary>
    [HttpGet("derived/systems")]
    public async Task<IActionResult> GetDerivedSystems([FromServices] FrontierUserService users, CancellationToken ct)
    {
        var profile = await users.GetProfileAsync(ct);
        if (profile == null || string.IsNullOrEmpty(profile.FrontierCustomerId))
            return Ok(new FrontierJournalDerivedResponseDto());
        return Ok(_parse.GetDerivedForMap(profile.FrontierCustomerId));
    }

    /// <summary>Statut détaillé du parsing (jours restants, systèmes) pour le CMDR courant.</summary>
    [HttpGet("parse/status")]
    public async Task<IActionResult> GetParseStatus([FromServices] FrontierUserService users, CancellationToken ct)
    {
        var profile = await users.GetProfileAsync(ct);
        if (profile == null || string.IsNullOrEmpty(profile.FrontierCustomerId))
            return Ok(new FrontierJournalParseStatusDto());
        return Ok(_parse.GetParseStatus(profile.FrontierCustomerId));
    }
}
