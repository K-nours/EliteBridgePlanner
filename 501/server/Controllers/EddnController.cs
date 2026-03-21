using GuildDashboard.Server.Integrations.Eddn;
using Microsoft.AspNetCore.Mvc;

namespace GuildDashboard.Server.Controllers;

[ApiController]
[Route("api/eddn")]
public class EddnController : ControllerBase
{
    private readonly EddnStatusService _status;
    private readonly EddnMessageStore _store;

    public EddnController(EddnStatusService status, EddnMessageStore store)
    {
        _status = status;
        _store = store;
    }

    /// <summary>GET /api/eddn/dashboard — page unique status + analysis avec bouton refresh.</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken ct = default)
    {
        var lastSchemas = await _store.GetLastSchemaRefsAsync(20, ct);
        var lastReceivedAt = await _store.GetLastReceivedAtAsync(ct);
        var count = await _store.GetCountAsync(ct);
        var schemaFrequency = await _store.GetSchemaRefFrequencyAsync(50, ct);
        var topSystems = await _store.GetTopSystemsAsync(50, ct);
        var topStations = await _store.GetTopStationsAsync(50, ct);
        var schemaCoverage = await _store.GetSchemaFieldCoverageAsync(ct);

        var statusConnected = _status.IsConnected;
        var statusReceivedCount = _status.ReceivedCount;
        var lastAt = (lastReceivedAt ?? _status.LastReceivedAt)?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—";

        var body = BuildDashboardHtml(statusConnected, statusReceivedCount, count, lastAt, lastSchemas,
            schemaFrequency, topSystems, topStations, schemaCoverage);
        return Content(body, "text/html; charset=utf-8");
    }

    /// <summary>GET /api/eddn/status — diagnostic du listener EDDN (JSON).</summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct = default)
    {
        var lastSchemas = await _store.GetLastSchemaRefsAsync(20, ct);
        var lastReceivedAt = await _store.GetLastReceivedAtAsync(ct);
        var count = await _store.GetCountAsync(ct);
        return Ok(new
        {
            connected = _status.IsConnected,
            receivedCount = _status.ReceivedCount,
            storedCount = count,
            lastReceivedAt = lastReceivedAt ?? _status.LastReceivedAt,
            lastSchemasSeen = lastSchemas,
        });
    }

    /// <summary>GET /api/eddn/messages?limit=50 — messages bruts pour inspection/debug.</summary>
    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var messages = await _store.GetMessagesAsync(Math.Clamp(limit, 1, 200), ct);
        return Ok(messages);
    }

    /// <summary>GET /api/eddn/analysis — fréquence schémas, top systèmes/stations, couverture BGS (JSON).</summary>
    [HttpGet("analysis")]
    public async Task<IActionResult> GetAnalysis(CancellationToken ct = default)
    {
        var schemaFrequency = await _store.GetSchemaRefFrequencyAsync(50, ct);
        var topSystems = await _store.GetTopSystemsAsync(50, ct);
        var topStations = await _store.GetTopStationsAsync(50, ct);
        var payloadExamples = await _store.GetPayloadExamplesAsync(perSchema: 1, ct);
        var schemaCoverage = await _store.GetSchemaFieldCoverageAsync(ct);
        return Ok(new
        {
            schemaRefFrequency = schemaFrequency.Select(x => new { schemaRef = x.SchemaRef, count = x.Count }).ToList(),
            topSystems = topSystems.Select(x => new { systemName = x.SystemName, count = x.Count }).ToList(),
            topStations = topStations.Select(x => new { stationName = x.StationName, count = x.Count }).ToList(),
            payloadExamples,
            schemaFieldCoverage = schemaCoverage.Select(x => new
            {
                x.SchemaRef,
                x.TotalCount,
                withSystemName = x.WithSystemName,
                withStationName = x.WithStationName,
                hasFactions = x.HasFactions,
                hasInfluence = x.HasInfluence,
                hasState = x.HasState,
            }).ToList(),
        });
    }

    private static string BuildDashboardHtml(
        bool statusConnected,
        long statusReceivedCount,
        long storedCount,
        string lastAt,
        IReadOnlyList<string> lastSchemas,
        IReadOnlyList<(string SchemaRef, long Count)> schemaFreq,
        IReadOnlyList<(string SystemName, long Count)> topSystems,
        IReadOnlyList<(string StationName, long Count)> topStations,
        IReadOnlyList<EddnSchemaCoverage> schemaCoverage)
    {
        var css = "body{font-family:system-ui;margin:0;padding:1rem;background:#1a1a2e;color:#e6edf3;}" +
            ".header{display:flex;justify-content:space-between;align-items:center;margin-bottom:1.5rem;flex-wrap:wrap;gap:1rem;}" +
            "h1{color:#6ee7b7;margin:0;} .btn{background:#6ee7b7;color:#1a1a2e;border:none;padding:0.5rem 1rem;border-radius:6px;cursor:pointer;font-size:1rem;font-weight:600;}" +
            ".btn:hover{background:#5dd6a6;}" +
            ".row{display:grid;grid-template-columns:repeat(3,1fr);gap:1rem;margin-bottom:1rem;}" +
            ".row .card.full{grid-column:1/-1;}" +
            ".card{background:#2d2d44;padding:1rem;border-radius:8px;}" +
            ".card p{margin:0.3rem 0;} .ok{color:#6ee7b7;} .count{color:#93c5fd;font-size:1.1em;}" +
            "h2{color:#6ee7b7;margin:0 0 0.8rem 0;font-size:1rem;}" +
            "table{border-collapse:collapse;width:100%;font-size:0.9em;} th,td{border:1px solid #444;padding:0.3rem 0.5rem;text-align:left;} th{background:#1a1a2e;}" +
            ".yes{color:#6ee7b7;} .no{color:#666;} ul{list-style:none;padding:0;margin:0;font-size:0.85em;} li{padding:0.2em 0;border-bottom:1px solid #333;}";

        var statusClass = statusConnected ? "ok" : "";
        var statusText = statusConnected ? "Oui" : "Non";
        var schemasHtml = string.Join("", (lastSchemas ?? Array.Empty<string>()).Select(s => "<li>" + System.Net.WebUtility.HtmlEncode(s) + "</li>"));

        var schemaRows = string.Join("", schemaFreq.Take(20).Select(x =>
            "<tr><td>" + System.Net.WebUtility.HtmlEncode(x.SchemaRef) + "</td><td>" + x.Count + "</td></tr>"));
        var systemRows = string.Join("", topSystems.Take(15).Select(x =>
            "<tr><td>" + System.Net.WebUtility.HtmlEncode(x.SystemName) + "</td><td>" + x.Count + "</td></tr>"));
        var stationRows = string.Join("", topStations.Take(15).Select(x =>
            "<tr><td>" + System.Net.WebUtility.HtmlEncode(x.StationName) + "</td><td>" + x.Count + "</td></tr>"));
        var coverageRows = string.Join("", schemaCoverage.Select(x =>
            "<tr><td>" + System.Net.WebUtility.HtmlEncode(x.SchemaRef) + "</td><td>" + x.TotalCount + "</td>" +
            "<td>" + x.WithSystemName + "</td><td>" + x.WithStationName + "</td>" +
            "<td class=\"" + (x.HasFactions ? "yes" : "no") + "\">" + (x.HasFactions ? "✓" : "—") + "</td>" +
            "<td class=\"" + (x.HasInfluence ? "yes" : "no") + "\">" + (x.HasInfluence ? "✓" : "—") + "</td>" +
            "<td class=\"" + (x.HasState ? "yes" : "no") + "\">" + (x.HasState ? "✓" : "—") + "</td></tr>"));

        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>EDDN Dashboard</title><style>" + css + "</style></head><body>" +
            "<div class=\"header\"><h1>EDDN Dashboard</h1>" +
            "<button class=\"btn\" onclick=\"location.reload()\">Rafraîchir</button></div>" +
            "<div class=\"row\"><div class=\"card\"><h2>EDDN statut</h2><p>Connecté : <span class=\"" + statusClass + "\">" + statusText + "</span></p>" +
            "<p>Reçus : <span class=\"count\">" + statusReceivedCount + "</span> | Stockés : <span class=\"count\">" + storedCount + "</span></p>" +
            "<p>Dernier : " + lastAt + "</p><ul>" + schemasHtml + "</ul></div>" +
            "<div class=\"card\"><h2>Top systèmes</h2><table><tr><th>Système</th><th>Count</th></tr>" + systemRows + "</table></div>" +
            "<div class=\"card\"><h2>Top stations</h2><table><tr><th>Station</th><th>Count</th></tr>" + stationRows + "</table></div></div>" +
            "<div class=\"row\"><div class=\"card full\"><h2>Fréquences schémas</h2><table><tr><th>SchemaRef</th><th>Count</th></tr>" + schemaRows + "</table></div></div>" +
            "<div class=\"row\"><div class=\"card full\"><h2>Couverture BGS par schéma</h2><table><tr><th>SchemaRef</th><th>Total</th><th>System</th><th>Station</th><th>Factions</th><th>Influence</th><th>State</th></tr>" + coverageRows + "</table></div></div>" +
            "</body></html>";
    }
}
