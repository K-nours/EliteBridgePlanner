using GuildDashboard.Server.DTOs;
using HtmlAgilityPack;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Récupère les informations d'une faction depuis Inara en chaînant 2 à 3 requêtes HTTP :
///   1. Page système (search) → lien "Faction au pouvoir"
///   2. Page faction → Allégeance, Origine, Faction de joueur, lien Escadron
///   3. Page escadron (optionnelle) → Langue, Fuseau horaire, Nombre de membres
///
/// Note : Inara peut bloquer les requêtes serveur (challenge anti-bot).
/// Dans ce cas, l'erreur est retournée proprement dans InaraFactionInfoDto.Error.
/// </summary>
public class InaraSystemFactionService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<InaraSystemFactionService> _log;

    private const string BaseUrl = "https://inara.cz";
    private const int TimeoutSeconds = 20;

    // Labels reconnus (français + anglais) pour la robustesse
    private static readonly string[] LabelControllingFaction = ["Faction au pouvoir", "Controlling faction", "Faction contrôlante"];
    private static readonly string[] LabelAllegiance = ["allégeance", "allegiance"];
    private static readonly string[] LabelGovernment = ["gouvernement", "government"];
    private static readonly string[] LabelOrigin = ["origine", "origin"];
    private static readonly string[] LabelPlayerFaction = ["faction de joueur", "player faction"];
    private static readonly string[] LabelSquadron = ["escadron", "squadron"];
    private static readonly string[] LabelLanguage = ["langue", "language"];
    private static readonly string[] LabelTimezone = ["fuseau horaire", "timezone", "time zone"];
    private static readonly string[] LabelMembers = ["membres", "members", "pilotes", "pilots"];

    public InaraSystemFactionService(IHttpClientFactory httpFactory, ILogger<InaraSystemFactionService> log)
    {
        _httpFactory = httpFactory;
        _log = log;
    }

    /// <summary>
    /// Enchaîne les requêtes Inara pour récupérer les infos de la faction dominante du système.
    /// </summary>
    public async Task<InaraFactionInfoDto> GetFactionInfoAsync(string systemName, CancellationToken ct = default)
    {
        using var client = CreateClient();

        // ── Étape 1 : page système ──────────────────────────────────────────────
        var systemUrl = $"{BaseUrl}/elite/starsystem/?search={Uri.EscapeDataString(systemName)}";
        var systemHtml = await FetchHtmlAsync(client, systemUrl, ct);
        if (systemHtml == null)
            return Fail("Inara inaccessible (anti-bot Cloudflare ou timeout). Le scraping serveur est bloqué — essaye depuis le navigateur.");

        var (factionName, factionRelUrl) = ExtractControllingFaction(systemHtml);
        if (factionRelUrl == null)
            return Fail("Faction au pouvoir introuvable sur la page système Inara (structure DOM modifiée ou système introuvable).");

        // ── Étape 2 : page faction ──────────────────────────────────────────────
        var factionAbsUrl = factionRelUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? factionRelUrl
            : BaseUrl + factionRelUrl;

        var factionHtml = await FetchHtmlAsync(client, factionAbsUrl, ct);
        if (factionHtml == null)
            return new InaraFactionInfoDto
            {
                FactionName = factionName,
                FactionInaraUrl = factionAbsUrl,
                Error = "Page faction Inara inaccessible."
            };

        var info = ParseFactionPage(factionHtml, factionName, factionAbsUrl);

        // ── Étape 3 : page escadron (optionnelle) ───────────────────────────────
        if (!string.IsNullOrEmpty(info.SquadronInaraUrl))
        {
            var squadronAbsUrl = info.SquadronInaraUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? info.SquadronInaraUrl
                : BaseUrl + info.SquadronInaraUrl;

            info.SquadronInaraUrl = squadronAbsUrl; // normalise l'URL absolue

            var squadronHtml = await FetchHtmlAsync(client, squadronAbsUrl, ct);
            if (squadronHtml != null)
                EnrichWithSquadronInfo(squadronHtml, info);
        }

        return info;
    }

    // ── Parsing ──────────────────────────────────────────────────────────────────

    private (string? Name, string? Url) ExtractControllingFaction(string html)
    {
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var containers = doc.DocumentNode.SelectNodes("//div[contains(@class,'itempaircontainer')]");
            if (containers == null) return (null, null);

            foreach (var container in containers)
            {
                var labelText = container
                    .SelectSingleNode(".//div[contains(@class,'itempairlabel')]")?
                    .InnerText?.Trim() ?? "";

                if (!LabelControllingFaction.Any(l => labelText.Equals(l, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var valueDiv = container.SelectSingleNode(".//div[contains(@class,'itempairvalue')]");
                var link = valueDiv?.SelectSingleNode(".//a[@href]");
                if (link == null) continue;

                var href = link.GetAttributeValue("href", "");
                if (!href.Contains("/minorfaction/")) continue;

                var name = HtmlEntity.DeEntitize(link.InnerText?.Trim() ?? "");
                return (name, href);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[InaraSystemFaction] Erreur parsing page système");
        }

        return (null, null);
    }

    private InaraFactionInfoDto ParseFactionPage(string html, string? factionName, string factionUrl)
    {
        var info = new InaraFactionInfoDto
        {
            FactionName = factionName,
            FactionInaraUrl = factionUrl
        };

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var containers = doc.DocumentNode.SelectNodes("//div[contains(@class,'itempaircontainer')]");
            if (containers == null) return info;

            foreach (var container in containers)
            {
                var labelText = (container
                    .SelectSingleNode(".//div[contains(@class,'itempairlabel')]")?
                    .InnerText?.Trim() ?? "").ToLowerInvariant();

                var valueDiv = container.SelectSingleNode(".//div[contains(@class,'itempairvalue')]");
                var valueText = HtmlEntity.DeEntitize(valueDiv?.InnerText?.Trim() ?? "");

                if (LabelAllegiance.Contains(labelText))
                {
                    info.Allegiance = valueText;
                }
                else if (LabelGovernment.Contains(labelText))
                {
                    info.Government = valueText;
                }
                else if (LabelOrigin.Contains(labelText))
                {
                    // Origine peut être un lien vers un système
                    var link = valueDiv?.SelectSingleNode(".//a[@href]");
                    info.Origin = link != null
                        ? HtmlEntity.DeEntitize(link.InnerText?.Trim() ?? valueText)
                        : valueText;
                }
                else if (LabelPlayerFaction.Contains(labelText))
                {
                    info.IsPlayerFaction =
                        valueText.Equals("Oui", StringComparison.OrdinalIgnoreCase) ||
                        valueText.Equals("Yes", StringComparison.OrdinalIgnoreCase);
                }
                else if (LabelSquadron.Contains(labelText))
                {
                    var link = valueDiv?.SelectSingleNode(".//a[@href]");
                    if (link != null)
                    {
                        info.SquadronName = HtmlEntity.DeEntitize(link.InnerText?.Trim() ?? "");
                        info.SquadronInaraUrl = link.GetAttributeValue("href", "");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[InaraSystemFaction] Erreur parsing page faction {Url}", factionUrl);
        }

        return info;
    }

    private void EnrichWithSquadronInfo(string html, InaraFactionInfoDto info)
    {
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var containers = doc.DocumentNode.SelectNodes("//div[contains(@class,'itempaircontainer')]");
            if (containers == null) return;

            foreach (var container in containers)
            {
                var labelText = (container
                    .SelectSingleNode(".//div[contains(@class,'itempairlabel')]")?
                    .InnerText?.Trim() ?? "").ToLowerInvariant();

                var valueText = HtmlEntity.DeEntitize(
                    container.SelectSingleNode(".//div[contains(@class,'itempairvalue')]")?
                    .InnerText?.Trim() ?? "");

                if (LabelLanguage.Contains(labelText))
                    info.SquadronLanguage = valueText;
                else if (LabelTimezone.Contains(labelText))
                    info.SquadronTimezone = valueText;
                else if (LabelMembers.Contains(labelText))
                {
                    // Parfois "42 membres" ou juste "42"
                    var digits = new string(valueText.Where(char.IsDigit).ToArray());
                    if (int.TryParse(digits, out var count) && count > 0)
                        info.SquadronMembersCount = count;
                }
            }

            // Fallback : compter les lignes de la table roster si disponible
            if (info.SquadronMembersCount == null)
            {
                var rosterRows = doc.DocumentNode.SelectNodes(
                    "//table[contains(@class,'roster')]//tr[not(contains(@class,'header'))]");
                if (rosterRows?.Count > 0)
                    info.SquadronMembersCount = rosterRows.Count;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[InaraSystemFaction] Erreur parsing page escadron");
        }
    }

    // ── HTTP ──────────────────────────────────────────────────────────────────────

    private async Task<string?> FetchHtmlAsync(HttpClient client, string url, CancellationToken ct)
    {
        try
        {
            _log.LogInformation("[InaraSystemFaction] GET {Url}", url);
            var response = await client.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("[InaraSystemFaction] HTTP {Status} — {Url}", (int)response.StatusCode, url);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(ct);

            if (string.IsNullOrWhiteSpace(html) || html.Length < 300)
            {
                _log.LogWarning("[InaraSystemFaction] Réponse trop courte ({Len}) — {Url}", html?.Length ?? 0, url);
                return null;
            }

            if (html.Contains("Access check required", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("Cloudflare", StringComparison.OrdinalIgnoreCase) && html.Contains("checking your browser", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("something happened", StringComparison.OrdinalIgnoreCase))
            {
                _log.LogWarning("[InaraSystemFaction] Bloqué anti-bot Cloudflare — {Url}", url);
                return null;
            }

            return html;
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            _log.LogWarning("[InaraSystemFaction] Annulé — {Url}", url);
            return null;
        }
        catch (TaskCanceledException)
        {
            _log.LogWarning("[InaraSystemFaction] Timeout ({Timeout}s) — {Url}", TimeoutSeconds, url);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "[InaraSystemFaction] Erreur réseau — {Url}", url);
            return null;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[InaraSystemFaction] Erreur inattendue — {Url}", url);
            return null;
        }
    }

    private HttpClient CreateClient()
    {
        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);
        client.DefaultRequestHeaders.Clear();
        // User-Agent navigateur pour contourner les filtres basiques
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Add("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        client.DefaultRequestHeaders.Add("Accept-Language", "fr-FR,fr;q=0.9,en-US;q=0.8,en;q=0.7");
        client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
        return client;
    }

    private static InaraFactionInfoDto Fail(string error) => new() { Error = error };
}
