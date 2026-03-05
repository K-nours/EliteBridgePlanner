using EliteBridgePlanner.Server.Data;
using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EliteBridgePlanner.Server.Services;

public class BridgeService : IBridgeService
{
    private readonly AppDbContext _db;

    // AppDbContext injecté — jamais instancié directement
    public BridgeService(AppDbContext db) => _db = db;

    // ── Bridges ───────────────────────────────────────────────────────────

    public async Task<IEnumerable<BridgeDto>> GetAllBridgesAsync()
    {
        var bridges = await _db.Bridges
            .Include(b => b.CreatedBy)
            .Include(b => b.Systems).ThenInclude(s => s.Architect)
            .AsNoTracking()
            .ToListAsync();

        return bridges.Select(MapToDto);
    }

    public async Task<BridgeDto?> GetBridgeByIdAsync(int id)
    {
        var bridge = await _db.Bridges
            .Include(b => b.CreatedBy)
            .Include(b => b.Systems).ThenInclude(s => s.Architect)
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id);

        return bridge is null ? null : MapToDto(bridge);
    }

    public async Task<BridgeDto> CreateBridgeAsync(CreateBridgeRequest request, string userId)
    {
        var bridge = new Bridge
        {
            Name = request.Name,
            Description = request.Description,
            CreatedByUserId = userId
        };

        _db.Bridges.Add(bridge);
        await _db.SaveChangesAsync();

        return (await GetBridgeByIdAsync(bridge.Id))!;
    }

    // ── StarSystems ───────────────────────────────────────────────────────

    public async Task<StarSystemDto> AddSystemAsync(CreateSystemRequest request)
    {
        var newSystem = new StarSystem
        {
            Name = request.Name,
            Type = Enum.Parse<SystemType>(request.Type, true),
            Status = Enum.Parse<ColonizationStatus>(request.Status, true),
            ArchitectId = string.IsNullOrEmpty(request.ArchitectId) ? null : request.ArchitectId,
            BridgeId = request.BridgeId
        };

        if (request.InsertAfterId is null)
        {
            // Insérer en tête — trouver l'actuel premier élément
            var currentHead = await _db.StarSystems
                .FirstOrDefaultAsync(s => s.BridgeId == request.BridgeId
                                       && s.PreviousSystemId == null);

            if (currentHead is not null)
            {
                newSystem.NextSystemId = currentHead.Id;
                _db.StarSystems.Add(newSystem);
                await _db.SaveChangesAsync();
                currentHead.PreviousSystemId = newSystem.Id;
            }
            else
            {
                _db.StarSystems.Add(newSystem);
                await _db.SaveChangesAsync();
            }
        }
        else
        {
            // Insérer après InsertAfterId
            var predecessor = await _db.StarSystems
                .FirstOrDefaultAsync(s => s.Id == request.InsertAfterId);

            if (predecessor is null)
                throw new ArgumentException($"Système {request.InsertAfterId} introuvable");

            var successor = predecessor.NextSystemId.HasValue
                ? await _db.StarSystems.FindAsync(predecessor.NextSystemId.Value)
                : null;

            _db.StarSystems.Add(newSystem);
            await _db.SaveChangesAsync();

            // Rechaîner : predecessor → newSystem → successor
            predecessor.NextSystemId = newSystem.Id;
            newSystem.PreviousSystemId = predecessor.Id;

            if (successor is not null)
            {
                newSystem.NextSystemId = successor.Id;
                successor.PreviousSystemId = newSystem.Id;
            }
        }

        await _db.SaveChangesAsync();
        await _db.Entry(newSystem).Reference(s => s.Architect).LoadAsync();
        return MapSystemToDto(newSystem, await GetOrderInChain(newSystem));
    }

    public async Task<StarSystemDto?> UpdateSystemAsync(int id, UpdateSystemRequest request)
    {
        var system = await _db.StarSystems
            .Include(s => s.Architect)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (system is null) return null;

        if (request.Name is not null)
            system.Name = request.Name;

        if (request.Type is not null)
            system.Type = Enum.Parse<SystemType>(request.Type, ignoreCase: true);

        if (request.Status is not null)
            system.Status = Enum.Parse<ColonizationStatus>(request.Status, ignoreCase: true);

        if (request.ArchitectId is not null)
            system.ArchitectId = request.ArchitectId == "" ? null : request.ArchitectId;

        system.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Recharger après update
        await _db.Entry(system).Reference(s => s.Architect).LoadAsync();
        var order = await GetOrderInChain(system);
        return MapSystemToDto(system, order);
    }

    // Déplacer un système dans la chaîne
    public async Task<StarSystemDto?> MoveSystemAsync(int id, int? insertAfterId)
    {
        var system = await _db.StarSystems.FindAsync(id);
        if (system is null) return null;

        // 1. Décrocher de la position actuelle
        var prev = system.PreviousSystemId.HasValue
            ? await _db.StarSystems.FindAsync(system.PreviousSystemId.Value) : null;
        var next = system.NextSystemId.HasValue
            ? await _db.StarSystems.FindAsync(system.NextSystemId.Value) : null;

        if (prev is not null) prev.NextSystemId = next?.Id;
        if (next is not null) next.PreviousSystemId = prev?.Id;

        system.PreviousSystemId = null;
        system.NextSystemId = null;
        await _db.SaveChangesAsync();

        // 2. Réinsérer à la nouvelle position
        if (insertAfterId is null)
        {
            // En tête
            var currentHead = await _db.StarSystems
                .FirstOrDefaultAsync(s => s.BridgeId == system.BridgeId
                                       && s.PreviousSystemId == null
                                       && s.Id != id);

            if (currentHead is not null)
            {
                system.NextSystemId = currentHead.Id;
                currentHead.PreviousSystemId = system.Id;
            }
        }
        else
        {
            var predecessor = await _db.StarSystems.FindAsync(insertAfterId);
            if (predecessor is null) return null;

            var successor = predecessor.NextSystemId.HasValue
                ? await _db.StarSystems.FindAsync(predecessor.NextSystemId.Value)
                : null;

            predecessor.NextSystemId = system.Id;
            system.PreviousSystemId = predecessor.Id;

            if (successor is not null)
            {
                system.NextSystemId = successor.Id;
                successor.PreviousSystemId = system.Id;
            }
        }

        system.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _db.Entry(system).Reference(s => s.Architect).LoadAsync();
        return MapSystemToDto(system, await GetOrderInChain(system));
    }

    // Calcule l'ordre dynamiquement en parcourant la chaîne
    private async Task<int> GetOrderInChain(StarSystem target)
    {
        var current = await _db.StarSystems
            .FirstOrDefaultAsync(s => s.BridgeId == target.BridgeId
                                   && s.PreviousSystemId == null);
        int order = 1;
        while (current is not null)
        {
            if (current.Id == target.Id) return order;
            order++;
            current = current.NextSystemId.HasValue
                ? await _db.StarSystems.FindAsync(current.NextSystemId.Value)
                : null;
        }
        return order;
    }

    public async Task<bool> DeleteSystemAsync(int id)
    {
        var system = await _db.StarSystems.FindAsync(id);
        if (system is null) return false;

        var bridgeId = system.BridgeId;

        var prev = system.PreviousSystemId.HasValue ? await _db.StarSystems.FindAsync(system.PreviousSystemId.Value) : null;
        var next = system.NextSystemId.HasValue ? await _db.StarSystems.FindAsync(system.NextSystemId.Value) : null;

        if (prev is not null) prev.NextSystemId = next?.Id;
        if (next is not null) next.PreviousSystemId = prev?.Id;

        _db.StarSystems.Remove(system);

        await _db.SaveChangesAsync();
        return true;
    }

    // ── Mappers privés ────────────────────────────────────────────────────

    private static BridgeDto MapToDto(Bridge b)
    {
        var systems = b.Systems            
            .Select(MapSystemToDto)
            .OrderBy(s => s.Order)
            .ToList();        

        var total = systems.Count;
        var done = systems.Count(s => s.Status == nameof(ColonizationStatus.FINI));

        return new BridgeDto(
            b.Id,
            b.Name,
            b.Description,
            b.CreatedBy?.CommanderName,
            systems,
            total > 0 ? (int)Math.Round((double)done / total * 100) : 0,
            b.CreatedAt
        );
    }
    private async Task<List<StarSystemDto>> GetOrderedSystemsAsync(int bridgeId)
    {
        var all = await _db.StarSystems
            .Include(s => s.Architect)
            .Where(s => s.BridgeId == bridgeId)
            .ToListAsync();

        var result = new List<StarSystemDto>();
        var current = all.FirstOrDefault(s => s.PreviousSystemId == null);
        int order = 1;

        while (current is not null)
        {
            result.Add(MapSystemToDto(current, order++));
            current = current.NextSystemId.HasValue
                ? all.FirstOrDefault(s => s.Id == current.NextSystemId.Value)
                : null;
        }

        return result;
    }

    private static StarSystemDto MapSystemToDto(StarSystem s, int order) => new(
        s.Id,
        s.Name,
        s.Type.ToString(),
        s.Status.ToString(),
        order,
        s.PreviousSystemId?.ToString(),
        s.NextSystemId?.ToString(),
        s.ArchitectId,
        s.Architect?.CommanderName,
        s.BridgeId,
        s.UpdatedAt
    );
}
