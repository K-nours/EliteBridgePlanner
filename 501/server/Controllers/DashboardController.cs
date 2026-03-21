using GuildDashboard.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace GuildDashboard.Server.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly CommandersService _commanders;
    private readonly CurrentGuildService _currentGuild;

    public DashboardController(CommandersService commanders, CurrentGuildService currentGuild)
    {
        _commanders = commanders;
        _currentGuild = currentGuild;
    }

    /// <summary>GET /api/dashboard/commanders — guildId optionnel, utilise Guild:CurrentGuildId si absent.</summary>
    [HttpGet("commanders")]
    public async Task<IActionResult> GetCommanders([FromQuery] int? guildId, CancellationToken ct = default)
    {
        var id = guildId is > 0 ? guildId.Value : _currentGuild.CurrentGuildId;
        var result = await _commanders.GetCommandersAsync(id, ct);
        return Ok(result);
    }
}
