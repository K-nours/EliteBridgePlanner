using Microsoft.Extensions.Hosting;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Au démarrage, recharge les tokens OAuth Frontier depuis la base vers le store mémoire
/// pour que les appels CAPI live fonctionnent sans nouveau login.
/// </summary>
public sealed class FrontierOAuthRehydrationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FrontierOAuthRehydrationHostedService> _log;

    public FrontierOAuthRehydrationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<FrontierOAuthRehydrationHostedService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<FrontierOAuthSessionService>();
            await svc.RehydrateFromDatabaseAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[FrontierOAuthRehydration] Échec rechargement session persistée au démarrage");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
