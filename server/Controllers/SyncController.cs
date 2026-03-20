using GuildDashboard.Server.Data;
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

    public SyncController(SquadronSyncService sync, InaraClient inara, InaraSquadronRosterService roster, GuildDashboardDbContext db)
    {
        _sync = sync;
        _inara = inara;
        _roster = roster;
        _db = db;
    }

    /// <summary>POST /api/sync/inara/commanders — sync roster Inara vers cache DB. Utilise Squadron:InaraSquadronId (config statique temporaire).</summary>
    [HttpPost("inara/commanders")]
    public async Task<IActionResult> SyncInaraCommanders([FromQuery] int guildId = 1, CancellationToken ct = default)
    {
        var result = await _sync.SyncAsync(guildId, ct);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error, syncedCount = 0 });

        return Ok(new { syncedCount = result.SyncedCount });
    }

    /// <summary>GET /api/sync/inara/roster-diagnostic — diagnostic du roster Inara (anonyme vs clé API).</summary>
    [HttpGet("inara/roster-diagnostic")]
    public async Task<IActionResult> GetRosterDiagnostic([FromQuery] int guildId = 1, CancellationToken ct = default)
    {
        var guild = await _db.Guilds.AsNoTracking().FirstOrDefaultAsync(g => g.Id == guildId, ct);
        var squadronId = await _inara.GetSquadronIdAsync(guild?.InaraSquadronId, guild?.InaraFactionId, ct);
        if (squadronId == null)
            return BadRequest(new { error = "Squadron:InaraSquadronId non configuré (config ou Guild.InaraSquadronId)" });

        var diagnostic = await _roster.GetRosterDiagnosticAsync(squadronId.Value, ct);
        return Ok(diagnostic);
    }
}
