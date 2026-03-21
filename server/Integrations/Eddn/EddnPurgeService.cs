using GuildDashboard.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Integrations.Eddn;

/// <summary>Purge périodique des messages EDDN de plus de 7 jours.</summary>
public class EddnPurgeService : BackgroundService
{
    private const int RetentionDays = 7;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceProvider _services;
    private readonly ILogger<EddnPurgeService> _log;

    public EddnPurgeService(IServiceProvider services, ILogger<EddnPurgeService> log)
    {
        _services = services;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("EDDN purge démarrée — rétention {Days} jours, intervalle {Interval}h", RetentionDays, Interval.TotalHours);

        await PurgeAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "EDDN purge erreur");
            }

            await Task.Delay(Interval, stoppingToken);
        }

        _log.LogInformation("EDDN purge arrêtée");
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GuildDashboardDbContext>();

        var toDelete = await db.EddnRawMessages
            .Where(m => m.ReceivedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (toDelete > 0)
            _log.LogInformation("EDDN purge : {Count} messages supprimés (avant {Cutoff:yyyy-MM-dd})", toDelete, cutoff);
    }
}
