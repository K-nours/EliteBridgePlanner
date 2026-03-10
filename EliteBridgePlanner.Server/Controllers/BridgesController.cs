using System.Security.Claims;
using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EliteBridgePlanner.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BridgesController : ControllerBase
{
    private readonly IBridgeService _bridgeService;

    // IBridgeService injecté — mockable dans les tests NUnit
    public BridgesController(IBridgeService bridgeService)
    {
        _bridgeService = bridgeService;
    }

    /// <summary>Retourne tous les ponts avec leurs systèmes ordonnés</summary>
    [HttpGet]
    [ProducesResponseType<IEnumerable<BridgeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
        => Ok(await _bridgeService.GetAllBridgesAsync());

    /// <summary>Vide tous les systèmes du pont</summary>
    [HttpDelete("{id:int}/systems")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearAllSystems(int id)
    {
        var bridge = await _bridgeService.GetBridgeByIdAsync(id);
        if (bridge is null) return NotFound();
        await _bridgeService.ClearAllSystemsAsync(id);
        return NoContent();
    }

    /// <summary>Retourne un pont par son ID</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<BridgeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _bridgeService.GetBridgeByIdAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Importe une route calculée (Spansh) dans un nouveau pont ou remplace le pont actif</summary>
    [HttpPost("import-route")]
    [ProducesResponseType<BridgeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportRoute([FromBody] ImportRouteRequest? request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        if (request?.Systems is null || request.Systems.Count == 0)
            return BadRequest("Aucun système à importer.");

        var systems = request.Systems
            .Select(s => new SpanshSystemImport(s.Name, s.Type))
            .ToList();

        var bridgeName = request.BridgeName ?? $"Pont {systems[0].Name} → {systems[^1].Name} (Spansh)";
        var result = await _bridgeService.ImportSpanshRouteAsync(userId, bridgeName, systems, request.ReplaceBridgeId);
        return Ok(result);
    }

    /// <summary>Crée un nouveau pont pour le CMDR connecté</summary>
    [HttpPost]
    [ProducesResponseType<BridgeDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateBridgeRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // Récupérer l'ID utilisateur depuis le JWT
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var result = await _bridgeService.CreateBridgeAsync(request, userId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }
}

public record ImportRouteRequest(
    List<ImportSystemItem>? Systems,
    string? BridgeName = null,
    int? ReplaceBridgeId = null);

public record ImportSystemItem(string Name, string Type);
