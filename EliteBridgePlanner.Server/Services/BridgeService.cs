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

    private async Task<StarSystem?> GetNextAsync(int systemId, int bridgeId)
        => await _db.StarSystems
            .FirstOrDefaultAsync(s => s.BridgeId == bridgeId
                                   && s.PreviousSystemId == systemId);

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
    private async Task<int?> ResolveInsertAfterId(int bridgeId, int insertAtIndex, int? excludeSystemId = null)
    {
        if (insertAtIndex <= 1) return null; // Insertion en tête

        var current = await _db.StarSystems
            .FirstOrDefaultAsync(s => s.BridgeId == bridgeId 
                                   && s.PreviousSystemId == null
                                   && (excludeSystemId == null || s.Id != excludeSystemId));

        int position = 1;
        while (current is not null && position < insertAtIndex - 1)
        {
            current = await _db.StarSystems.FirstOrDefaultAsync(p => p.PreviousSystemId == current.Id
                                                                  && (excludeSystemId == null || p.Id != excludeSystemId));
            position++;
        }

        // Si on dépasse la fin de liste → insérer en queue
        return current?.Id;
    }

    private async Task InsertInChain(StarSystem system, int? insertAfterId, bool isNew = true)
    {
        if (isNew)
        {
            _db.StarSystems.Add(system);
            await _db.SaveChangesAsync();
        }
        else
        {
            // Si le système était déjà chaîné, le détacher d'abord
            var actualSuccessor = await GetNextAsync(system.Id, system.BridgeId);
            if (actualSuccessor is not null)
            {
                actualSuccessor.PreviousSystemId = system.PreviousSystemId;
            }
        }

        if (insertAfterId is null)
        {
            // Insertion en tête — trouver l'actuelle tête et la faire pointer vers newSystem
            var currentHead = await _db.StarSystems
                .FirstOrDefaultAsync(s => s.BridgeId == system.BridgeId
                                       && s.PreviousSystemId == null
                                       && s.Id != system.Id);

            system.PreviousSystemId = null;

            if (currentHead is not null)
                currentHead.PreviousSystemId = system.Id;
        }
        else
        {
            // Trouver l'actuel suivant du prédécesseur
            var successor = await GetNextAsync(insertAfterId.Value, system.BridgeId);

            // Brancher system après son nouveau prédécesseur
            system.PreviousSystemId = insertAfterId;

            // Le successeur du prédécesseur pointe maintenant vers system
            if (successor is not null && successor.Id != system.Id)
                successor.PreviousSystemId = system.Id;
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
        var systems = await GetOrderedSystemsAsync(system.BridgeId);
        return systems.FirstOrDefault(s => s.Id == id);
    }

    // Déplacer un système dans la chaîne
    public async Task<StarSystemDto?> MoveSystemAsync(int id, int insertAtIndex)
    {
        var system = await _db.StarSystems.FindAsync(id);
        if (system is null) return null;

        // Étape 1 : Détacher le système de sa position actuelle
        var nextSystem = await GetNextAsync(system.Id, system.BridgeId);
        if (nextSystem is not null)
        {
            nextSystem.PreviousSystemId = system.PreviousSystemId;
        }
        await _db.SaveChangesAsync(); // Sauvegarder le détachement

        // Étape 2 : Calculer insertAfterId sur la liste SANS le système (en l'excluant explicitement)
        int? insertAfterId = await ResolveInsertAfterId(system.BridgeId, insertAtIndex, excludeSystemId: system.Id);

        // Étape 3 : Réinsérer à la bonne position
        await InsertSystemAtPosition(system, insertAfterId);

        return MapSystemToDto(system, insertAtIndex);
    }

    /// <summary>
    /// Réinsère un système détaché à une position donnée.
    /// Le système est supposé être détaché (ne pas être dans la chaîne).
    /// </summary>
    private async Task InsertSystemAtPosition(StarSystem system, int? insertAfterId)
    {
        if (insertAfterId is null)
        {
            // Insertion en tête
            var currentHead = await _db.StarSystems
                .FirstOrDefaultAsync(s => s.BridgeId == system.BridgeId
                                       && s.PreviousSystemId == null
                                       && s.Id != system.Id);

            system.PreviousSystemId = null;

            if (currentHead is not null)
                currentHead.PreviousSystemId = system.Id;
        }
        else
        {
            // Insérer après le prédécesseur
            var successor = await GetNextAsync(insertAfterId.Value, system.BridgeId);

            system.PreviousSystemId = insertAfterId;

            if (successor is not null && successor.Id != system.Id)
                successor.PreviousSystemId = system.Id;
        }

        await _db.SaveChangesAsync();
    }

    private async Task<List<StarSystemDto>> GetOrderedSystemsAsync(int bridgeId)
    {
        var all = await _db.StarSystems
            .Include(s => s.Architect)
            .Where(s => s.BridgeId == bridgeId)
            .ToListAsync();

        var result = new List<StarSystemDto>();
        // Tête = aucun prédécesseur
        var current = all.FirstOrDefault(s => s.PreviousSystemId == null);
        int order = 1;

        while (current is not null)
        {
            result.Add(MapSystemToDto(current, order++));
            // Suivant = celui dont PreviousSystemId == current.Id
            current = all.FirstOrDefault(s => s.PreviousSystemId == current.Id);
        }

        return result;
    }

    

    public async Task<BridgeDto> ImportSpanshRouteAsync(string userId, string bridgeName, IEnumerable<SpanshSystemImport> systems, int? replaceBridgeId = null)
    {
        var list = systems.ToList();
        if (list.Count == 0) throw new ArgumentException("Aucun système à importer.");

        Bridge bridge;
        if (replaceBridgeId.HasValue)
        {
            bridge = await _db.Bridges.FindAsync(replaceBridgeId.Value)
                ?? throw new ArgumentException($"Pont {replaceBridgeId} introuvable.");
            var existing = await _db.StarSystems.Where(s => s.BridgeId == bridge.Id).ToListAsync();
            _db.StarSystems.RemoveRange(existing);
            bridge.Name = bridgeName;
            bridge.Description = $"Import Spansh — {list.Count} systèmes";
            await _db.SaveChangesAsync();
        }
        else
        {
            bridge = new Bridge
            {
                Name = bridgeName,
                Description = $"Import Spansh — {list.Count} systèmes",
                CreatedByUserId = userId
            };
            _db.Bridges.Add(bridge);
            await _db.SaveChangesAsync();
        }

        var entities = list.Select(s => new StarSystem
        {
            Name = s.Name,
            Type = Enum.Parse<SystemType>(s.Type, true),
            Status = ColonizationStatus.PLANIFIE,
            BridgeId = bridge.Id,
            PreviousSystemId = null,
            ArchitectId = null
        }).ToList();

        _db.StarSystems.AddRange(entities);
        await _db.SaveChangesAsync();

        for (var i = 1; i < entities.Count; i++)
        {
            entities[i].PreviousSystemId = entities[i - 1].Id;
        }
        await _db.SaveChangesAsync();

        return (await GetBridgeByIdAsync(bridge.Id))!;
    }

    public async Task<bool> DeleteSystemAsync(int id)
    {
        var system = await _db.StarSystems.FindAsync(id);
        if (system is null) return false;

        var bridgeId = system.BridgeId;

        //var prev = system.PreviousSystemId.HasValue ? await _db.StarSystems.FindAsync(system.PreviousSystemId.Value) : null;
        var next = await _db.StarSystems.FirstOrDefaultAsync(s => s.BridgeId == bridgeId && s.PreviousSystemId == system.Id);

        if (next is not null) next.PreviousSystemId = system.PreviousSystemId;

        _db.StarSystems.Remove(system);

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task ClearAllSystemsAsync(int bridgeId)
    {
        var systems = await _db.StarSystems
            .Where(s => s.BridgeId == bridgeId)
            .ToListAsync();
        _db.StarSystems.RemoveRange(systems);
        await _db.SaveChangesAsync();
    }

    // ── Mappers privés ────────────────────────────────────────────────────

    private static BridgeDto MapToDto(Bridge b)
    {
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

        var result = new List<StarSystemDto>();
        var current = bridge.Systems.FirstOrDefault(s => s.PreviousSystemId == null);
        int order = 1;

        while (current is not null)
        {
            result.Add(MapSystemToDto(current, order++));
            current = bridge.Systems.FirstOrDefault(s => s.PreviousSystemId == current.Id);                
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
        s.ArchitectId,
        s.Architect?.CommanderName,
        s.BridgeId,
        s.UpdatedAt
    );
}
