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
    private readonly CurrentGuildService _currentGuild;

    public SyncController(SquadronSyncService sync, InaraClient inara, InaraSquadronRosterService roster, GuildDashboardDbContext db, CurrentGuildService currentGuild)
    {
        _sync = sync;
        _inara = inara;
        _roster = roster;
        _db = db;
        _currentGuild = currentGuild;
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
