using System.Text;
using EliteBridgePlanner.Server.Auth;
using EliteBridgePlanner.Server.Data;
using EliteBridgePlanner.Server.Data.Seed;
using EliteBridgePlanner.Server.Middleware;
using EliteBridgePlanner.Server.Models;
using EliteBridgePlanner.Server.Services.Contracts;
using EliteBridgePlanner.Server.Services.Implementations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── Base de données (Docker SQL Server) ───────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null
        )
    )
);

// ── Identity ──────────────────────────────────────────────────────────────
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// ── JWT (POC — voir JwtConfig.cs pour la roadmap migration Frontier SSO) ──
var jwtSection = builder.Configuration.GetSection(JwtConfig.Section);
builder.Services.Configure<JwtConfig>(jwtSection);
var jwtConfig = jwtSection.Get<JwtConfig>()!;

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtConfig.Issuer,
            ValidAudience            = jwtConfig.Audience,
            IssuerSigningKey         = new SymmetricSecurityKey(
                                           Encoding.UTF8.GetBytes(jwtConfig.Secret)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// ── CORS — Angular dev server ─────────────────────────────────────────────
builder.Services.AddCors(options =>
    options.AddPolicy("AngularDev", policy =>
        policy.WithOrigins("https://localhost:61358", "https://localhost:4200", "https://127.0.0.1:4200", "http://localhost:4200", "http://127.0.0.1:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
    )
);

// ── Services métier (tous via leurs interfaces pour testabilité) ───────────
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBridgeService, BridgeService>();
builder.Services.AddScoped<DataSeeder>();

// ── Controllers + JSON ────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Sérialiser les enums en string (PLANIFIE, FINI...) au lieu de int
        options.JsonSerializerOptions.Converters
            .Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// ── Swagger / OpenAPI ─────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// ─────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Migration EF Core + Seed automatiques au démarrage ────────────────────
using (var scope = app.Services.CreateScope())
{
    var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();

    // Applique toutes les migrations en attente (crée la DB si inexistante)
    await db.Database.MigrateAsync();

    // Seed uniquement en développement
    if (app.Environment.IsDevelopment())
        await seeder.SeedAsync();
}

// ── Pipeline HTTP ─────────────────────────────────────────────────────────
app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors("AngularDev");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Proxy SPA Angular (déjà configuré par le template VS)
app.MapFallbackToFile("index.html");

app.Run();

// Nécessaire pour que WebApplicationFactory fonctionne dans les tests d'intégration
public partial class Program { }
