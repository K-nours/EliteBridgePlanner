using GuildDashboard.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace GuildDashboard.Server.Controllers;

[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly FrontierUserService _userService;

    public UserController(FrontierUserService userService)
    {
        _userService = userService;
    }

    /// <summary>GET /api/user/me — utilisateur courant (Frontier). commander, squadron, guildId.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var user = await _userService.GetCurrentUserAsync(ct);
        if (user == null)
            return Ok(new { connected = false });
        return Ok(new
        {
            connected = true,
            commander = user.Commander,
            squadron = user.Squadron,
            guildId = user.GuildId,
            guildName = user.GuildName,
            customerId = user.CustomerId,
        });
    }
}
