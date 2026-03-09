namespace EliteBridgePlanner.Server.Services;

/// <summary>
/// Appelle l'API Spansh Colonisation, attend le résultat et retourne la liste des sauts.
/// </summary>
public interface ISpanshRouteService
{
    /// <summary>
    /// Lance un calcul de route Sol → Colonia (ou source/destination donnés),
    /// poll jusqu'à ce que le résultat soit prêt, retourne les jumps.
    /// </summary>
    Task<IReadOnlyList<SpanshJump>> GetRouteAsync(string source, string destination, CancellationToken ct = default);
}

public record SpanshJump(string Name, double Distance, double? X, double? Y, double? Z);
