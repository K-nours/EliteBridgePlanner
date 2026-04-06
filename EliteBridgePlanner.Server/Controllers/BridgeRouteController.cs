using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EliteBridgePlanner.Server.Controllers;

/// <summary>
/// Route affichée sur le dashboard 501 (vue Pont galactique). Stockage mémoire uniquement.
/// GET / POST / DELETE — <see cref="AllowAnonymous"/> pour le front 501 (CORS sans JWT).
/// </summary>
[ApiController]
[Route("api/bridge-route")]
[AllowAnonymous]
public class BridgeRouteController : ControllerBase
{
    private readonly BridgeRouteStore _store;

    public BridgeRouteController(BridgeRouteStore store)
    {
        _store = store;
    }

    /// <summary>Récupère la route courante.</summary>
    [HttpGet]
    public IActionResult Get()
    {
        var p = _store.Get();
        return p == null ? NotFound() : Ok(p);
    }

    /// <summary>Enregistre ou remplace la route en mémoire.</summary>
    [HttpPost]
    public IActionResult Post([FromBody] BridgeRoutePayloadDto? dto)
    {
        if (dto?.Points == null || dto.Points.Count == 0)
            return BadRequest(new { error = "Points requis." });
        _store.Set(dto);
        return Ok(new { received = dto.Points.Count });
    }

    /// <summary>Efface la route.</summary>
    [HttpDelete]
    public IActionResult Delete()
    {
        _store.Clear();
        return NoContent();
    }
}
