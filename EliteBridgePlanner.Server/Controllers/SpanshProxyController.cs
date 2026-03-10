using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EliteBridgePlanner.Server.Controllers;

/// <summary>
/// Proxy vers l'API Spansh — permet au frontend d'appeler Spansh sans CORS,
/// que l'app soit servie par ng serve ou par le backend .NET.
/// </summary>
[ApiController]
[Route("api/spansh")]
[Authorize]
public class SpanshProxyController : ControllerBase
{
    private const string SpanshBase = "https://www.spansh.co.uk";
    private readonly IHttpClientFactory _httpFactory;

    public SpanshProxyController(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    /// <summary>Crée un job de calcul de route colonisation.</summary>
    [HttpPost("colonisation/route")]
    public async Task<IActionResult> CreateColonisationRoute(
        [FromForm] string source_system,
        [FromForm] string destination_system,
        CancellationToken ct)
    {
        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EliteBridgePlanner/1.0");

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("source_system", source_system),
            new KeyValuePair<string, string>("destination_system", destination_system)
        });

        var response = await client.PostAsync($"{SpanshBase}/api/colonisation/route", form, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, content);

        return Content(content, "application/json");
    }

    /// <summary>Récupère le résultat d'un job Spansh.</summary>
    [HttpGet("results/{jobId}")]
    public async Task<IActionResult> GetResult(string jobId, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EliteBridgePlanner/1.0");

        var response = await client.GetAsync($"{SpanshBase}/api/results/{jobId}", ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, content);

        return Content(content, "application/json");
    }
}
