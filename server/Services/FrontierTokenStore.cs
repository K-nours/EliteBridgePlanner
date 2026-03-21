namespace GuildDashboard.Server.Services;

/// <summary>Tentative OAuth PKCE — stockée par state pour gérer plusieurs flux simultanés.</summary>
internal sealed class OAuthAttempt
{
    public required string State { get; init; }
    public required string CodeVerifier { get; init; }
    public DateTime CreatedAt { get; init; }
    public string Status { get; set; } = "pending"; // pending | used | expired
}

/// <summary>Stockage OAuth Frontier : tentatives par state, token courant, rapport. Non persistant.</summary>
public class FrontierTokenStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, OAuthAttempt> _attempts = new(StringComparer.Ordinal);
    private static readonly TimeSpan AttemptExpiration = TimeSpan.FromMinutes(10);

    private FrontierTokenResult? _lastToken;
    private FrontierValidationReport? _lastReport;

    public void SetPendingAuth(string state, string codeVerifier)
    {
        lock (_lock)
        {
            PurgeExpiredAttemptsLocked();
            _attempts[state] = new OAuthAttempt
            {
                State = state,
                CodeVerifier = codeVerifier,
                CreatedAt = DateTime.UtcNow,
                Status = "pending",
            };
        }
    }

    /// <summary>Consomme une tentative par state. Retourne (Verifier, Valid) ou null si introuvable/expirée.</summary>
    public (string? Verifier, bool Valid)? ConsumePendingAuth(string state)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;

        lock (_lock)
        {
            if (!_attempts.TryGetValue(state, out var attempt))
                return null;

            if (attempt.Status != "pending")
                return null;

            if (DateTime.UtcNow - attempt.CreatedAt > AttemptExpiration)
            {
                attempt.Status = "expired";
                _attempts.Remove(state);
                return null;
            }

            var verifier = attempt.CodeVerifier;
            attempt.Status = "used";
            _attempts.Remove(state);

            return string.IsNullOrEmpty(verifier) ? null : (verifier, true);
        }
    }

    private void PurgeExpiredAttemptsLocked()
    {
        var now = DateTime.UtcNow;
        var toRemove = _attempts.Where(kv => now - kv.Value.CreatedAt > AttemptExpiration).Select(kv => kv.Key).ToList();
        foreach (var k in toRemove)
            _attempts.Remove(k);
    }

    public void SetToken(FrontierTokenResult token, FrontierValidationReport? report = null)
    {
        lock (_lock)
        {
            _lastToken = token;
            _lastReport = report;
        }
    }

    public FrontierTokenResult? GetToken() => _lastToken;

    public FrontierValidationReport? GetReport() => _lastReport;

    public void ClearToken()
    {
        lock (_lock)
        {
            _lastToken = null;
            _lastReport = null;
        }
    }
}
