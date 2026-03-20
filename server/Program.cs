using GuildDashboard.Server.Data;
using GuildDashboard.Server.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=localhost,1434;Database=GuildDashboardDb;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=False";

builder.Services.AddDbContext<GuildDashboardDbContext>(o =>
    o.UseSqlServer(connStr));

builder.Services.AddHttpClient<InaraApiService>();
builder.Services.AddHttpClient<InaraSquadronRosterService>();
builder.Services.AddScoped<InaraClient>();
builder.Services.AddScoped<GuildSystemsService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<CommandersService>();
builder.Services.AddScoped<SquadronSyncService>();
builder.Services.AddScoped<DataSeeder>();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

builder.Services.AddCors(c => c.AddPolicy("AllowAll", p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

app.UseCors("AllowAll");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GuildDashboardDbContext>();
    await db.Database.MigrateAsync();
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync();
}

app.MapControllers();

// Log config Inara au démarrage (valeur statique temporaire — voir README)
var inaraSquadronId = app.Configuration.GetValue<int?>("Squadron:InaraSquadronId");
if (inaraSquadronId.HasValue && inaraSquadronId.Value > 0)
    app.Logger.LogInformation("Squadron:InaraSquadronId chargé = {Id} (config statique temporaire)", inaraSquadronId.Value);
else
    app.Logger.LogWarning("Squadron:InaraSquadronId non configuré — le panneau CMDRs restera vide jusqu'à configuration");

app.Run();
