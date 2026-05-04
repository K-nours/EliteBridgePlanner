using EliteBridgePlanner.Server.Data;
using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Models;
using EliteBridgePlanner.Server.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace EliteBridgePlanner.Server.Services.Implementations;

public class BridgeService : IBridgeService
{
    private readonly AppDbContext _db;

    // AppDbContext injecté — jamais instancié directement
    public BridgeService(AppDbContext db) => _db = db;

    private async Task<BridgeStarSystem?> GetNextInBridgeAsync(int bridgeStarSystemId, int bridgeId)
        => await _db.BridgeStarSystems
            .FirstOrDefaultAsync(bs => bs.BridgeId == bridgeId
                                    && bs.PreviousSystemId == bridgeStarSystemId);

    public async Task<IEnumerable<BridgeDto>> GetAllBridgesAsync()
    {
        var bridges = await _db.Bridges
            .Include(b => b.CreatedBy)
            .Include(b => b.Systems)
                .ThenInclude(bs => bs.StarSystem)
                    .ThenInclude(s => s.Architect)
            .AsNoTracking()
            .ToListAsync();

        return bridges.Select(MapToDto);
    }

    public async Task<BridgeDto?> GetBridgeByIdAsync(int id)
    {
        var bridge = await _db.Bridges
            .Include(b => b.CreatedBy)
            .Include(b => b.Systems)
                .ThenInclude(bs => bs.StarSystem)
                    .ThenInclude(s => s.Architect)
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
        // Chercher ou créer le StarSystem
        var starSystem = await _db.StarSystems.FirstOrDefaultAsync(s => s.Name == request.Name);

        if (starSystem is null)
        {
            starSystem = new StarSystem
            {
                Name = request.Name,
                ArchitectId = string.IsNullOrEmpty(request.ArchitectId) ? null : request.ArchitectId,
                X = request.X ?? 0,
                Y = request.Y ?? 0,
                Z = request.Z ?? 0,
                Status = Enum.Parse<ColonizationStatus>(request.Status, true)
            };
            _db.StarSystems.Add(starSystem);
            await _db.SaveChangesAsync();
        }

        // Créer l'association Bridge-StarSystem avec le rôle spécifique
        int? insertAfterId = await ResolveInsertAfterId(request.BridgeId, request.InsertAtIndex);

        var bridgeStarSystem = new BridgeStarSystem
        {
            BridgeId = request.BridgeId,
            StarSystemId = starSystem.Id,
            Type = Enum.Parse<SystemType>(request.Type, true)
           
        };

        await InsertInChain(bridgeStarSystem, insertAfterId);

        // Recharger avec ses relations
        await _db.Entry(bridgeStarSystem)
            .Reference(bs => bs.StarSystem)
            .LoadAsync();
        await _db.Entry(bridgeStarSystem.StarSystem)
            .Reference(s => s.Architect)
            .LoadAsync();

        return MapSystemToDto(bridgeStarSystem, request.InsertAtIndex);
    }

    /// <summary>
    /// Convertit un index 1-based en ID du BridgeStarSystem prédécesseur.
    /// InsertAtIndex = 1 → insérer en tête  → retourne null
    /// InsertAtIndex = 2 → insérer après le 1er élément → retourne Id du 1er
    /// InsertAtIndex = N → retourne l'Id du (N-1)ème élément
    /// </summary>
    private async Task<int?> ResolveInsertAfterId(int bridgeId, int insertAtIndex, int? excludeSystemId = null)
    {
        if (insertAtIndex <= 1) return null; // Insertion en tête

        var current = await _db.BridgeStarSystems
            .FirstOrDefaultAsync(bs => bs.BridgeId == bridgeId 
                                    && bs.PreviousSystemId == null
                                    && (excludeSystemId == null || bs.Id != excludeSystemId));

        int position = 1;
        while (current is not null && position < insertAtIndex - 1)
        {
            current = await _db.BridgeStarSystems
                .FirstOrDefaultAsync(bs => bs.BridgeId == bridgeId
                                        && bs.PreviousSystemId == current.Id
                                        && (excludeSystemId == null || bs.Id != excludeSystemId));
            position++;
        }

        return current?.Id;
    }

    private async Task InsertInChain(BridgeStarSystem bridgeStarSystem, int? insertAfterId, bool isNew = true)
    {
        if (isNew)
        {
            _db.BridgeStarSystems.Add(bridgeStarSystem);
            await _db.SaveChangesAsync();
        }
        else
        {
            // Détacher le système de sa position actuelle
            var actualSuccessor = await GetNextInBridgeAsync(bridgeStarSystem.Id, bridgeStarSystem.BridgeId);
            if (actualSuccessor is not null)
            {
                actualSuccessor.PreviousSystemId = bridgeStarSystem.PreviousSystemId;
            }
        }

        if (insertAfterId is null)
        {
            // Insertion en tête
            var currentHead = await _db.BridgeStarSystems
                .FirstOrDefaultAsync(bs => bs.BridgeId == bridgeStarSystem.BridgeId
                                        && bs.PreviousSystemId == null
                                        && bs.Id != bridgeStarSystem.Id);

            bridgeStarSystem.PreviousSystemId = null;

            if (currentHead is not null)
                currentHead.PreviousSystemId = bridgeStarSystem.Id;
        }
        else
        {
            // Trouver le successeur du prédécesseur
            var successor = await GetNextInBridgeAsync(insertAfterId.Value, bridgeStarSystem.BridgeId);

            bridgeStarSystem.PreviousSystemId = insertAfterId;

            if (successor is not null && successor.Id != bridgeStarSystem.Id)
                successor.PreviousSystemId = bridgeStarSystem.Id;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<StarSystemDto?> UpdateSystemAsync(int bridgeId, UpdateSystemRequest request)
    {
        // `id` est l'ID du StarSystem
        var system = await _db.StarSystems
            .Include(s => s.Architect)
            .FirstOrDefaultAsync(s => s.Id == request.starSystemId);

        if (system is null) return null;

        if (request.Name is not null)
            system.Name = request.Name;

        if (request.X is not null)
            system.X = request.X.Value;

        if (request.Y is not null)
            system.Y = request.Y.Value;

        if (request.Z is not null)
            system.Z = request.Z.Value;

        system.Status = request.Status is not null ? Enum.Parse<ColonizationStatus>(request.Status, true) : system.Status;

        if (request.ArchitectId is not null)
            system.ArchitectId = request.ArchitectId == "" ? null : request.ArchitectId;

        system.UpdatedAt = DateTime.UtcNow;
        


        await _db.Entry(system).Reference(s => s.Architect).LoadAsync();

        // Retourner le BridgeStarSystem du système pour l'aperçu
        var bridgeStarSystem = await _db.BridgeStarSystems
            .Include(bs => bs.StarSystem.Architect)
            .FirstAsync(bs => bs.BridgeId == bridgeId && bs.StarSystemId == request.starSystemId);

        bridgeStarSystem.Type = request.Type is not null ? Enum.Parse<SystemType>(request.Type, true) : bridgeStarSystem.Type;
        
        await _db.SaveChangesAsync();

        return bridgeStarSystem is null ? null : MapSystemToDto(bridgeStarSystem, 1);
    }

    // Déplacer un système dans la chaîne d'un pont
    public async Task<StarSystemDto?> MoveSystemAsync(int bridgeId,int starSystemId, int insertAtIndex)
    {
        // `id` est l'ID du BridgeStarSystem
        var bridgeStarSystem = await _db.BridgeStarSystems.FirstOrDefaultAsync(p=> p.BridgeId == bridgeId && p.StarSystemId == starSystemId);
        if (bridgeStarSystem is null) return null;

        // Étape 1 : Détacher de sa position actuelle
        var nextSystem = await GetNextInBridgeAsync(bridgeStarSystem.Id, bridgeStarSystem.BridgeId);
        if (nextSystem is not null)
        {
            nextSystem.PreviousSystemId = bridgeStarSystem.PreviousSystemId;
        }
        await _db.SaveChangesAsync();

        // Étape 2 : Calculer la nouvelle position
        int? insertAfterId = await ResolveInsertAfterId(bridgeStarSystem.BridgeId, insertAtIndex, excludeSystemId: bridgeStarSystem.Id);

        // Étape 3 : Réinsérer
        await InsertSystemAtPosition(bridgeStarSystem, insertAfterId);

        // Recharger les données
        await _db.Entry(bridgeStarSystem)
            .Reference(bs => bs.StarSystem)
            .LoadAsync();
        await _db.Entry(bridgeStarSystem.StarSystem)
            .Reference(s => s.Architect)
            .LoadAsync();

        return MapSystemToDto(bridgeStarSystem, insertAtIndex);
    }

    /// <summary>
    /// Réinsère un BridgeStarSystem détaché à une position donnée.
    /// </summary>
    private async Task InsertSystemAtPosition(BridgeStarSystem bridgeStarSystem, int? insertAfterId)
    {
        if (insertAfterId is null)
        {
            // Insertion en tête
            var currentHead = await _db.BridgeStarSystems
                .FirstOrDefaultAsync(bs => bs.BridgeId == bridgeStarSystem.BridgeId
                                        && bs.PreviousSystemId == null
                                        && bs.Id != bridgeStarSystem.Id);

            bridgeStarSystem.PreviousSystemId = null;

            if (currentHead is not null)
                currentHead.PreviousSystemId = bridgeStarSystem.Id;
        }
        else
        {
            // Insérer après le prédécesseur
            var successor = await GetNextInBridgeAsync(insertAfterId.Value, bridgeStarSystem.BridgeId);

            bridgeStarSystem.PreviousSystemId = insertAfterId;

            if (successor is not null && successor.Id != bridgeStarSystem.Id)
                successor.PreviousSystemId = bridgeStarSystem.Id;
        }

        await _db.SaveChangesAsync();
    }

    private async Task<List<StarSystemDto>> GetOrderedSystemsInBridgeAsync(int bridgeId)
    {
        var all = await _db.BridgeStarSystems
            .Include(bs => bs.StarSystem.Architect)
            .Where(bs => bs.BridgeId == bridgeId)
            .ToListAsync();

        var result = new List<StarSystemDto>();

        var current = all.FirstOrDefault(bs => bs.PreviousSystemId == null);
        int order = 1;

        while (current is not null)
        {
            result.Add(MapSystemToDto(current, order++));
            current = all.FirstOrDefault(bs => bs.PreviousSystemId == current.Id);
        }

        return result;
    }

    public async Task<bool> DeleteSystemAsync(int starSystemId)
    {
        // `id` est l'ID du BridgeStarSystem
        var bridgeStarSystem = await _db.BridgeStarSystems.FirstOrDefaultAsync(p => p.StarSystemId == starSystemId);
        if (bridgeStarSystem is null) return false;

        var nextSystem = await _db.BridgeStarSystems
            .FirstOrDefaultAsync(bs => bs.BridgeId == bridgeStarSystem.BridgeId 
                                    && bs.PreviousSystemId == bridgeStarSystem.Id);

        if (nextSystem is not null) 
            nextSystem.PreviousSystemId = bridgeStarSystem.PreviousSystemId;

        _db.BridgeStarSystems.Remove(bridgeStarSystem);
        await _db.SaveChangesAsync();

        return true;
    }

    // ── Mappers privés ────────────────────────────────────────────────────

    private static BridgeDto MapToDto(Bridge b)
    {
        var systems = GetOrderedSystems(b);
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
        var result = new List<StarSystemDto>();
        var current = bridge.Systems.FirstOrDefault(bs => bs.PreviousSystemId == null);
        int order = 1;

        while (current is not null)
        {
            result.Add(MapSystemToDto(current, order++));
            current = bridge.Systems.FirstOrDefault(bs => bs.PreviousSystemId == current.Id);
        }

        return result;
    }

    private static StarSystemDto MapSystemToDto(BridgeStarSystem bs, int order) => new(
        bs.StarSystem.Id,
        bs.StarSystem.Name,
        bs.Type.ToString(),
        bs.StarSystem.Status.ToString(),
        order,
        bs.PreviousSystemId?.ToString(),
        bs.StarSystem.ArchitectId,
        bs.StarSystem.Architect?.CommanderName,
        bs.BridgeId,
        bs.StarSystem.X,
        bs.StarSystem.Y,
        bs.StarSystem.Z,
        bs.UpdatedAt
    );
}
