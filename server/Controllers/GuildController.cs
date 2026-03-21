using GuildDashboard.Server.Data;
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
    private readonly CurrentGuildService _currentGuild;
    private readonly GuildDashboardDbContext _db;

    public GuildController(GuildSystemsService service, DashboardService dashboard, BgsSyncService bgsSync, CurrentGuildService currentGuild, GuildDashboardDbContext db)
    {
        _service = service;
        _dashboard = dashboard;
        _bgsSync = bgsSync;
        _currentGuild = currentGuild;
        _db = db;
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

    private int ResolveGuildId(int? guildId) => guildId is > 0 ? guildId.Value : _currentGuild.CurrentGuildId;

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
