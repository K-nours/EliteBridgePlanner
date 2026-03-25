using GuildDashboard.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace GuildDashboard.Server.Controllers;

/// <summary>
/// Test et futur envoi du journal local vers EDSM (API Journal v1).
/// </summary>
[ApiController]
[Route("api/edsm/journal")]
public class EdsmJournalController : ControllerBase
{
    private readonly EdsmJournalUploadService _upload;
    private readonly EdsmJournalSettingsStore _edsmSettings;
    private readonly IConfiguration _config;

    public EdsmJournalController(
        EdsmJournalUploadService upload,
        EdsmJournalSettingsStore edsmSettings,
        IConfiguration config)
    {
        _upload = upload;
        _edsmSettings = edsmSettings;
        _config = config;
    }

    /// <summary>GET /api/edsm/journal/settings — nom CMDR affiché et si une clé API est configurée (fichier local ou appsettings).</summary>
    [HttpGet("settings")]
    public ActionResult<EdsmJournalClientSettingsDto> GetJournalSettings()
    {
        var file = _edsmSettings.Load();
        var cfgCmd = _config["Edsm:CommanderName"];
        var commander = !string.IsNullOrWhiteSpace(file?.CommanderName)
            ? file!.CommanderName.Trim()
            : (cfgCmd?.Trim() ?? "");
        var apiKeyConfigured =
            !string.IsNullOrWhiteSpace(file?.ApiKey) ||
            !string.IsNullOrWhiteSpace(_config["Edsm:ApiKey"]);
        return Ok(new EdsmJournalClientSettingsDto
        {
            CommanderName = commander,
            ApiKeyConfigured = apiKeyConfigured,
        });
    }

    /// <summary>
    /// PUT /api/edsm/journal/settings — enregistre dans <c>Data/edsm-journal-user.json</c> (prioritaire sur appsettings).
    /// Omettre <c>apiKey</c> pour ne pas changer la clé déjà stockée.
    /// </summary>
    [HttpPut("settings")]
    public IActionResult PutJournalSettings([FromBody] EdsmJournalSettingsWriteDto? dto)
    {
        if (dto == null)
            return BadRequest(new { error = "Payload invalide" });
        var cur = _edsmSettings.Load() ?? new EdsmJournalUserSettingsFile();
        cur.CommanderName = dto.CommanderName?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(dto.ApiKey))
            cur.ApiKey = dto.ApiKey.Trim();
        _edsmSettings.Save(cur);
        return Ok(new { saved = true });
    }

    /// <summary>
    /// POST /api/edsm/journal/test-upload — envoie une ligne (FSDJump ou Location) du raw Frontier vers EDSM.
    /// Corps JSON optionnel : { "date": "2026-03-20", "systemName": "Sol" }. Sans date : dernier jour success du raw.
    /// Identifiants : Paramètres EDSM dans l’app ou Edsm:CommanderName / Edsm:ApiKey.
    /// </summary>
    [HttpPost("test-upload")]
    public async Task<ActionResult<EdsmJournalTestUploadResult>> TestUpload(
        [FromBody] EdsmJournalTestUploadRequest? body,
        CancellationToken ct)
    {
        var result = await _upload.TestUploadOneFromFrontierRawAsync(body?.Date, body?.SystemName, ct);
        if (!string.IsNullOrEmpty(result.Error) && result.MsgNum == null && result.HttpStatus == null)
            return BadRequest(result);
        return Ok(result);
    }
}

public sealed class EdsmJournalTestUploadRequest
{
    public string? Date { get; set; }
    public string? SystemName { get; set; }
}

public sealed class EdsmJournalClientSettingsDto
{
    public string CommanderName { get; set; } = "";
    public bool ApiKeyConfigured { get; set; }
}

public sealed class EdsmJournalSettingsWriteDto
{
    public string? CommanderName { get; set; }
    public string? ApiKey { get; set; }
}
