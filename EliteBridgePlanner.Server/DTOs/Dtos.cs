using System.ComponentModel.DataAnnotations;

namespace EliteBridgePlanner.Server.DTOs;

// ── Auth ──────────────────────────────────────────────────────────────────

public record LoginRequest(
    [Required][EmailAddress] string Email,
    [Required] string Password
);

public record RegisterRequest(
    [Required][EmailAddress] string Email,
    [Required][MinLength(3)][MaxLength(50)] string CommanderName,
    [Required][MinLength(8)] string Password
);

public record AuthResponse(
    string Token,
    string CommanderName,
    string Email,
    string PreferredLanguage,
    string PreferredTimeZone,
    DateTime ExpiresAt
);

// ── Bridge ────────────────────────────────────────────────────────────────

public record BridgeDto(
    int Id,
    string Name,
    string? Description,
    string? CreatedByName,
    IEnumerable<StarSystemDto> Systems,
    int CompletionPercent,
    DateTime CreatedAt
);

public record CreateBridgeRequest(
    [Required][MaxLength(200)] string Name,
    [MaxLength(1000)] string? Description
);

// ── StarSystem ────────────────────────────────────────────────────────────

public record StarSystemDto(
    int Id,
    string Name,
    string Type,
    string Status,
    int Order, // Calculé à la volée par le service, non stocké
    string? PreviousSystemId,    
    string? ArchitectId,
    string? ArchitectName,
    int BridgeId,
    DateTime UpdatedAt
);

public record CreateSystemRequest(
    [Required][MaxLength(200)] string Name,
    [Required] string Type,
    [Required] string Status,
    [Range(1, int.MaxValue)] int InsertAtIndex,  // 1 = en tête, 2 = après le 1er, etc.
    string? ArchitectId,
    int BridgeId
);

public record UpdateSystemRequest(
    [MaxLength(200)] string? Name,
    string? Type,
    string? Status,
    string? ArchitectId           // "" = remettre à Inconnu (null)
);

public record MoveSystemRequest(
    [Range(1, int.MaxValue)] int InsertAtIndex  // même logique que CreateSystemRequest
);

// ── User ──────────────────────────────────────────────────────────────────

public record UserProfileDto(
    string Email,
    string CommanderName,
    string PreferredLanguage,
    string PreferredTimeZone,
    DateTime CreatedAt
);

public record UpdateUserPreferencesRequest(
    [MaxLength(10)] string? PreferredLanguage = null,
    [MaxLength(100)] string? PreferredTimeZone = null
);