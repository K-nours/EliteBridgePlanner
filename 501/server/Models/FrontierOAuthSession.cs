namespace GuildDashboard.Server.Models;

/// <summary>
/// Session OAuth Frontier persistée (un seul enregistrement actif prévu pour cette app).
/// Les jetons sont stockés chiffrés (ASP.NET Data Protection), jamais en clair.
/// </summary>
public class FrontierOAuthSession
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Protections DP (non loggables).</summary>
    public byte[] AccessTokenProtected { get; set; } = Array.Empty<byte>();

    public byte[] RefreshTokenProtected { get; set; } = Array.Empty<byte>();

    public DateTime AccessTokenExpiresAtUtc { get; set; }

    public string TokenType { get; set; } = "Bearer";

    public string? Scope { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? LastRefreshAtUtc { get; set; }

    public bool IsActive { get; set; } = true;
}
