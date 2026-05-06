using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Récupère les données BGS d'une faction via scraping de la page présence Inara.
/// URL : https://inara.cz/elite/minorfaction-presence/{factionId}/
/// </summary>
/// <remarks>
/// Remplace EliteBGS (timeouts systématiques). Données extraites : systemName, influence, lastUpdate.
/// State BGS (War, Boom...) et IsControlled/IsThreatened ne sont pas disponibles sur la page présence → null/false.
/// Scraping fragile : tout changement de structure DOM cassera le parsing.
/// </remarks>
public class InaraFactionService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<InaraFactionService> _log;

    private static readonly Regex InfluenceRegex = new(@"(\d+(?:\.\d+)?)\s*%", RegexOptions.Compiled);

    private const int TimeoutSeconds = 15;

    public InaraFactionService(IHttpClientFactory httpFactory, ILogger<InaraFactionService> log)
    {
        _httpFactory = httpFactory;
        _log = log;
    }

    /// <summary>
    /// Récupère la présence de la faction par système (influence, lastUpdate).
    /// Nécessite Guild.InaraFactionId configuré en base.
    /// </summary>
    /// <returns>Liste des présences par système, ou null en cas d'erreur (timeout, HTML invalide, structure modifiée).</returns>
    public async Task<IReadOnlyList<InaraFactionPresence>?> GetFactionPresenceAsync(int inaraFactionId, CancellationToken ct = default)
    {
        var url = $"https://inara.cz/elite/minorfaction-presence/{inaraFactionId}/";
        _log.LogInformation("[InaraFaction] Requête: url={Url} timeout={Timeout}s", url, TimeoutSeconds);

        using var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("User-Agent", "GuildDashboard/1.0");

        try
        {
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("[InaraFaction] Échec http_non_200: status={Status} url={Url}",
                    (int)response.StatusCode, url);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(html) || html.Length < 500)
            {
                _log.LogWarning("[InaraFaction] Échec empty_response ou trop courte: length={Len} url={Url}",
                    html?.Length ?? 0, url);
                return null;
            }

            // Inara renvoie une page "Access check required" pour les requêtes server-side (anti-bot)
            if (html.Contains("Access check required", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("something happened", StringComparison.OrdinalIgnoreCase))
            {
                _log.LogWarning("[InaraFaction] Échec blocked_by_inara: page challenge anti-bot reçue (requêtes server-side bloquées) url={Url}", url);
                return null;
            }

            var presence = ParsePresenceTable(html);
            if (presence == null || presence.Count == 0)
            {
                _log.LogWarning("[InaraFaction] Échec parse: aucune donnée extraite (structure DOM modifiée?) url={Url}", url);
                return null;
            }

            _log.LogInformation("[InaraFaction] Succès: {Count} systèmes extraits url={Url}", presence.Count, url);
            return presence;
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            _log.LogWarning("[InaraFaction] Échec cancelled url={Url}", url);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _log.LogWarning(ex, "[InaraFaction] Échec timeout url={Url}", url);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "[InaraFaction] Échec network: {Msg} url={Url}", ex.Message, url);
            return null;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[InaraFaction] Échec inattendu: {Type} {Msg} url={Url}",
                ex.GetType().Name, ex.Message, url);
            return null;
        }
    }

    /// <summary>Parse la table de présence Inara. Retourne null si structure inattendue.</summary>
    private List<InaraFactionPresence>? ParsePresenceTable(string html)
    {
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var result = new List<InaraFactionPresence>();

            // Inara : table avec liens vers /elite/starsystem/{id} pour le nom du système
            var links = doc.DocumentNode.SelectNodes("//a[contains(@href,'/elite/starsystem/')]");
            if (links == null || links.Count == 0)
            {
                _log.LogDebug("[InaraFaction] Aucun lien starsystem trouvé dans le HTML");
                return result.Count > 0 ? result : null;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var link in links)
            {
                var systemName = link.InnerText?.Trim();
                if (string.IsNullOrWhiteSpace(systemName) || systemName.Length < 2)
                    continue;
                if (!seen.Add(systemName)) continue; // déduplication

                var cell = link.ParentNode;
                var row = cell?.ParentNode;
                if (row == null) continue;

                var cells = row.SelectNodes(".//td");
                if (cells == null || cells.Count < 2) continue;

                var rowText = row.InnerText ?? "";
                var influence = ParseInfluenceFromRow(rowText);
                var lastUpdate = ParseLastUpdateFromRow(rowText);

                result.Add(new InaraFactionPresence(systemName, influence, null, lastUpdate));
            }

            return result.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[InaraFaction] Erreur pendant le parsing HTML");
            return null;
        }
    }

    private decimal ParseInfluenceFromRow(string rowText)
    {
        var m = InfluenceRegex.Match(rowText);
        if (!m.Success) return 0;
        var s = m.Groups[1].Value;
        if (!decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
            return 0;
        if (val > 100)
        {
            _log.LogWarning("[InaraFaction] Influence > 100% détectée et clamée: raw=\"{Raw}\" parsed={Parsed}", s, val);
        }
        return Math.Clamp(val, 0, 100);
    }

    private static string? ParseLastUpdateFromRow(string rowText)
    {
        // "5 days ago", "2 hours ago", "17 days ago" — on conserve tel quel pour info, pas de parsing date
        var match = Regex.Match(rowText, @"(\d+\s*(?:day|hour|minute)s?\s*ago)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}

/// <summary>Présence d'une faction dans un système (données Inara scrapées).</summary>
public record InaraFactionPresence(string SystemName, decimal InfluencePercent, string? State, string? LastUpdateText);
