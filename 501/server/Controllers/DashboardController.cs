using GuildDashboard.Server.DTOs;
using GuildDashboard.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace GuildDashboard.Server.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly CommandersService _commanders;

    public DashboardController(CommandersService commanders) => _commanders = commanders;

    /// <summary>GET /api/dashboard/commanders?guildId=1</summary>
    [HttpGet("commanders")]
    public async Task<IActionResult> GetCommanders([FromQuery] int guildId = 1, CancellationToken ct = default)
    {
        var result = await _commanders.GetCommandersAsync(guildId, ct);
        return Ok(result);
    }
}
