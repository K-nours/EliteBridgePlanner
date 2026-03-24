using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EliteBridgePlanner.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;

    public UserController(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>Récupère le profil de l'utilisateur actuel</summary>
    [HttpGet("profile")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetProfile()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        return Ok(new UserProfileDto(
            user.Email!,
            user.CommanderName,
            user.PreferredLanguage,
            user.PreferredTimeZone,
            user.CreatedAt
        ));
    }

    /// <summary>Met à jour les préférences de l'utilisateur (langue, timezone)</summary>
    [HttpPut("preferences")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdateUserPreferencesRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        // Validation : vérifier que la langue est autorisée
        var allowedLanguages = new[] { "en-GB", "fr-FR" };
        if (request.PreferredLanguage is not null && !allowedLanguages.Contains(request.PreferredLanguage))
            return BadRequest(new { message = "Language not supported" });

        // Validation : vérifier que la timezone est valide
        if (request.PreferredTimeZone is not null)
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(request.PreferredTimeZone);
            }
            catch
            {
                return BadRequest(new { message = "Invalid time zone" });
            }
        }

        if (request.PreferredLanguage is not null)
            user.PreferredLanguage = request.PreferredLanguage;

        if (request.PreferredTimeZone is not null)
            user.PreferredTimeZone = request.PreferredTimeZone;

        await _userManager.UpdateAsync(user);

        return Ok(new UserProfileDto(
            user.Email!,
            user.CommanderName,
            user.PreferredLanguage,
            user.PreferredTimeZone,
            user.CreatedAt
        ));
    }
}
