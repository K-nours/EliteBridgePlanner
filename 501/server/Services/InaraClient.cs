namespace GuildDashboard.Server.Services;

/// <summary>Client Inara — récupère les membres du squadron. Gère timeout, rate limit, erreurs.</summary>
/// <remarks>
/// Le squadron est résolu de manière STATIQUE via Squadron:InaraSquadronId (appsettings) ou Guild.InaraSquadronId.
/// TODO: Replace static InaraSquadronId with dynamic squadron resolution once Frontier authentication is implemented.
/// Rate limit Inara: max 2 req/min pour l'API.
/// </remarks>
public class InaraClient
{
    private readonly InaraSquadronRosterService _roster;
    private readonly InaraApiService _inara;
    private readonly IConfiguration _config;
    private readonly ILogger<InaraClient> _logger;

    public InaraClient(
        InaraSquadronRosterService roster,
        InaraApiService inara,
        IConfiguration config,
        ILogger<InaraClient> logger)
    {
        _roster = roster;
        _inara = inara;
        _config = config;
        _logger = logger;
    }

    /// <summary>Résout l'ID squadron (InaraSquadronId ou via InaraFactionId).</summary>
    public async Task<int?> GetSquadronIdAsync(int? inaraSquadronId, int? inaraFactionId, CancellationToken ct = default)
    {
        if (inaraSquadronId.HasValue && inaraSquadronId.Value > 0)
            return inaraSquadronId;

        if (inaraFactionId.HasValue && inaraFactionId.Value > 0)
        {
            var resolved = await _roster.TryResolveSquadronFromFactionAsync(inaraFactionId.Value, ct);
            if (resolved != null)
                return resolved;
        }

        // Source temporaire : config statique. Remplacer par résolution dynamique (auth Frontier) plus tard.
        var configSquadronId = _config.GetValue<int?>("Squadron:InaraSquadronId");
        return configSquadronId;
    }

    /// <summary>Récupère les membres du squadron avec noms et rangs (depuis le roster Inara).</summary>
    /// <remarks>TODO: Replace Inara roster scraping with a reliable data source (Frontier API or managed roster). Voir docs/INTEGRATION-INARA.md.</remarks>
    public async Task<IReadOnlyList<InaraSquadronMember>> GetSquadronMembersAsync(int squadronId, CancellationToken ct = default)
    {
        try
        {
            var rosterData = await _roster.GetMemberNamesAndRanksAsync(squadronId, ct);
            var result = new List<InaraSquadronMember>();

            foreach (var (name, rank) in rosterData)
            {
                string? avatarUrl = null;
                try
                {
                    var profile = await _inara.GetCommanderProfileAsync(name, ct);
                    avatarUrl = profile?.AvatarImageUrl;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Inara GetCommanderProfile failed for {Name}", name);
                }
                await Task.Delay(500, ct);

                result.Add(new InaraSquadronMember(name, avatarUrl, rank));
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Inara roster request failed");
            return Array.Empty<InaraSquadronMember>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inara roster error (timeout, rate limit, etc.)");
            return Array.Empty<InaraSquadronMember>();
        }
    }
}

public record InaraSquadronMember(string Name, string? AvatarUrl, string? Role);
