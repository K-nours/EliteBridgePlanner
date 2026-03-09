using EliteBridgePlanner.Server.DTOs;

namespace EliteBridgePlanner.Server.Services;

/// <summary>
/// Contrat du service métier Bridge + StarSystem.
/// Interface exposée aux controllers — permet le mock complet en tests NUnit.
/// </summary>
public interface IBridgeService
{
    Task<IEnumerable<BridgeDto>> GetAllBridgesAsync();
    Task<BridgeDto?> GetBridgeByIdAsync(int id);
    Task<BridgeDto> CreateBridgeAsync(CreateBridgeRequest request, string userId);

    Task<StarSystemDto> AddSystemAsync(CreateSystemRequest request);
    Task<StarSystemDto?> UpdateSystemAsync(int id, UpdateSystemRequest request);
    Task<bool> DeleteSystemAsync(int id);
    Task<StarSystemDto?> MoveSystemAsync(int id, int insertAfterId);

    /// <summary>Importe une route Spansh — crée un nouveau pont ou remplit un pont existant.</summary>
    Task<BridgeDto> ImportSpanshRouteAsync(string userId, string bridgeName, IEnumerable<SpanshSystemImport> systems, int? replaceBridgeId = null);
}

public record SpanshSystemImport(string Name, string Type);
