using EliteBridgePlanner.Server.DTOs;

namespace EliteBridgePlanner.Server.Services;

/// <summary>Route « Pont galactique » partagée avec le dashboard 501 (mémoire processus).</summary>
public sealed class BridgeRouteStore
{
    private readonly object _lock = new();
    private BridgeRoutePayloadDto? _current;

    public void Set(BridgeRoutePayloadDto dto)
    {
        lock (_lock)
            _current = dto;
    }

    public BridgeRoutePayloadDto? Get()
    {
        lock (_lock)
            return _current;
    }

    public void Clear()
    {
        lock (_lock)
            _current = null;
    }
}
