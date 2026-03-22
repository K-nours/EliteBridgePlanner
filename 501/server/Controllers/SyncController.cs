using GuildDashboard.Server.Data;
using GuildDashboard.Server.DTOs;
using GuildDashboard.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Controllers;

[ApiController]
[Route("api/sync")]
public class SyncController : ControllerBase
{
    private readonly SquadronSyncService _sync;
    private readonly InaraClient _inara;
    private readonly InaraSquadronRosterService _roster;
    private readonly GuildDashboardDbContext _db;
    private readonly CurrentGuildService _currentGuild;
    private readonly ILogger<SyncController> _logger;

    public SyncController(SquadronSyncService sync, InaraClient inara, InaraSquadronRosterService roster, GuildDashboardDbContext db, CurrentGuildService currentGuild, ILogger<SyncController> logger)
    {
        _sync = sync;
        _inara = inara;
        _roster = roster;
        _db = db;
        _currentGuild = currentGuild;
        _logger = logger;
    }

    /// <summary>GET /api/sync/ping — test de connectivité (certificat / CORS). À ouvrir dans un nouvel onglet depuis Inara.</summary>
    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { ok = true, message = "Backend joignable", timestamp = DateTime.UtcNow.ToString("o") });

    /// <summary>OPTIONS pour preflight CORS (commanders/import).</summary>
    [HttpOptions("inara/commanders/import")]
    public IActionResult ImportCommandersOptions()
    {
        Response.Headers.Append("Access-Control-Allow-Origin", "*");
        Response.Headers.Append("Access-Control-Allow-Methods", "POST, OPTIONS");
        Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type");
        return NoContent();
    }

    /// <summary>OPTIONS pour preflight CORS (avatar).</summary>
    [HttpOptions("inara/avatar")]
    public IActionResult ImportAvatarOptions()
    {
        Response.Headers.Append("Access-Control-Allow-Origin", "*");
        Response.Headers.Append("Access-Control-Allow-Methods", "POST, OPTIONS");
        Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type");
        return NoContent();
    }

    /// <summary>POST /api/sync/inara/commanders — sync roster Inara. guildId optionnel.</summary>
    [HttpPost("inara/commanders")]
    public async Task<IActionResult> SyncInaraCommanders([FromQuery] int? guildId, CancellationToken ct = default)
    {
        var id = guildId is > 0 ? guildId.Value : _currentGuild.CurrentGuildId;
        var result = await _sync.SyncAsync(id, ct);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error, syncedCount = 0 });

        return Ok(new { syncedCount = result.SyncedCount });
    }

    /// <summary>POST /api/sync/inara/commanders/import — importe les CMDRs extraits par le userscript (page roster Inara).</summary>
    [HttpPost("inara/commanders/import")]
    public async Task<IActionResult> ImportCommanders(
        [FromBody] CommandersImportPayload? payload,
        [FromQuery] int? guildId,
        CancellationToken ct = default)
    {
        // Log dès réception pour confirmer que la requête arrive (diagnostic CORS/certificat)
        _logger.LogWarning("[Sync] POST commanders/import RECEIVED — ContentLength={Len}", Request.ContentLength);
        var id = guildId is > 0 ? guildId.Value : _currentGuild.CurrentGuildId;
        if (payload == null)
            return BadRequest(new { error = "Payload invalide (JSON requis)", imported = 0, totalReceived = 0 });
        if (payload.Commanders == null)
            return BadRequest(new { error = "Payload.commanders manquant ou invalide (attendu: { commanders: [{ name, role? }] })", imported = 0, totalReceived = 0 });

        var result = await _sync.ImportFromPayloadAsync(id, payload, ct);
        if (result.Error != null)
            return BadRequest(new { error = result.Error, imported = 0, totalReceived = 0, skipped = 0, processedNames = Array.Empty<string>() });

        return Ok(new
        {
            imported = result.Imported,
            totalReceived = result.TotalReceived,
            skipped = result.Skipped,
            processedNames = result.ProcessedNames
        });
    }

    /// <summary>POST /api/sync/inara/avatar — importe l'avatar extrait par le userscript (page CMDR Inara).</summary>
    [HttpPost("inara/avatar")]
    public async Task<IActionResult> ImportAvatar([FromBody] AvatarImportPayload? payload, [FromQuery] int? guildId, CancellationToken ct = default)
    {
        try
        {
            var id = guildId is > 0 ? guildId.Value : _currentGuild.CurrentGuildId;

            // 1. Logs réception
            var commanderNameReceived = payload?.CommanderName ?? "(null)";
            var avatarUrlReceived = payload?.AvatarUrl ?? "(null)";
            _logger.LogInformation("[Sync] POST avatar RECEIVED — commanderName={CommanderName} avatarUrl={AvatarUrl} guildId={GuildId}",
                commanderNameReceived, avatarUrlReceived, id);

            if (payload == null || string.IsNullOrWhiteSpace(payload.AvatarUrl))
                return BadRequest(new { error = "Payload invalide (avatarUrl requis)" });

            var name = (payload.CommanderName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { error = "commanderName requis pour identifier le CMDR" });

            // 2. Recherche en base (trim + case-insensitive)
            var members = await _db.SquadronMembers
                .Where(m => m.GuildId == id)
                .ToListAsync(ct);

            var member = members.FirstOrDefault(m =>
                string.Equals((m.CommanderName ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase));

            // 3. Log résultat recherche
            if (member != null)
                _logger.LogInformation("[Sync] avatar — CMDR trouvé: {CommanderName}", member.CommanderName);
            else
                _logger.LogWarning("[Sync] avatar — CMDR non trouvé: nameRecherché=\"{Name}\" nomsEnBase=[{Names}]",
                    name, string.Join(", ", members.Select(m => '"' + (m.CommanderName ?? "") + '"')));

            if (member == null)
                return NotFound(new { error = $"Commander not found: {name}" });

            member.AvatarUrl = payload.AvatarUrl.Trim();
            member.LastSyncedAt = DateTime.UtcNow;

            var guildEntity = await _db.Guilds.FindAsync(new object[] { id }, ct);
            if (guildEntity != null)
            {
                guildEntity.LastAvatarImportAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new { updated = true, commanderName = member.CommanderName });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Sync] avatar — EXCEPTION: {Message}{NewLine}{StackTrace}",
                ex.Message, Environment.NewLine, ex.StackTrace ?? "");
            throw;
        }
    }

    /// <summary>GET /api/sync/diagnostic/commanders-flow — diagnostic complet : DB vs GET commanders (même guildId).</summary>
    [HttpGet("diagnostic/commanders-flow")]
    public async Task<IActionResult> GetCommandersFlowDiagnostic([FromQuery] int? guildId, CancellationToken ct = default)
    {
        var id = guildId is > 0 ? guildId.Value : _currentGuild.CurrentGuildId;
        var dbMembers = await _db.SquadronMembers
            .AsNoTracking()
            .Where(m => m.GuildId == id)
            .OrderBy(m => m.CommanderName)
            .Select(m => new { m.CommanderName, m.Role, m.AvatarUrl })
            .ToListAsync(ct);

        var guild = await _db.Guilds.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        return Ok(new
        {
            guildId = id,
            guildName = guild?.Name,
            currentGuildIdConfig = _currentGuild.CurrentGuildId,
            dbSquadronMembersCount = dbMembers.Count,
            dbSquadronMembersNames = dbMembers.Select(m => m.CommanderName).ToList(),
            getCommandersEndpoint = $"GET /api/dashboard/commanders?guildId={id} renvoie les mêmes données (même filtre GuildId)",
            note = "Si dbSquadronMembersCount > 0 mais le bloc CMDRs affiche vide, vérifier que le frontend appelle bien /api/dashboard/commanders et que le proxy/URL est correct."
        });
    }

    /// <summary>GET /api/sync/diagnostic/squadron-members — liste brute des SquadronMembers en base (diagnostic).</summary>
    [HttpGet("diagnostic/squadron-members")]
    public async Task<IActionResult> GetSquadronMembersDiagnostic([FromQuery] int? guildId, CancellationToken ct = default)
    {
        var id = guildId is > 0 ? guildId.Value : _currentGuild.CurrentGuildId;
        var members = await _db.SquadronMembers
            .AsNoTracking()
            .Where(m => m.GuildId == id)
            .OrderBy(m => m.CommanderName)
            .Select(m => new { m.Id, m.CommanderName, m.Role, m.AvatarUrl, m.LastSyncedAt, m.GuildId })
            .ToListAsync(ct);

        var guild = await _db.Guilds.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        return Ok(new
        {
            guildId = id,
            guildName = guild?.Name,
            totalCount = members.Count,
            names = members.Select(m => m.CommanderName).ToList(),
            members
        });
    }

    /// <summary>GET /api/sync/inara/roster-diagnostic — diagnostic du roster Inara.</summary>
    [HttpGet("inara/roster-diagnostic")]
    public async Task<IActionResult> GetRosterDiagnostic([FromQuery] int? guildId, CancellationToken ct = default)
    {
        var id = guildId is > 0 ? guildId.Value : _currentGuild.CurrentGuildId;
        var guild = await _db.Guilds.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        var squadronId = await _inara.GetSquadronIdAsync(guild?.InaraSquadronId, guild?.InaraFactionId, ct);
        if (squadronId == null)
            return BadRequest(new { error = "Squadron:InaraSquadronId non configuré (config ou Guild.InaraSquadronId)" });

        var diagnostic = await _roster.GetRosterDiagnosticAsync(squadronId.Value, ct);
        return Ok(diagnostic);
    }
}
