namespace EliteBridgePlanner.Server.DTOs;

/// <summary>Point de route (contrat 501 / BridgePlanner). lng = coordonnée ED X, lat = ED Z, y = ED Y (Ly).</summary>
public sealed class BridgeRoutePointDto
{
    public string Id { get; set; } = "";
    /// <summary>Coordonnée « latitude » galactique (ED Z, Ly).</summary>
    public double Lat { get; set; }
    /// <summary>Coordonnée « longitude » galactique (ED X, Ly).</summary>
    public double Lng { get; set; }
    /// <summary>Altitude galactique ED (Y, Ly).</summary>
    public double Y { get; set; }
    public string Type { get; set; } = "";
    /// <summary>Couleur d’affichage (#rrggbb), calculée côté BridgePlanner.</summary>
    public string ColorHex { get; set; } = "";
}

/// <summary>Route complète en mémoire (pas de persistance SQL).</summary>
public sealed class BridgeRoutePayloadDto
{
    public List<BridgeRoutePointDto> Points { get; set; } = new();
    public string? Source { get; set; }
}
