using System.Text.Json;
using GuildDashboard.Server.Data;
using GuildDashboard.Server.DTOs;
using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>Exploitation des données CAPI Frontier : /profile, mapping commander/squadron → utilisateur/guild.</summary>
public class FrontierUserService
{
    private readonly GuildDashboardDbContext _db;
    private readonly FrontierAuthService _auth;
    private readonly FrontierTokenStore _store;
    private readonly FrontierOAuthSessionService _oauthSession;
    private readonly ILogger<FrontierUserService> _log;

    public FrontierUserService(
        GuildDashboardDbContext db,
        FrontierAuthService auth,
        FrontierTokenStore store,
        FrontierOAuthSessionService oauthSession,
        ILogger<FrontierUserService> log)
    {
        _db = db;
        _auth = auth;
        _store = store;
        _oauthSession = oauthSession;
        _log = log;
    }

    /// <summary>Récupère le profil CAPI si un token Frontier valide existe. Parse, mappe squadron→guild, persiste et retourne.</summary>
    public async Task<FrontierProfileDto?> GetProfileAsync(CancellationToken ct = default)
    {
        var token = await _oauthSession.GetEffectiveTokenAsync(ct);
        if (token == null || string.IsNullOrEmpty(token.AccessToken))
            return await GetCachedProfileAsync(ct);

        var (statusCode, body) = await _auth.FetchCapiRawAsync(token.AccessToken, "/profile", ct);
        if (statusCode != 200 || string.IsNullOrEmpty(body))
        {
            if (statusCode == 422)
            {
                _log.LogInformation("[FrontierUser] Token expiré (422), tentative refresh");
                var refreshed = await _auth.RefreshTokenAsync(token.RefreshToken ?? "", ct);
                if (refreshed != null)
                {
                    await _oauthSession.PersistAndSetAsync(refreshed, null, ct);
                    return await GetProfileAsync(ct);
                }
            }
            return await GetCachedProfileAsync(ct);
        }

        var (parsed, error) = ParseCapiProfile(body);
        if (parsed == null)
        {
            _log.LogWarning("[FrontierUser] Parse CAPI échoué: {Error}", error);
            return await GetCachedProfileAsync(ct);
        }

        var guildId = await ResolveGuildIdFromSquadronAsync(parsed.SquadronName, ct);
        await UpsertUserAsync(parsed.FrontierCustomerId, parsed.CommanderName, parsed.SquadronName, guildId, ct);
        var entity = await UpsertProfileAsync(parsed, guildId, ct);
        return ToDto(entity);
    }

    /// <summary>Profil en cache (dernier connu) si pas de token ou CAPI indisponible.</summary>
    public async Task<FrontierProfileDto?> GetCachedProfileAsync(CancellationToken ct = default)
    {
        var latest = await _db.FrontierProfiles
            .AsNoTracking()
            .OrderByDescending(p => p.LastFetchedAt)
            .Include(p => p.Guild)
            .FirstOrDefaultAsync(ct);
        return latest == null ? null : ToDto(latest);
    }

    /// <summary>Parse le JSON CAPI /profile avec la même logique que la persistance (inspection / chantiers debug).</summary>
    public (FrontierProfileParseResult? Fields, string? Error) TryParseCapiProfileFields(string json)
    {
        var (parsed, err) = ParseCapiProfile(json);
        if (parsed == null)
            return (null, err);
        return (new FrontierProfileParseResult(
            parsed.FrontierCustomerId,
            parsed.CommanderName,
            parsed.SquadronName,
            parsed.LastSystemName,
            parsed.ShipName,
            parsed.IsDocked,
            parsed.StationName), null);
    }

    private static (CapiParsed? Parsed, string? Error) ParseCapiProfile(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var commander = root.TryGetProperty("commander", out var c) ? c : default;
            var commanderName = commander.TryGetProperty("name", out var cn) ? cn.GetString() ?? "" : "";
            var frontierId = commander.TryGetProperty("id", out var cid)
                ? cid.GetRawText().Trim('"')
                : commander.TryGetProperty("FrontierID", out var fid)
                    ? fid.GetRawText().Trim('"')
                    : "";

            var squadronName = root.TryGetProperty("squadron", out var sq)
                ? (sq.TryGetProperty("name", out var sqn) ? sqn.GetString() : sq.TryGetProperty("Name", out var sqn2) ? sqn2.GetString() : null)
                : commander.TryGetProperty("squadron", out var cqs)
                    ? (cqs.TryGetProperty("name", out var cqsn) ? cqsn.GetString() : cqs.TryGetProperty("Name", out var cqsn2) ? cqsn2.GetString() : null)
                    : commander.TryGetProperty("Squadron", out var csq)
                        ? csq.GetString()
                        : null;

            var lastSystemName = root.TryGetProperty("lastSystem", out var ls)
                ? (ls.TryGetProperty("name", out var lsn) ? lsn.GetString() : ls.TryGetProperty("Name", out var lsn2) ? lsn2.GetString() : null)
                : null;

            var shipName = root.TryGetProperty("ship", out var sh)
                ? (sh.TryGetProperty("name", out var shn) ? shn.GetString() : sh.TryGetProperty("Name", out var shn2) ? shn2.GetString() : null)
                : root.TryGetProperty("shipName", out var shn3)
                    ? shn3.GetString()
                    : null;

            var isDocked = ParseDocked(root, commander);
            var stationName = ParseStationName(root, commander);

            if (string.IsNullOrEmpty(commanderName) && string.IsNullOrEmpty(frontierId))
                return (null, "commander.name et commander.id manquants");

            return (new CapiParsed(
                string.IsNullOrEmpty(frontierId) ? $"cmd-{commanderName}" : frontierId,
                commanderName,
                squadronName,
                lastSystemName,
                shipName,
                isDocked,
                stationName
            ), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>Utilisateur courant (FrontierUser) — identité liée à la guild.</summary>
    public async Task<FrontierUserDto?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var profile = await GetProfileAsync(ct);
        if (profile == null) return null;
        return new FrontierUserDto(profile.FrontierCustomerId, profile.CommanderName, profile.SquadronName, profile.GuildId, profile.GuildName);
    }

    private async Task UpsertUserAsync(string customerId, string commanderName, string? squadronName, int? guildId, CancellationToken ct)
    {
        var existing = await _db.FrontierUsers.FirstOrDefaultAsync(u => u.CustomerId == customerId, ct);
        var now = DateTime.UtcNow;
        if (existing != null)
        {
            existing.CommanderName = commanderName;
            existing.SquadronName = squadronName;
            existing.GuildId = guildId;
            existing.UpdatedAt = now;
        }
        else
        {
            _db.FrontierUsers.Add(new FrontierUser
            {
                CustomerId = customerId,
                CommanderName = commanderName,
                SquadronName = squadronName,
                GuildId = guildId,
                UpdatedAt = now,
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task<int?> ResolveGuildIdFromSquadronAsync(string? squadronName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(squadronName)) return null;
        var normalized = squadronName.Trim().ToLowerInvariant();
        var guild = await _db.Guilds
            .AsNoTracking()
            .Where(g => g.SquadronName != null && g.SquadronName.Trim().ToLower() == normalized)
            .Select(g => new { g.Id })
            .FirstOrDefaultAsync(ct);
        return guild?.Id;
    }

    private async Task<FrontierProfile> UpsertProfileAsync(CapiParsed parsed, int? guildId, CancellationToken ct)
    {
        var existing = await _db.FrontierProfiles
            .FirstOrDefaultAsync(p => p.FrontierCustomerId == parsed.FrontierCustomerId, ct);

        var now = DateTime.UtcNow;
        if (existing != null)
        {
            existing.CommanderName = parsed.CommanderName;
            existing.SquadronName = parsed.SquadronName;
            existing.LastSystemName = parsed.LastSystemName;
            existing.ShipName = parsed.ShipName;
            existing.GuildId = guildId;
            existing.LastFetchedAt = now;
        }
        else
        {
            existing = new FrontierProfile
            {
                FrontierCustomerId = parsed.FrontierCustomerId,
                CommanderName = parsed.CommanderName,
                SquadronName = parsed.SquadronName,
                LastSystemName = parsed.LastSystemName,
                ShipName = parsed.ShipName,
                GuildId = guildId,
                LastFetchedAt = now,
            };
            _db.FrontierProfiles.Add(existing);
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("[FrontierUser] Profil enregistré: {Name} squadron={Squadron} guild={GuildId}",
            parsed.CommanderName, parsed.SquadronName, guildId);
        return await _db.FrontierProfiles.Include(p => p.Guild).FirstAsync(p => p.Id == existing.Id, ct);
    }

    private static FrontierProfileDto ToDto(FrontierProfile p)
    {
        return new FrontierProfileDto(
            p.FrontierCustomerId,
            p.CommanderName,
            p.SquadronName,
            p.LastSystemName,
            p.ShipName,
            p.GuildId,
            p.Guild?.SquadronName ?? p.Guild?.DisplayName ?? p.Guild?.Name,
            p.LastFetchedAt
        );
    }

    private static bool? ParseDocked(JsonElement root, JsonElement commander)
    {
        static bool? Bool(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.True) return true;
            if (e.ValueKind == JsonValueKind.False) return false;
            return null;
        }

        if (root.TryGetProperty("docked", out var d) && Bool(d) is { } r0) return r0;

        if (commander.ValueKind == JsonValueKind.Object && commander.TryGetProperty("docked", out var d2) &&
            Bool(d2) is { } r1)
            return r1;

        if (commander.ValueKind == JsonValueKind.Object &&
            commander.TryGetProperty("location", out var cmdLoc) &&
            cmdLoc.ValueKind == JsonValueKind.Object &&
            cmdLoc.TryGetProperty("docked", out var d4) &&
            Bool(d4) is { } r2)
            return r2;

        if (root.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Object &&
            loc.TryGetProperty("docked", out var d3) &&
            Bool(d3) is { } r3)
            return r3;

        if (root.TryGetProperty("ship", out var shipRoot) && shipRoot.ValueKind == JsonValueKind.Object &&
            shipRoot.TryGetProperty("docked", out var sd) &&
            Bool(sd) is { } r4)
            return r4;

        return null;
    }

    private static string? ParseStationName(JsonElement root, JsonElement commander)
    {
        static string? NameFromObject(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }

            if (el.ValueKind != JsonValueKind.Object) return null;

            foreach (var prop in new[] { "name", "Name", "stationName", "StationName", "marketName", "MarketName" })
            {
                if (el.TryGetProperty(prop, out var n) && n.ValueKind == JsonValueKind.String)
                {
                    var s = n.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }

            return null;
        }

        static string? TryLastStarport(JsonElement container)
        {
            if (container.ValueKind != JsonValueKind.Object) return null;
            if (!container.TryGetProperty("lastStarport", out var lsp)) return null;
            return NameFromObject(lsp);
        }

        static string? TryStarport(JsonElement container)
        {
            if (container.ValueKind != JsonValueKind.Object) return null;
            if (!container.TryGetProperty("starport", out var sp)) return null;
            return NameFromObject(sp);
        }

        // Ordre : structures les plus spécifiques d’abord (CAPI / profile variables).
        var fromCmdLastStarport = TryLastStarport(commander);
        if (!string.IsNullOrWhiteSpace(fromCmdLastStarport)) return fromCmdLastStarport;

        var fromRootLastStarport = TryLastStarport(root);
        if (!string.IsNullOrWhiteSpace(fromRootLastStarport)) return fromRootLastStarport;

        if (root.TryGetProperty("lastStation", out var ls))
        {
            var x = NameFromObject(ls);
            if (!string.IsNullOrWhiteSpace(x)) return x;
        }

        if (root.TryGetProperty("station", out var st))
        {
            var x = NameFromObject(st);
            if (!string.IsNullOrWhiteSpace(x)) return x;
        }

        var fromCmdStarport = TryStarport(commander);
        if (!string.IsNullOrWhiteSpace(fromCmdStarport)) return fromCmdStarport;

        var fromRootStarport = TryStarport(root);
        if (!string.IsNullOrWhiteSpace(fromRootStarport)) return fromRootStarport;

        if (commander.ValueKind == JsonValueKind.Object && commander.TryGetProperty("lastStation", out var cls))
        {
            var x = NameFromObject(cls);
            if (!string.IsNullOrWhiteSpace(x)) return x;
        }

        if (root.TryGetProperty("ship", out var ship) && ship.ValueKind == JsonValueKind.Object)
        {
            if (ship.TryGetProperty("station", out var shipSt))
            {
                var x = NameFromObject(shipSt);
                if (!string.IsNullOrWhiteSpace(x)) return x;
            }
        }

        if (root.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Object)
        {
            if (loc.TryGetProperty("station", out var lst))
            {
                var x = NameFromObject(lst);
                if (!string.IsNullOrWhiteSpace(x)) return x;
            }

            if (loc.TryGetProperty("name", out var ln) && ln.ValueKind == JsonValueKind.String)
            {
                var s = ln.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }

        return null;
    }

    private record CapiParsed(
        string FrontierCustomerId,
        string CommanderName,
        string? SquadronName,
        string? LastSystemName,
        string? ShipName,
        bool? IsDocked,
        string? StationName);
}
