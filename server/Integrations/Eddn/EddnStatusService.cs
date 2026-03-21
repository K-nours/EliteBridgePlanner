namespace GuildDashboard.Server.Integrations.Eddn;

/// <summary>État en mémoire du listener EDDN pour l'endpoint de diagnostic.</summary>
public class EddnStatusService
{
    private volatile bool _isConnected;
    private long _receivedCount;
    private DateTime? _lastReceivedAt;

    public bool IsConnected => _isConnected;
    public long ReceivedCount => _receivedCount;
    public DateTime? LastReceivedAt => _lastReceivedAt;

    public void SetConnected(bool value) => _isConnected = value;
    public void SetReceivedCount(long value) => _receivedCount = value;
    public void SetLastReceivedAt(DateTime value) => _lastReceivedAt = value;
}
