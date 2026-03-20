using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace GuildDashboard.Server.Services;

/// <summary>Récupère la liste des membres du squadron depuis la page roster Inara (si publique).</summary>
/// <remarks>
/// Le roster est obtenu via la page HTML https://inara.cz/elite/squadron-roster/{id}, pas via l'API Inara.
/// L'API Inara (inapi/v1) n'a pas d'endpoint roster — elle sert à getCommanderProfile, etc.
/// On teste les deux modes (sans clé, avec clé) pour documenter si Inara:ApiKey donne accès au roster privé.
/// TODO: Replace Inara roster scraping with a reliable data source (Frontier API or managed roster). Voir docs/INTEGRATION-INARA.md.
/// </remarks>
public class InaraSquadronRosterService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<InaraSquadronRosterService> _logger;
    private static readonly Regex CmdrLinkRegex = new(@"\[([^\]]+)\]\(https://inara\.cz/elite/cmdr/\d+/\)", RegexOptions.Compiled);
    private static readonly Regex SquadronLinkRegex = new(@"https://inara\.cz/elite/squadron/(\d+)/?", RegexOptions.Compiled);

    public InaraSquadronRosterService(HttpClient http, IConfiguration config, ILogger<InaraSquadronRosterService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.Add("User-Agent", "EliteBridgePlanner/1.0");
    }

    /// <summary>Récupère (nom, rang) depuis le roster Inara. Tente anonyme puis avec clé API si configurée. Liste vide si roster privé ou erreur.</summary>
    public async Task<IReadOnlyList<(string Name, string? Rank)>> GetMemberNamesAndRanksAsync(int squadronId, CancellationToken ct = default)
    {
        var url = $"https://inara.cz/elite/squadron-roster/{squadronId}";
        var apiKey = _config["Inara:ApiKey"];
        var hasApiKey = !string.IsNullOrWhiteSpace(apiKey);

        _logger.LogInformation("Inara roster fetch: squadronId={SquadronId}, endpoint={Url}", squadronId, url);

        // 1. Mode anonyme
        var (anonymousMembers, anonymousLog) = await FetchRosterOnceAsync(url, useApiKey: false, apiKey: null, ct);
        LogRosterAttempt("Inara roster — mode ANONYME", url, anonymousLog, anonymousMembers.Count);

        if (anonymousMembers.Count > 0)
        {
            _logger.LogInformation("Inara roster: mode anonyme OK — {Count} membres parsés", anonymousMembers.Count);
            return anonymousMembers;
        }

        // 2. Mode avec clé API (si configurée)
        if (hasApiKey)
        {
            var (keyedMembers, keyedLog) = await FetchRosterOnceAsync(url, useApiKey: true, apiKey, ct);
            LogRosterAttempt("Inara roster — mode AVEC CLÉ API", url, keyedLog, keyedMembers.Count);

            if (keyedMembers.Count > 0)
            {
                _logger.LogInformation("Inara roster: mode clé API OK — {Count} membres parsés", keyedMembers.Count);
                return keyedMembers;
            }
        }
        else
        {
            _logger.LogInformation("Inara roster: Inara:ApiKey non configurée — pas de tentative avec clé API");
        }

        _logger.LogWarning("Inara roster: ZÉRO membre récupéré (roster privé ou indisponible) — endpoint={Url}", url);
        return Array.Empty<(string, string?)>();
    }

    private void LogRosterAttempt(string modeLabel, string url, RosterFetchLog log, int membersCount)
    {
        var msg = membersCount == 0
            ? "Zéro membre parsé"
            : $"{membersCount} membre(s) parsé(s)";
        _logger.LogInformation(
            "{Mode} — URL={Url} | HTTP={StatusCode} | Taille={ResponseSize} octets | Content-Type={ContentType} | {Result}",
            modeLabel, url, log.StatusCode, log.ResponseSize, log.ContentType ?? "(null)", msg);
    }

    private async Task<(IReadOnlyList<(string Name, string? Rank)> Members, RosterFetchLog Log)> FetchRosterOnceAsync(
        string url, bool useApiKey, string? apiKey, CancellationToken ct)
    {
        var log = new RosterFetchLog { Url = url, UseApiKey = useApiKey };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (useApiKey && !string.IsNullOrWhiteSpace(apiKey))
                request.Headers.Add("X-Inara-ApiKey", apiKey);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            log.StatusCode = (int)response.StatusCode;
            log.ContentType = response.Content.Headers.ContentType?.ToString();

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            log.ResponseSize = bytes.Length;
            var html = System.Text.Encoding.UTF8.GetString(bytes);

            if (html.Contains("not allowed", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("You are not allowed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Inara roster: HTML contient 'not allowed' — roster probablement privé");
                return (Array.Empty<(string, string?)>(), log);
            }

            var rows = ParseRosterTable(html);
            return (rows, log);
        }
        catch (Exception ex)
        {
            log.Error = ex.Message;
            _logger.LogWarning(ex, "Inara roster fetch failed: {Mode}", useApiKey ? "avec clé API" : "anonyme");
            return (Array.Empty<(string, string?)>(), log);
        }
    }

    /// <summary>Diagnostic complet : tente anonyme + clé API, retourne les détails pour chaque tentative.</summary>
    public async Task<RosterDiagnosticResult> GetRosterDiagnosticAsync(int squadronId, CancellationToken ct = default)
    {
        var url = $"https://inara.cz/elite/squadron-roster/{squadronId}";
        var apiKey = _config["Inara:ApiKey"];
        var hasApiKey = !string.IsNullOrWhiteSpace(apiKey);

        var result = new RosterDiagnosticResult
        {
            SquadronId = squadronId,
            Endpoint = url,
            ApiKeyConfigured = hasApiKey,
        };

        var (anonMembers, anonLog) = await FetchRosterOnceAsync(url, useApiKey: false, null, ct);
        result.AnonymousAttempt = new RosterAttemptDetail
        {
            Mode = "anonyme",
            HttpStatusCode = anonLog.StatusCode,
            ResponseSizeBytes = anonLog.ResponseSize,
            ContentType = anonLog.ContentType,
            MembersParsed = anonMembers.Count,
            Error = anonLog.Error,
        };

        if (hasApiKey)
        {
            var (keyedMembers, keyedLog) = await FetchRosterOnceAsync(url, useApiKey: true, apiKey, ct);
            result.WithApiKeyAttempt = new RosterAttemptDetail
            {
                Mode = "avec clé API",
                HttpStatusCode = keyedLog.StatusCode,
                ResponseSizeBytes = keyedLog.ResponseSize,
                ContentType = keyedLog.ContentType,
                MembersParsed = keyedMembers.Count,
                Error = keyedLog.Error,
            };
        }

        result.Conclusion = result.AnonymousAttempt.MembersParsed > 0 || (result.WithApiKeyAttempt?.MembersParsed ?? 0) > 0
            ? "Roster accessible"
            : "Roster non accessible (privé ou indisponible). Inara:ApiKey ne fournit pas d'accès au roster — l'API Inara n'a pas d'endpoint roster.";
        return result;
    }

    /// <summary>Tente de résoudre l'ID squadron depuis la page faction Inara.</summary>
    public async Task<int?> TryResolveSquadronFromFactionAsync(int factionId, CancellationToken ct = default)
    {
        var url = $"https://inara.cz/elite/minorfaction/{factionId}";
        try
        {
            var html = await _http.GetStringAsync(url, ct);
            var match = SquadronLinkRegex.Match(html);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var squadronId))
                return squadronId;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TryResolveSquadronFromFaction failed for faction {FactionId}", factionId);
        }
        return null;
    }

    private static List<(string Name, string? Rank)> ParseRosterTable(string html)
    {
        var result = new List<(string, string?)>();
        var matches = CmdrLinkRegex.Matches(html);
        foreach (Match m in matches)
        {
            if (!m.Success || m.Groups.Count < 2) continue;
            var name = m.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            var rank = ExtractRankAfterLink(html, m.Index + m.Length);
            result.Add((name, rank));
        }
        return result;
    }

    private static string? ExtractRankAfterLink(string html, int startIndex)
    {
        var end = Math.Min(startIndex + 300, html.Length);
        var segment = html[startIndex..end];
        var parts = segment.Split('|');
        if (parts.Length >= 2)
        {
            var rank = parts[1].Trim();
            return string.IsNullOrWhiteSpace(rank) ? null : rank;
        }
        return null;
    }

    private sealed class RosterFetchLog
    {
        public string Url { get; set; } = "";
        public bool UseApiKey { get; set; }
        public int StatusCode { get; set; }
        public long ResponseSize { get; set; }
        public string? ContentType { get; set; }
        public string? Error { get; set; }
    }
}

/// <summary>Résultat de diagnostic du roster Inara.</summary>
public class RosterDiagnosticResult
{
    public int SquadronId { get; set; }
    public string Endpoint { get; set; } = "";
    public bool ApiKeyConfigured { get; set; }
    public RosterAttemptDetail AnonymousAttempt { get; set; } = null!;
    public RosterAttemptDetail? WithApiKeyAttempt { get; set; }
    public string Conclusion { get; set; } = "";
}

/// <summary>Détail d'une tentative de récupération du roster.</summary>
public class RosterAttemptDetail
{
    public string Mode { get; set; } = "";
    public int HttpStatusCode { get; set; }
    public long ResponseSizeBytes { get; set; }
    public string? ContentType { get; set; }
    public int MembersParsed { get; set; }
    public string? Error { get; set; }
}
