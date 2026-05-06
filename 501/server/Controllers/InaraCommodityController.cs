using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using System.Web;

namespace GuildDashboard.Server.Controllers;

/// <summary>
/// Proxy vers la page Inara des rare commodities.
/// Scrape le tableau HTML côté serveur (pas de CORS) et retourne les
/// commodités dont le stock (supply) dépasse le seuil de 350t.
/// Le cache "une fois par jour" est géré côté client (localStorage).
/// </summary>
[ApiController]
[Route("api/inara")]
public class InaraCommodityController : ControllerBase
{
    private const string InaraUrl        = "https://inara.cz/elite/commodities-rare/";
    private const int    SupplyThreshold = 350;

    private readonly IHttpClientFactory                _httpFactory;
    private readonly ILogger<InaraCommodityController> _logger;

    public InaraCommodityController(
        IHttpClientFactory                httpFactory,
        ILogger<InaraCommodityController> logger)
    {
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    /// <summary>
    /// GET /api/inara/rare-commodities
    /// Retourne { hasAlert: bool, commodities: [{ name, supplyT, url }] }
    /// </summary>
    [HttpGet("rare-commodities")]
    public async Task<IActionResult> CheckRareCommodities(CancellationToken ct)
    {
        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GuildDashboard/1.0");
        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml");

        string html;
        try
        {
            var response = await client.GetAsync(InaraUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Inara a répondu {Status}", response.StatusCode);
                return StatusCode((int)response.StatusCode, "Erreur Inara");
            }
            html = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible de contacter Inara");
            return StatusCode(502, "Impossible de contacter Inara");
        }

        var commodities = ParseRareCommodities(html);

        return Ok(new { hasAlert = commodities.Count > 0, commodities });
    }

    // ── HTML parser ───────────────────────────────────────────────────────────

    private static readonly Regex RxRows      = new(@"<tr[^>]*>(.*?)</tr>",              RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex RxCells     = new(@"<td[^>]*>(.*?)</td>",              RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex RxThCells   = new(@"<th[^>]*>(.*?)</th>",              RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex RxLink      = new(@"<a[^>]+href=""([^""]+)""[^>]*>\s*([^<]+?)\s*</a>", RegexOptions.IgnoreCase);
    private static readonly Regex RxSupply    = new(@"([\d,\.]+)\s*t\b",                  RegexOptions.IgnoreCase);
    private static readonly Regex RxStripTags = new(@"<[^>]+>",                           RegexOptions.Singleline);
    private static readonly Regex RxThead     = new(@"<thead[^>]*>(.*?)</thead>",         RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex RxTbody     = new(@"<tbody[^>]*>(.*?)</tbody>",         RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static List<object> ParseRareCommodities(string html)
    {
        var results = new List<object>();

        // ── 1. Index de la colonne supply ─────────────────────────────────────
        int supplyCol = -1;
        var theadMatch = RxThead.Match(html);
        if (theadMatch.Success)
        {
            var headerCells = RxThCells.Matches(theadMatch.Groups[1].Value);
            for (int i = 0; i < headerCells.Count; i++)
            {
                var text = RxStripTags.Replace(headerCells[i].Groups[1].Value, "")
                                      .Trim().ToLowerInvariant();
                if (text.Contains("supply") || text.Contains("stock") ||
                    text.Contains("offre")  || text.Contains("offer"))
                {
                    supplyCol = i;
                    break;
                }
            }
        }

        // ── 2. Lignes du tbody ────────────────────────────────────────────────
        var tbodyMatch = RxTbody.Match(html);
        if (!tbodyMatch.Success)
            return ParseFromRawRows(html, supplyCol);

        foreach (Match row in RxRows.Matches(tbodyMatch.Groups[1].Value))
            TryParseRow(row.Groups[1].Value, supplyCol, results);

        return results;
    }

    private static List<object> ParseFromRawRows(string html, int supplyCol)
    {
        var results = new List<object>();
        bool headerSkipped = false;
        foreach (Match row in RxRows.Matches(html))
        {
            if (!headerSkipped) { headerSkipped = true; continue; }
            TryParseRow(row.Groups[1].Value, supplyCol, results);
        }
        return results;
    }

    private static void TryParseRow(string rowHtml, int supplyCol, List<object> results)
    {
        var cells = RxCells.Matches(rowHtml);
        if (cells.Count < 2) return;

        string name = "";
        string url  = InaraUrl;

        var linkMatch = RxLink.Match(cells[0].Groups[1].Value);
        if (linkMatch.Success)
        {
            url  = linkMatch.Groups[1].Value.Trim();
            name = linkMatch.Groups[2].Value.Trim();
            if (!url.StartsWith("http")) url = "https://inara.cz" + url;
        }
        else
        {
            name = RxStripTags.Replace(cells[0].Groups[1].Value, "").Trim();
        }

        if (string.IsNullOrEmpty(name)) return;

        if (supplyCol >= 0 && supplyCol < cells.Count)
            CheckCell(cells[supplyCol].Groups[1].Value, name, url, results);
        else
            for (int i = 1; i < cells.Count; i++)
                if (CheckCell(cells[i].Groups[1].Value, name, url, results)) break;
    }

    private static bool CheckCell(string cellHtml, string name, string url, List<object> results)
    {
        var text = RxStripTags.Replace(cellHtml, "").Trim();
        var m    = RxSupply.Match(text);
        if (!m.Success) return false;

        var numStr = m.Groups[1].Value.Replace(",", "").Replace(".", "");
        if (!int.TryParse(numStr, out int supply)) return false;

        if (supply > SupplyThreshold)
            results.Add(new { name, supplyT = supply, url });

        return true;
    }

    // ── CMDR gallery ─────────────────────────────────────────────────────────

    private static readonly Regex RxGalleryImg = new(
        @"(?:src|href)=""(/data/gallery/[^""]+\.(jpg|jpeg|png|webp))""",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// GET /api/inara/cmdr-gallery?cmdrUrl=https://inara.cz/elite/cmdr/12345/
    /// Scrape la page profil Inara du CMDR et retourne les URLs d'images de sa galerie.
    /// </summary>
    [HttpGet("cmdr-gallery")]
    public async Task<IActionResult> GetCmdrGallery([FromQuery] string cmdrUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmdrUrl))
            return BadRequest("cmdrUrl requis");

        // Accepte les URLs Inara CMDR uniquement
        if (!cmdrUrl.StartsWith("https://inara.cz/", StringComparison.OrdinalIgnoreCase))
            return BadRequest("URL Inara invalide");

        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GuildDashboard/1.0");
        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml");

        string html;
        try
        {
            var response = await client.GetAsync(cmdrUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Inara gallery: {Status} pour {Url}", response.StatusCode, cmdrUrl);
                return StatusCode((int)response.StatusCode, "Erreur Inara");
            }
            html = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible de contacter Inara pour la galerie");
            return StatusCode(502, "Impossible de contacter Inara");
        }

        var images = RxGalleryImg.Matches(html)
            .Select(m => "https://inara.cz" + m.Groups[1].Value)
            .Distinct()
            .ToList();

        return Ok(new { images });
    }
}
