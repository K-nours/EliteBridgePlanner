using System.Text;
using System.Text.Json;
using EliteBridgePlanner.Server.Auth;
using EliteBridgePlanner.Server.Data;
using EliteBridgePlanner.Server.Data.Seed;
using EliteBridgePlanner.Server.Middleware;
using EliteBridgePlanner.Server.Models;
using EliteBridgePlanner.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var contentRoot = Directory.GetCurrentDirectory();
// Résolution du build Angular : depuis project dir, solution root, ou bin/Debug
var relPaths = new[] { "../elitebridgeplanner.client", "elitebridgeplanner.client", "../../../elitebridgeplanner.client" };
string? spaBrowserPath = null;
foreach (var rel in relPaths)
{
    var candidate = Path.GetFullPath(Path.Combine(contentRoot, rel, "dist", "elitebridgeplanner.client", "browser"));
    if (Directory.Exists(candidate)) { spaBrowserPath = candidate; break; }
}
var webRootPath = spaBrowserPath;
if (webRootPath is null)
    Console.WriteLine("[WARN] Build Angular introuvable. Lancez: cd elitebridgeplanner.client && ng build");
else
    Console.WriteLine($"[SPA] Fichiers statiques: {webRootPath}");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot,
    WebRootPath = webRootPath
});

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

// ── CORS — dev : tout localhost (ports variables ng serve) ; prod : origines fixes ──
builder.Services.AddCors(options =>
    options.AddPolicy("AngularDev", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(static origin =>
            {
                if (string.IsNullOrEmpty(origin)) return false;
                try
                {
                    var uri = new Uri(origin);
                    return uri.Host is "localhost" or "127.0.0.1";
                }
                catch (UriFormatException)
                {
                    return false;
                }
            });
        }
        else
        {
            policy.WithOrigins("https://localhost:61358", "https://localhost:4200", "https://127.0.0.1:4200", "http://localhost:4200", "http://127.0.0.1:4200");
        }

        policy.AllowAnyHeader().AllowAnyMethod();
    })
);

// ── HttpClient (Spansh API) ───────────────────────────────────────────────
builder.Services.AddHttpClient();

// ── Services métier (tous via leurs interfaces pour testabilité) ───────────
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBridgeService, BridgeService>();
builder.Services.AddScoped<ISpanshRouteService, SpanshRouteService>();
builder.Services.AddScoped<DataSeeder>();
builder.Services.AddSingleton<BridgeRouteStore>();

// ── Controllers + JSON ────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
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
// Anciens clients (cache navigateur) : proxy ng serve utilisait /spansh-api/api/* → même cible que SpanshProxyController.
const string legacySpanshPrefix = "/spansh-api/api";
app.Use((context, next) =>
{
    var path = context.Request.Path.Value;
    if (path is not null && path.StartsWith(legacySpanshPrefix, StringComparison.Ordinal))
    {
        var tail = path.Length > legacySpanshPrefix.Length ? path[legacySpanshPrefix.Length..] : "";
        context.Request.Path = "/api/spansh" + tail;
    }
    return next();
});

app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<LocalizationMiddleware>();

// Fichiers statiques : uniquement hors /api — sinon POST/GET /api/* peut être traité comme ressource statique (405 Method Not Allowed).
if (webRootPath != null)
{
    app.UseWhen(
        ctx => !ctx.Request.Path.StartsWithSegments("/api"),
        sub => sub.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(webRootPath) }));
}

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

// Fallback SPA : ne pas intercepter /api/* (sinon conflit de méthodes avec MapFallbackToFile sur POST, etc.).
if (webRootPath != null)
{
    app.MapFallback(async (HttpContext context) =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(Path.Combine(webRootPath, "index.html"));
    });
}

app.Run();

// Nécessaire pour que WebApplicationFactory fonctionne dans les tests d'intégration
public partial class Program { }
