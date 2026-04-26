using System.Net.Sockets;
using GuildDashboard.Server.Data;
using GuildDashboard.Server.Integrations.Eddn;
using GuildDashboard.Server.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<HostOptions>(o =>
{
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=localhost,1434;Database=GuildDashboardDb;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=False";

builder.Services.AddDbContext<GuildDashboardDbContext>(o =>
    o.UseSqlServer(connStr));

builder.Services.AddHttpClient<InaraApiService>();
builder.Services.AddHttpClient<InaraSquadronRosterService>();
builder.Services.AddHttpClient<EliteBgsApiService>();
builder.Services.AddHttpClient<EdsmApiService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<InaraFactionService>();
builder.Services.AddScoped<InaraClient>();
builder.Services.AddDataProtection();
builder.Services.AddScoped<FrontierAuthService>();
builder.Services.AddScoped<FrontierUserService>();
builder.Services.AddScoped<FrontierOAuthSessionService>();
builder.Services.AddSingleton<FrontierTokenStore>();
builder.Services.AddHostedService<FrontierOAuthRehydrationHostedService>();
builder.Services.AddSingleton<CurrentGuildService>();
builder.Services.AddScoped<DeclaredChantiersService>();
builder.Services.AddScoped<FrontierLogisticsInventoryService>();
builder.Services.AddScoped<GuildSystemsService>();
builder.Services.AddScoped<DiplomaticPipelineService>();
builder.Services.AddScoped<BgsSyncService>();
builder.Services.AddScoped<EliteBgsDiagnosticService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<CommandersService>();
builder.Services.AddScoped<SquadronSyncService>();
builder.Services.AddScoped<DataSeeder>();
builder.Services.AddScoped<GuildSystemsSeedLoader>();
builder.Services.AddScoped<GuildSystemsImportService>();
builder.Services.AddScoped<EdsmDeltaEnrichmentService>();
builder.Services.AddScoped<EdsmCoordsEnrichmentService>();
builder.Services.AddSingleton<SystemsImportProgressStore>();
builder.Services.AddSingleton<EddnStatusService>();
builder.Services.AddSingleton<FrontierJournalBackfillService>();
builder.Services.AddSingleton<FrontierJournalParseService>();
builder.Services.AddSingleton<FrontierJournalUnifiedSyncService>();
builder.Services.AddScoped<FrontierJournalImportExportService>();
builder.Services.AddSingleton<InaraApiUserSettingsStore>();
builder.Services.AddScoped<EddnMessageStore>();
builder.Services.AddHostedService<EddnListenerService>();
builder.Services.AddHostedService<EddnPurgeService>();
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.PropertyNameCaseInsensitive = true; // tolère commanders/Commanders côté payload
    });

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

// CORS doit être après UseRouting mais avant UseAuthorization (si présent).
// Pour preflight OPTIONS, le middleware CORS doit intercepter avant toute auth.
app.UseRouting();
app.UseCors();

// SQL Docker peut être « unhealthy » ou encore en recovery : on retente plutôt que de planter au Migrate.
const int dbStartupMaxAttempts = 12;
const int dbStartupDelayMs = 2500;
for (var attempt = 1; attempt <= dbStartupMaxAttempts; attempt++)
{
    try
    {
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GuildDashboardDbContext>();
            await db.Database.MigrateAsync();
            // IsFromSeed, IsControlled : colonnes explicites. Idempotent pour DB existantes.
            await db.Database.ExecuteSqlRawAsync(@"
        IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ControlledSystems')
        AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ControlledSystems') AND name = 'IsFromSeed')
        ALTER TABLE [ControlledSystems] ADD [IsFromSeed] bit NOT NULL DEFAULT 1;
    ");
            await db.Database.ExecuteSqlRawAsync(@"
        IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ControlledSystems')
        AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ControlledSystems') AND name = 'IsControlled')
        ALTER TABLE [ControlledSystems] ADD [IsControlled] bit NOT NULL DEFAULT 1;
    ");
            var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
            await seeder.SeedAsync();

            var currentGuildId = scope.ServiceProvider.GetRequiredService<CurrentGuildService>().CurrentGuildId;
            var loader = scope.ServiceProvider.GetRequiredService<GuildSystemsSeedLoader>();
            var result = await loader.LoadAsync(currentGuildId);
            app.Logger.LogInformation(
                "GuildSystems seed: totalSource={TotalSource} inserted={Inserted} ignored={Ignored} totalFinal={TotalFinal} missing={MissingCount}",
                result.TotalSource, result.Inserted, result.Ignored, result.TotalFinal, result.MissingNames?.Count ?? 0);
        }

        break;
    }
    catch (Exception ex) when (attempt < dbStartupMaxAttempts && IsLikelySqlConnectivityFailure(ex))
    {
        app.Logger.LogWarning(
            ex,
            "Connexion SQL indisponible ou instable — tentative {Attempt}/{Max}, nouvel essai dans {Delay}s",
            attempt,
            dbStartupMaxAttempts,
            dbStartupDelayMs / 1000);
        await Task.Delay(dbStartupDelayMs);
    }
}

static bool IsLikelySqlConnectivityFailure(Exception ex)
{
    for (var e = ex; e != null; e = e.InnerException)
    {
        if (e is SqlException or IOException or SocketException)
            return true;
    }
    return false;
}

app.MapControllers();

app.Logger.LogInformation(
    "API GuildDashboard : routes Frontier chantiers — GET chantiers-inspect, chantiers-declare-evaluate, chantiers-declared GET/POST/me/others");

// Log config Inara au démarrage (valeur statique temporaire — voir README)
var inaraSquadronId = app.Configuration.GetValue<int?>("Squadron:InaraSquadronId");
if (inaraSquadronId.HasValue && inaraSquadronId.Value > 0)
    app.Logger.LogInformation("Squadron:InaraSquadronId chargé = {Id} (config statique temporaire)", inaraSquadronId.Value);
else
    app.Logger.LogWarning("Squadron:InaraSquadronId non configuré — le panneau CMDRs restera vide jusqu'à configuration");

// Log Frontier OAuth — callback HTTPS requis par Frontier
var frontierRedirect = app.Configuration["Frontier:RedirectUri"];
if (!string.IsNullOrEmpty(frontierRedirect))
    app.Logger.LogInformation("Frontier OAuth callback: {RedirectUri} (HTTPS requis)", frontierRedirect);

// --show-db : affiche 10 systèmes en base (Id, Name, CoordsX/Y/Z) puis quitte. Pour vérifier les coordonnées.
if (args.Contains("--show-db"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GuildDashboardDbContext>();
    var guildId = scope.ServiceProvider.GetRequiredService<CurrentGuildService>().CurrentGuildId;
    var list = await db.GuildSystems
        .AsNoTracking()
        .Where(s => s.GuildId == guildId)
        .OrderBy(s => s.Name)
        .Take(15)
        .Select(s => new { s.Id, s.Name, s.Category, s.InfluencePercent, s.CoordsX, s.CoordsY, s.CoordsZ })
        .ToListAsync();
    var withCoords = list.Count(s => s.CoordsX != null && s.CoordsY != null && s.CoordsZ != null);
    var json = System.Text.Json.JsonSerializer.Serialize(new { total = list.Count, withCoords, systems = list }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    return;
}

app.Run();
