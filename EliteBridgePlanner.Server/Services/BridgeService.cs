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
        // Parcourir la chaîne pour trouver le prédécesseur à l'index demandé
        int? insertAfterId = await ResolveInsertAfterId(request.BridgeId, request.InsertAtIndex);

        var newSystem = new StarSystem
        {
            Name = request.Name,
            Type = Enum.Parse<SystemType>(request.Type, true),
            Status = Enum.Parse<ColonizationStatus>(request.Status, true),
            ArchitectId = string.IsNullOrEmpty(request.ArchitectId) ? null : request.ArchitectId,
            BridgeId = request.BridgeId
        };
        
        await InsertInChain(newSystem, insertAfterId); // isNew = true par défaut
        await _db.Entry(newSystem).Reference(s => s.Architect).LoadAsync();
        return MapSystemToDto(newSystem, request.InsertAtIndex);
    }

    /// <summary>
    /// Convertit un index 1-based en ID du prédécesseur.
    /// InsertAtIndex = 1 → insérer en tête  → retourne null
    /// InsertAtIndex = 2 → insérer après le 1er élément → retourne Id du 1er
    /// InsertAtIndex = N → retourne l'Id du (N-1)ème élément
    /// </summary>
    private async Task<int?> ResolveInsertAfterId(int bridgeId, int insertAtIndex)
    {
        if (insertAtIndex <= 1) return null; // Insertion en tête

        var current = await _db.StarSystems
            .FirstOrDefaultAsync(s => s.BridgeId == bridgeId && s.PreviousSystemId == null);

        int position = 1;
        while (current is not null && position < insertAtIndex)
        {
            current = current.NextSystemId.HasValue
                ? await _db.StarSystems.FindAsync(current.NextSystemId.Value)
                : null;
            position++;
        }

        // Si on dépasse la fin de liste → insérer en queue
        return current?.Id;
    }

    /// <summary>
    /// Insère un nouveau système dans la chaîne après le prédécesseur donné.
    /// insertAfterId = null → insertion en tête.
    /// </summary>
    private async Task InsertInChain(StarSystem system, int? insertAfterId, bool isNew = true)
    {
        if (isNew)
        {
            _db.StarSystems.Add(system);
            await _db.SaveChangesAsync(); // Génère l'Id
        }

        if (insertAfterId is null)
        {
            // Insertion en tête
            var currentHead = await _db.StarSystems
                .FirstOrDefaultAsync(s => s.BridgeId == system.BridgeId
                                       && s.PreviousSystemId == null
                                       && s.Id != system.Id); // Exclure lui-même

            if (currentHead is not null)
            {
                system.NextSystemId = currentHead.Id;
                currentHead.PreviousSystemId = system.Id;
            }
        }
        else
        {
            await DetachFromChain(system);
            var newPredecessor = await _db.StarSystems.FindAsync(insertAfterId)
                ?? throw new ArgumentException($"Système prédécesseur {insertAfterId} introuvable");
            system.NextSystemId = newPredecessor.NextSystemId;
            newPredecessor.NextSystemId = system.Id;
            var newSuccessor = system.NextSystemId.HasValue
                ? await _db.StarSystems.FindAsync(system.NextSystemId.Value)
                : null;
            if (newSuccessor is not null)
            {
                newSuccessor.PreviousSystemId = system.Id;
                system.NextSystemId = newSuccessor.Id;
            }
            else
            {
                system.NextSystemId = null;
            }
        }

        await _db.SaveChangesAsync();
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
    public async Task<StarSystemDto?> MoveSystemAsync(int id, int insertAtIndex)
    {
        var system = await _db.StarSystems.FindAsync(id);
        if (system is null) return null;
        int? insertAfterId = await ResolveInsertAfterId(system.BridgeId, insertAtIndex);
        await InsertInChain(system, insertAfterId, isNew: false); // ← système existant
        return MapSystemToDto(system, insertAtIndex);
    }

    private async Task DetachFromChain(StarSystem system)
    {
        var prev = system.PreviousSystemId.HasValue
            ? await _db.StarSystems.FindAsync(system.PreviousSystemId.Value) : null;
        var next = system.NextSystemId.HasValue
            ? await _db.StarSystems.FindAsync(system.NextSystemId.Value) : null;

        if (prev is not null) prev.NextSystemId = next?.Id;
        if (next is not null) next.PreviousSystemId = prev?.Id;

        system.PreviousSystemId = null;
        system.NextSystemId = null;
        await _db.SaveChangesAsync();
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
        //var systems = b.Systems            
        //    .Select(MapSystemToDto)
        //    .OrderBy(s => s.Order)
        //    .ToList();        

        var systems= GetOrderedSystems(b);

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
    private static List<StarSystemDto> GetOrderedSystems(Bridge bridge)
    {
        //var all = await _db.StarSystems
        //    .Include(s => s.Architect)
        //    .Where(s => s.BridgeId == bridgeId)
        //    .ToListAsync();

        var result = new List<StarSystemDto>();
        var current = bridge.Systems.FirstOrDefault(s => s.PreviousSystemId == null);
        int order = 1;

        while (current is not null)
        {
            result.Add(MapSystemToDto(current, order++));
            current = current.NextSystemId.HasValue
                ? bridge.Systems.FirstOrDefault(s => s.Id == current.NextSystemId.Value)
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
