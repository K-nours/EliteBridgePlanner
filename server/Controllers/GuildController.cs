using GuildDashboard.Server.Data;
using GuildDashboard.Server.DTOs;
using GuildDashboard.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GuildController : ControllerBase
{
    private readonly GuildSystemsService _service;
    private readonly DashboardService _dashboard;
    private readonly BgsSyncService _bgsSync;
    private readonly EliteBgsDiagnosticService _eliteBgsDiagnostic;
    private readonly InaraFactionService _inaraFaction;
    private readonly CurrentGuildService _currentGuild;
    private readonly GuildDashboardDbContext _db;
    private readonly GuildSystemsImportService _importService;
    private readonly GuildSystemsSeedLoader _seedLoader;

    public GuildController(GuildSystemsService service, DashboardService dashboard, BgsSyncService bgsSync, EliteBgsDiagnosticService eliteBgsDiagnostic, InaraFactionService inaraFaction, CurrentGuildService currentGuild, GuildDashboardDbContext db, GuildSystemsImportService importService, GuildSystemsSeedLoader seedLoader)
    {
        _service = service;
        _dashboard = dashboard;
        _bgsSync = bgsSync;
        _eliteBgsDiagnostic = eliteBgsDiagnostic;
        _inaraFaction = inaraFaction;
        _currentGuild = currentGuild;
        _db = db;
        _importService = importService;
        _seedLoader = seedLoader;
    }

    /// <summary>GET /api/integrations/elitebgs/test — diagnostic Elite BGS. HTML si Accept:text/html, sinon JSON.</summary>
    [HttpGet("~/api/integrations/elitebgs/test")]
    public async Task<IActionResult> TestEliteBgs([FromQuery] string? factionName, CancellationToken ct = default)
    {
        var name = factionName ?? "The 501st Guild";
        var result = await _eliteBgsDiagnostic.TestAsync(name, ct);

        if (!Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            var html = BuildEliteBgsDiagnosticHtml(result, name, Request.Path);
            return Content(html, "text/html; charset=utf-8");
        }
        return Ok(result);
    }

    private static string BuildEliteBgsDiagnosticHtml(EliteBgsTestResult r, string factionName, string path)
    {
        var css = "body{font-family:system-ui;margin:0;padding:1rem;background:#1a1a2e;color:#e6edf3;}" +
            ".header{display:flex;justify-content:space-between;align-items:center;margin-bottom:1.5rem;flex-wrap:wrap;gap:1rem;}" +
            "h1{color:#6ee7b7;margin:0;} .btn{background:#6ee7b7;color:#1a1a2e;border:none;padding:0.5rem 1rem;border-radius:6px;cursor:pointer;font-size:1rem;font-weight:600;}" +
            ".btn:hover{background:#5dd6a6;} .card{background:#2d2d44;padding:1rem;border-radius:8px;margin-bottom:1rem;}" +
            "h2{color:#6ee7b7;margin:0 0 0.8rem 0;font-size:1rem;} p{margin:0.3rem 0;} .ok{color:#6ee7b7;} .err{color:#f87171;} .count{color:#93c5fd;}" +
            "pre{background:#1a1a2e;padding:0.5rem;border-radius:4px;overflow-x:auto;font-size:0.85em;} .form{display:flex;gap:0.5rem;align-items:center;flex-wrap:wrap;}" +
            "input[type=text]{padding:0.4rem 0.6rem;border-radius:4px;border:1px solid #444;background:#1a1a2e;color:#e6edf3;min-width:200px;}";
        var ok = string.IsNullOrEmpty(r.ErrorType);
        var statusClass = ok ? "ok" : "err";
        var statusText = ok ? "OK" : (r.ErrorType ?? "—");
        var preview = string.IsNullOrEmpty(r.ResponsePreview) ? "(vide)" : System.Net.WebUtility.HtmlEncode(r.ResponsePreview);
        var form = "<form class=\"form\" method=\"get\" action=\"" + System.Net.WebUtility.HtmlEncode(path) + "\">" +
            "<input type=\"text\" name=\"factionName\" value=\"" + System.Net.WebUtility.HtmlEncode(factionName) + "\" placeholder=\"Faction name\" />" +
            "<button type=\"submit\" class=\"btn\">Tester</button></form>";
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Elite BGS Diagnostic</title><style>" + css + "</style></head><body>" +
            "<div class=\"header\"><h1>Elite BGS Diagnostic</h1><button class=\"btn\" onclick=\"location.reload()\">Rafraîchir</button></div>" +
            "<div class=\"card\"><h2>Test faction</h2>" + form + "</div>" +
            "<div class=\"card\"><h2>Résultat</h2>" +
            "<p>Statut : <span class=\"" + statusClass + "\">" + System.Net.WebUtility.HtmlEncode(statusText) + "</span></p>" +
            "<p>URL : <a href=\"" + System.Net.WebUtility.HtmlEncode(r.Url) + "\" style=\"color:#93c5fd;\" target=\"_blank\">" + System.Net.WebUtility.HtmlEncode(r.Url) + "</a></p>" +
            "<p>Durée : <span class=\"count\">" + r.DurationMs + " ms</span> | Timeout : " + r.TimeoutSeconds + " s</p>" +
            "<p>HTTP : " + r.HttpStatusCode + " | Taille : " + r.ResponseLength + " bytes | JSON valide : " + (r.HasValidJson ? "Oui" : "Non") + " | docs : " + r.DocsCount + "</p>" +
            (string.IsNullOrEmpty(r.ErrorMessage) ? "" : "<p class=\"err\">Erreur : " + System.Net.WebUtility.HtmlEncode(r.ErrorMessage) + "</p>") +
            "<h2>Aperçu réponse</h2><pre>" + preview + "</pre></div>" +
            "</body></html>";
    }

    /// <summary>GET /api/integrations/inara/faction-presence/test — diagnostic Inara faction presence. HTML si Accept:text/html, sinon JSON.</summary>
    [HttpGet("~/api/integrations/inara/faction-presence/test")]
    public async Task<IActionResult> TestInaraFactionPresence([FromQuery] int? factionId, CancellationToken ct = default)
    {
        var id = factionId ?? await _db.Guilds.AsNoTracking()
            .Where(g => g.Name == "The 501st Guild")
            .Select(g => g.InaraFactionId)
            .FirstOrDefaultAsync(ct) ?? 78866;
        if (id <= 0)
            return BadRequest(new { error = "factionId requis ou guilde The 501st Guild non trouvée" });

        var presence = await _inaraFaction.GetFactionPresenceAsync(id, ct);
        var url = $"https://inara.cz/elite/minorfaction-presence/{id}/";

        var systemsList = presence?.Take(50).Select(p => (p.SystemName, p.InfluencePercent, p.LastUpdateText)).ToList() ?? new List<(string, decimal, string?)>();

        var result = new
        {
            url,
            factionId = id,
            systemsCount = presence?.Count ?? 0,
            systems = presence?.Take(50).Select(p => new { p.SystemName, p.InfluencePercent, p.LastUpdateText }).ToList(),
            success = presence != null && presence.Count > 0,
        };

        if (!Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            var html = BuildInaraDiagnosticHtml(result.url, result.factionId, result.success, result.systemsCount, systemsList, Request.Path.ToString());
            return Content(html, "text/html; charset=utf-8");
        }
        return Ok(result);
    }

    private static string BuildInaraDiagnosticHtml(string url, int factionId, bool success, int systemsCount, List<(string SystemName, decimal InfluencePercent, string? LastUpdateText)> systems, string path)
    {
        var css = "body{font-family:system-ui;margin:0;padding:1rem;background:#1a1a2e;color:#e6edf3;}" +
            ".header{display:flex;justify-content:space-between;align-items:center;margin-bottom:1.5rem;flex-wrap:wrap;gap:1rem;}" +
            "h1{color:#6ee7b7;margin:0;} .btn{background:#6ee7b7;color:#1a1a2e;border:none;padding:0.5rem 0.6rem;border-radius:6px;cursor:pointer;font-size:1rem;}" +
            ".card{background:#2d2d44;padding:1rem;border-radius:8px;margin-bottom:1rem;} h2{color:#6ee7b7;font-size:1rem;} .ok{color:#6ee7b7;} .err{color:#f87171;} .count{color:#93c5fd;}" +
            "table{border-collapse:collapse;width:100%;font-size:0.9em;} th,td{border:1px solid #444;padding:0.3rem 0.5rem;text-align:left;} th{background:#1a1a2e;}";
        var statusClass = success ? "ok" : "err";
        var statusText = success ? "OK" : "Échec";
        var form = "<form method=\"get\" action=\"" + System.Net.WebUtility.HtmlEncode(path) + "\" style=\"display:flex;gap:0.5rem;align-items:center;\">" +
            "<input type=\"number\" name=\"factionId\" value=\"" + factionId + "\" placeholder=\"Inara Faction ID\" style=\"padding:0.4rem;width:120px;\" />" +
            "<button type=\"submit\" class=\"btn\">Tester</button></form>";
        var rows = string.Join("", systems.Select(s =>
            "<tr><td>" + System.Net.WebUtility.HtmlEncode(s.SystemName) + "</td><td>" + s.InfluencePercent + "%</td><td>" + System.Net.WebUtility.HtmlEncode(s.LastUpdateText ?? "-") + "</td></tr>"));
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Inara Faction Presence Diagnostic</title><style>" + css + "</style></head><body>" +
            "<div class=\"header\"><h1>Inara Faction Presence Diagnostic</h1><button class=\"btn\" onclick=\"location.reload()\">Rafraîchir</button></div>" +
            "<div class=\"card\"><h2>Test</h2>" + form + "</div>" +
            "<div class=\"card\"><h2>Résultat</h2>" +
            "<p>Statut : <span class=\"" + statusClass + "\">" + statusText + "</span> | Systèmes : <span class=\"count\">" + systemsCount + "</span></p>" +
            "<p>URL : <a href=\"" + System.Net.WebUtility.HtmlEncode(url) + "\" style=\"color:#93c5fd;\" target=\"_blank\">" + System.Net.WebUtility.HtmlEncode(url) + "</a></p>" +
            (rows.Length > 0 ? "<h2>Extrait (50 premiers)</h2><table><tr><th>Système</th><th>Influence</th><th>Dernière MAJ</th></tr>" + rows + "</table>" : "") +
            "</div></body></html>";
    }

    /// <summary>GET /api/guild/diagnostic — liste les guildes en base + systèmes associés. Pour identifier l'ID réel de "The 501st Guild".</summary>
    [HttpGet("diagnostic")]
    public async Task<IActionResult> GetDiagnostic(CancellationToken ct = default)
    {
        var currentId = _currentGuild.CurrentGuildId;
        var guilds = await _db.Guilds.AsNoTracking().ToListAsync(ct);
        var guildSystemsCount = await _db.GuildSystems.AsNoTracking().GroupBy(s => s.GuildId).ToDictionaryAsync(g => g.Key, g => g.Count(), ct);
        var controlledCount = await _db.ControlledSystems.AsNoTracking().GroupBy(c => c.GuildId).ToDictionaryAsync(g => g.Key, g => g.Count(), ct);

        var result = guilds.Select(g => new
        {
            g.Id,
            g.Name,
            g.DisplayName,
            g.FactionName,
            g.InaraFactionId,
            guildSystemCount = guildSystemsCount.ContainsKey(g.Id) ? guildSystemsCount[g.Id] : 0,
            controlledSystemCount = controlledCount.ContainsKey(g.Id) ? controlledCount[g.Id] : 0,
        }).ToList();

        var the501st = result.FirstOrDefault(g => string.Equals(g.Name, "The 501st Guild", StringComparison.OrdinalIgnoreCase));
        object the501stGuild = the501st != null
            ? (object)new
            {
                found = true,
                id = the501st.Id,
                name = the501st.Name,
                factionName = the501st.FactionName,
                inaraFactionId = the501st.InaraFactionId,
                guildSystemCount = the501st.guildSystemCount,
                controlledSystemCount = the501st.controlledSystemCount,
            }
            : new { found = false, suggestion = "Exécuter les migrations et le seed : dotnet run (le DataSeeder crée 'The 501st Guild' avec Id=1)" };

        return Ok(new { currentGuildId = currentId, guilds = result, the501stGuild });
    }

    /// <summary>GET /api/guild/current — guilde courante (Guild:CurrentGuildId). Temporaire jusqu'à auth Frontier.</summary>
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken ct = default)
    {
        var id = _currentGuild.CurrentGuildId;
        var guild = await _db.Guilds.AsNoTracking()
            .Where(g => g.Id == id)
            .Select(g => new { id = g.Id, displayName = g.DisplayName ?? g.Name, factionName = g.FactionName ?? g.Name })
            .FirstOrDefaultAsync(ct);
        if (guild == null)
            return NotFound(new { error = $"Guild {id} introuvable" });
        return Ok(guild);
    }

    /// <summary>GET /api/guild/settings — URLs Inara et dates de dernière sync.</summary>
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings([FromQuery] int? guildId, CancellationToken ct = default)
    {
        var id = ResolveGuildId(guildId);
        var guild = await _db.Guilds.AsNoTracking()
            .Where(g => g.Id == id)
            .Select(g => new
            {
                g.InaraFactionPresenceUrl,
                g.InaraSquadronUrl,
                g.InaraCmdrUrl,
                g.LastSystemsImportAt,
            })
            .FirstOrDefaultAsync(ct);
        if (guild == null)
            return NotFound(new { error = $"Guild {id} introuvable" });

        var lastCommandersSyncAt = await _db.SquadronSnapshots.AsNoTracking()
            .Where(s => s.GuildId == id && s.Success)
            .OrderByDescending(s => s.LastSyncedAt)
            .Select(s => (DateTime?)s.LastSyncedAt)
            .FirstOrDefaultAsync(ct);

        return Ok(new
        {
            inaraFactionPresenceUrl = guild.InaraFactionPresenceUrl,
            inaraSquadronUrl = guild.InaraSquadronUrl,
            inaraCmdrUrl = guild.InaraCmdrUrl,
            lastSystemsImportAt = guild.LastSystemsImportAt,
            lastCommandersSyncAt,
        });
    }

    /// <summary>PUT /api/guild/settings — met à jour les URLs Inara.</summary>
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] GuildSettingsUpdateDto? dto, [FromQuery] int? guildId, CancellationToken ct = default)
    {
        var id = ResolveGuildId(guildId);
        if (dto == null)
            return BadRequest(new { error = "Payload invalide" });

        var factionUrl = string.IsNullOrWhiteSpace(dto.InaraFactionPresenceUrl) ? null : dto.InaraFactionPresenceUrl!.Trim();
        var squadronUrl = string.IsNullOrWhiteSpace(dto.InaraSquadronUrl) ? null : dto.InaraSquadronUrl!.Trim();
        var cmdrUrl = string.IsNullOrWhiteSpace(dto.InaraCmdrUrl) ? null : dto.InaraCmdrUrl!.Trim();

        var (factionOk, factionError) = ValidateInaraFactionPresenceUrl(factionUrl);
        if (!factionOk)
            return BadRequest(new { error = factionError });

        var (squadronOk, squadronError) = ValidateInaraSquadronUrl(squadronUrl);
        if (!squadronOk)
            return BadRequest(new { error = squadronError });

        var (cmdrOk, cmdrError) = ValidateInaraCmdrUrl(cmdrUrl);
        if (!cmdrOk)
            return BadRequest(new { error = cmdrError });

        var guild = await _db.Guilds.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (guild == null)
            return NotFound(new { error = $"Guild {id} introuvable" });

        guild.InaraFactionPresenceUrl = factionUrl;
        guild.InaraSquadronUrl = squadronUrl;
        guild.InaraCmdrUrl = cmdrUrl;

        await _db.SaveChangesAsync(ct);
        return Ok(new { inaraFactionPresenceUrl = guild.InaraFactionPresenceUrl, inaraSquadronUrl = guild.InaraSquadronUrl, inaraCmdrUrl = guild.InaraCmdrUrl });
    }

    private static (bool Ok, string? Error) ValidateInaraCmdrUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return (true, null);
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || !u.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return (false, "URL CMDR invalide");
        if (!u.Host.Contains("inara.cz", StringComparison.OrdinalIgnoreCase))
            return (false, "URL doit pointer vers inara.cz");
        if (!u.AbsolutePath.Contains("cmdr", StringComparison.OrdinalIgnoreCase))
            return (false, "URL doit être une page CMDR Inara");
        return (true, null);
    }

    private static (bool Ok, string? Error) ValidateInaraFactionPresenceUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return (true, null); // autoriser vide pour effacement
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || !u.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return (false, "URL faction systems invalide");
        if (!u.Host.Contains("inara.cz", StringComparison.OrdinalIgnoreCase))
            return (false, "URL doit pointer vers inara.cz");
        if (!u.AbsolutePath.Contains("minorfaction-presence", StringComparison.OrdinalIgnoreCase))
            return (false, "URL doit être une page présence faction (minorfaction-presence)");
        return (true, null);
    }

    private static (bool Ok, string? Error) ValidateInaraSquadronUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return (true, null);
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || !u.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return (false, "URL squadron invalide");
        if (!u.Host.Contains("inara.cz", StringComparison.OrdinalIgnoreCase))
            return (false, "URL doit pointer vers inara.cz");
        if (!u.AbsolutePath.Contains("squadron-roster", StringComparison.OrdinalIgnoreCase))
            return (false, "URL doit être la page liste pilotes (squadron-roster)");
        return (true, null);
    }

    private int ResolveGuildId(int? guildId) => guildId is > 0 ? guildId.Value : _currentGuild.CurrentGuildId;

    /// <summary>GET /api/guild/systems/verify — vérifie seed vs base (total attendu, total en base, manquants).</summary>
    [HttpGet("systems/verify")]
    public async Task<IActionResult> VerifySystemsSeed([FromQuery] int? guildId, CancellationToken ct = default)
    {
        var id = ResolveGuildId(guildId);
        var result = await _seedLoader.VerifyAsync(id, ct);
        return Ok(new { result.TotalExpected, result.TotalInDb, result.MissingNames, result.Error });
    }

    /// <summary>GET /api/guild/systems?guildId=...</summary>
    [HttpGet("systems")]
    public async Task<IActionResult> GetSystems([FromQuery] int? guildId)
    {
        var result = await _service.GetSystemsAsync(ResolveGuildId(guildId));
        return Ok(result);
    }

    /// <summary>POST /api/guild/systems/sync — synchronise les données BGS depuis Elite BGS API.</summary>
    [HttpPost("systems/sync")]
    public async Task<IActionResult> SyncBgs([FromQuery] int? guildId, CancellationToken ct = default)
    {
        var id = ResolveGuildId(guildId);
        var result = await _bgsSync.SyncAsync(id, ct);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.ErrorMessage });
        return Ok(new { updated = result.UpdatedCount });
    }

    /// <summary>POST /api/guild/systems/import — importe les systèmes depuis JSON (userscript Inara). Upsert idempotent.</summary>
    [HttpPost("systems/import")]
    public async Task<IActionResult> ImportSystems([FromBody] GuildSystemsImportPayload? payload, [FromQuery] int? guildId, CancellationToken ct = default)
    {
        var id = ResolveGuildId(guildId);
        if (payload == null)
            return BadRequest(new { error = "Payload invalide (JSON requis)" });

        var result = await _importService.ImportAsync(id, payload, ct);
        return Ok(new
        {
            totalReceived = result.TotalReceived,
            inserted = result.Inserted,
            updated = result.Updated,
            skipped = result.Skipped,
            totalProcessed = result.Inserted + result.Updated + result.Skipped,
            error = result.Error,
        });
    }

    /// <summary>POST /api/guild/systems/{systemId}/toggle-headquarter — toggle HQ (déclaratif).</summary>
    [HttpPost("systems/{systemId:int}/toggle-headquarter")]
    public async Task<IActionResult> ToggleHeadquarter(int systemId, [FromQuery] int? guildId, CancellationToken ct = default)
    {
        var ok = await _service.ToggleHeadquarterAsync(systemId, ResolveGuildId(guildId), ct);
        return ok ? Ok() : NotFound();
    }

    /// <summary>GET /api/guild/dashboard — commanderName optionnel.</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard([FromQuery] string? commanderName, [FromQuery] int? guildId, CancellationToken ct = default)
    {
        var result = await _dashboard.GetDashboardAsync(commanderName, ResolveGuildId(guildId), ct);
        return Ok(result);
    }

}
