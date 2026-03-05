using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EliteBridgePlanner.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SystemsController : ControllerBase
{
    private readonly IBridgeService _bridgeService;

    public SystemsController(IBridgeService bridgeService)
    {
        _bridgeService = bridgeService;
    }

    /// <summary>Ajoute un système à n'importe quelle position du pont</summary>
    [HttpPost]
    [ProducesResponseType<StarSystemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateSystemRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _bridgeService.AddSystemAsync(request);
        return CreatedAtAction(nameof(Update), new { id = result.Id }, result);
    }

    /// <summary>Modifie les propriétés d'un système (PATCH partiel)</summary>
    [HttpPatch("{id:int}")]
    [ProducesResponseType<StarSystemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSystemRequest request)
    {
        var result = await _bridgeService.UpdateSystemAsync(id, request);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Déplace un système à une nouvelle position dans le pont</summary>
    [HttpPatch("{id:int}/reorder")]
    [ProducesResponseType<StarSystemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Move(int id, [FromBody] MoveSystemRequest request)
    {
        var result = await _bridgeService.MoveSystemAsync(id, request.InsertAfterId);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Supprime un système et compacte automatiquement les positions</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var ok = await _bridgeService.DeleteSystemAsync(id);
        return ok ? NoContent() : NotFound();
    }
}
