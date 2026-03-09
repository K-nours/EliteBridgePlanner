using EliteBridgePlanner.Server.Data;
using EliteBridgePlanner.Server.Models;
using EliteBridgePlanner.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EliteBridgePlanner.Server.Controllers;

/// <summary>
/// Dev only — importe une route Spansh (Sol → Colonia) et remplit la BDD.
/// POST /api/dev/import-spansh-route avec { "source": "Sol", "destination": "Colonia" }
/// </summary>
[ApiController]
[Route("api/dev/import-spansh")]
[AllowAnonymous]
public class SpanshImportController : ControllerBase
{
    private readonly ISpanshRouteService _spansh;
    private readonly IBridgeService _bridge;
    private readonly AppDbContext _db;

    public SpanshImportController(ISpanshRouteService spansh, IBridgeService bridge, AppDbContext db)
    {
        _spansh = spansh;
        _bridge = bridge;
        _db = db;
    }

    /// <summary>Importe Sol→Colonia par défaut. ?replace=1 pour remplir le pont 1 existant.</summary>
    [HttpPost]
    public async Task<IActionResult> ImportRoute(
        [FromBody] ImportRouteRequest? request,
        [FromQuery] int? replace,
        CancellationToken ct)
    {
        var source = request?.Source?.Trim() ?? "Prieluia BP-R d4-105";
        var dest = request?.Destination?.Trim() ?? "Blae Hypue NH-V e2-2";

        var jumps = await _spansh.GetRouteAsync(source, dest, ct);

        var systems = ComputeSystemTypes(jumps);

        var user = await _db.Users
            .Where(u => u.Email == "cmdr@demo.local")
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(user))
            return Unauthorized("Utilisateur démo (cmdr@demo.local) introuvable. Lancez le seed d'abord.");

        var bridge = await _bridge.ImportSpanshRouteAsync(
            user,
            $"Pont {source} → {dest} (Spansh)",
            systems,
            replace == 1 ? 1 : null);

        return Ok(new { bridge, count = jumps.Count });
    }

    /// <summary>Calcule DEBUT, PILE, TABLIER, FIN comme le client (430–499 LY = PILE).</summary>
    private static IEnumerable<SpanshSystemImport> ComputeSystemTypes(IReadOnlyList<SpanshJump> jumps)
    {
        const int PILE_MIN = 430;
        const int PILE_MAX = 499;

        if (jumps.Count == 0) yield break;
        if (jumps.Count == 1)
        {
            yield return new SpanshSystemImport(jumps[0].Name, "DEBUT");
            yield break;
        }

        double cumulative = 0;
        double anchor = 0;

        for (var i = 0; i < jumps.Count; i++)
        {
            cumulative += jumps[i].Distance;
            var fromAnchor = cumulative - anchor;

            string type;
            if (i == 0)
                type = "DEBUT";
            else if (i == jumps.Count - 1)
                type = "FIN";
            else if (fromAnchor >= PILE_MIN && fromAnchor <= PILE_MAX)
            {
                type = "PILE";
                anchor = cumulative;
            }
            else
                type = "TABLIER";

            yield return new SpanshSystemImport(jumps[i].Name, type);
        }
    }
}

public record ImportRouteRequest(string? Source, string? Destination);
