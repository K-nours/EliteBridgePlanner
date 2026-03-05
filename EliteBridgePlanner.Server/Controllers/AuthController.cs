using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace EliteBridgePlanner.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    // IAuthService injecté — mockable dans les tests NUnit
    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>Connexion CMDR — retourne un JWT Bearer</summary>
    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _authService.LoginAsync(request);
        return result is null
            ? Unauthorized(new { message = "Email ou mot de passe invalide" })
            : Ok(result);
    }

    /// <summary>Inscription d'un nouveau CMDR</summary>
    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _authService.RegisterAsync(request);
        return result is null
            ? BadRequest(new { message = "Email déjà utilisé ou données invalides" })
            : CreatedAtAction(nameof(Login), result);
    }
}
