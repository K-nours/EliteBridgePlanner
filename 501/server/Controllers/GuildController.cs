using GuildDashboard.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace GuildDashboard.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GuildController : ControllerBase
{
    private readonly GuildSystemsService _service;
    private readonly DashboardService _dashboard;

    public GuildController(GuildSystemsService service, DashboardService dashboard)
    {
        _service = service;
        _dashboard = dashboard;
    }

    /// <summary>GET /api/guild/systems?guildId=1</summary>
    [HttpGet("systems")]
    public async Task<IActionResult> GetSystems([FromQuery] int guildId = 1)
    {
        var result = await _service.GetSystemsAsync(guildId);
        return Ok(result);
    }

    /// <summary>GET /api/guild/dashboard?commanderName=Bib0xkn0x&guildId=1</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard([FromQuery] string? commanderName, [FromQuery] int guildId = 1, CancellationToken ct = default)
    {
        var result = await _dashboard.GetDashboardAsync(commanderName, guildId, ct);
        return Ok(result);
    }

}
