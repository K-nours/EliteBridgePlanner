using GuildDashboard.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace GuildDashboard.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GuildController : ControllerBase
{
    private readonly GuildSystemsService _service;

    public GuildController(GuildSystemsService service) => _service = service;

    /// <summary>GET /api/guild/systems?guildId=1</summary>
    [HttpGet("systems")]
    public async Task<IActionResult> GetSystems([FromQuery] int guildId = 1)
    {
        var result = await _service.GetSystemsAsync(guildId);
        return Ok(result);
    }
}
