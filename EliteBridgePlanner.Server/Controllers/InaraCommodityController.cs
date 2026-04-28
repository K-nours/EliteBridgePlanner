using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace EliteBridgePlanner.Server.Controllers;

/// <summary>
/// Proxy vers la page Inara des rare commodities.
/// Scrape le tableau HTML une fois par appel et retourne les commodités
/// dont le stock (supply) dépasse le seuil configuré.
/// Le cache "une fois par jour" est géré côté client (localStorage).
/// </summary>
[ApiController]
[Route("api/inara")]
[Authorize]
public class InaraCommodityController : ControllerBase
{
    private const string InaraUrl       = "https://inara.cz/elite/commodities-rare/";
    private const int    SupplyThreshold = 350;

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<InaraCommodityController> _logger;

    public InaraCommodityController(
        IHttpClientFactory httpFactory,
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EliteBridgePlanner/1.0");
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

        return Ok(new
        {
            hasAlert    = commodities.Count > 0,
            commodities
        });
    }

    // ── HTML parser ───────────────────────────────────────────────────────────

    private static readonly Regex RxRows     = new(@"<tr[^>]*>(.*?)</tr>",              RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex RxCells    = new(@"<td[^>]*>(.*?)</td>",              RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex RxThCells  = new(@"<th[^>]*>(.*?)</th>",              RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex RxLink     = new(@"<a[^>]+href=""([^""]+)""[^>]*>\s*([^<]+?)\s*</a>", RegexOptions.IgnoreCase);
    private static readonly Regex RxSupply   = new(@"([\d,\.]+)\s*t\b",                 RegexOptions.IgnoreCase);
    private static readonly Regex RxStripTags = new(@"<[^>]+>",                         RegexOptions.Singleline);
    private static readonly Regex RxThead    = new(@"<thead[^>]*>(.*?)</thead>",        RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex RxTbody    = new(@"<tbody[^>]*>(.*?)</tbody>",        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static List<object> ParseRareCommodities(string html)
    {
        var results = new List<object>();

        // ── 1. Cherche l'index de la colonne supply dans le <thead> ──────────
        int supplyCol = -1;
        var theadMatch = RxThead.Match(html);
        if (theadMatch.Success)
        {
            var headerCells = RxThCells.Matches(theadMatch.Groups[1].Value);
            for (int i = 0; i < headerCells.Count; i++)
            {
                var text = RxStripTags.Replace(headerCells[i].Groups[1].Value, "")
                                      .Trim()
                                      .ToLowerInvariant();
                if (text.Contains("supply") || text.Contains("stock") ||
                    text.Contains("offre")  || text.Contains("offer"))
                {
                    supplyCol = i;
                    break;
                }
            }
        }

        // ── 2. Parse les lignes du <tbody> ───────────────────────────────────
        var tbodyMatch = RxTbody.Match(html);
        if (!tbodyMatch.Success)
        {
            // fallback : scan tout le HTML si pas de <tbody> explicite
            return ParseFromRawRows(html, supplyCol);
        }

        foreach (Match row in RxRows.Matches(tbodyMatch.Groups[1].Value))
        {
            TryParseRow(row.Groups[1].Value, supplyCol, results);
        }

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

        // Nom + URL de la commodité (première cellule)
        string name = "";
        string url  = InaraUrl;

        var linkMatch = RxLink.Match(cells[0].Groups[1].Value);
        if (linkMatch.Success)
        {
            url  = linkMatch.Groups[1].Value.Trim();
            name = linkMatch.Groups[2].Value.Trim();
            if (!url.StartsWith("http"))
                url = "https://inara.cz" + url;
        }
        else
        {
            name = RxStripTags.Replace(cells[0].Groups[1].Value, "").Trim();
        }

        if (string.IsNullOrEmpty(name)) return;

        // Colonne supply : index trouvé via header, ou on scanne toutes les cellules
        if (supplyCol >= 0 && supplyCol < cells.Count)
        {
            CheckCellForSupply(cells[supplyCol].Groups[1].Value, name, url, results);
        }
        else
        {
            // Scan toutes les cellules — on prend la première qui ressemble à "NNN t"
            for (int i = 1; i < cells.Count; i++)
            {
                if (CheckCellForSupply(cells[i].Groups[1].Value, name, url, results))
                    break;
            }
        }
    }

    /// <returns>true si une valeur de supply a été trouvée (quelle que soit sa valeur)</returns>
    private static bool CheckCellForSupply(string cellHtml, string name, string url, List<object> results)
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
}
