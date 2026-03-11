using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EliteBridgePlanner.Server.Auth;
using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EliteBridgePlanner.Server.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly JwtConfig _jwt;

    // Toutes les dépendances injectées — jamais de new dans le constructeur
    public AuthService(UserManager<AppUser> userManager, IOptions<JwtConfig> jwtOptions)
    {
        _userManager = userManager;
        _jwt = jwtOptions.Value;
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null) return null;

        var passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordValid) return null;

        return BuildTokenResponse(user);
    }

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request)
    {
        var user = new AppUser
        {
            Email = request.Email,
            UserName = request.Email,
            CommanderName = request.CommanderName
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded) return null;

        return BuildTokenResponse(user);
    }

    private AuthResponse BuildTokenResponse(AppUser user)
    {
        var expiry = DateTime.UtcNow.AddDays(_jwt.ExpirationDays);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim("commander_name", user.CommanderName),
            // Claim "sub" compatible avec la structure Frontier SSO pour migration facile
            new Claim(JwtRegisteredClaimNames.Sub, user.Id)
        };

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: expiry,
            signingCredentials: creds
        );

        return new AuthResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            user.CommanderName,
            user.Email!,
            user.PreferredLanguage,
            user.PreferredTimeZone,
            expiry
        );
    }
}
