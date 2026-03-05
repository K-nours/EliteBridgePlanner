namespace EliteBridgePlanner.Server.Auth;

/// <summary>
/// Configuration JWT pour le POC.
///
/// MIGRATION VERS FRONTIER SSO (OAuth2 / OIDC) :
/// ─────────────────────────────────────────────
/// 1. Dans Program.cs, remplacer AddJwtBearer() par :
///      builder.Services.AddAuthentication()
///        .AddOpenIdConnect("Frontier", options => {
///            options.Authority  = "https://auth.frontier.co.uk";
///            options.ClientId   = "YOUR_CLIENT_ID";
///            options.ClientSecret = "YOUR_SECRET";
///            options.ResponseType = "code";
///            options.Scope.Add("openid");
///            options.Scope.Add("profile");
///        });
/// 2. Supprimer AuthController et AuthService (login géré par Frontier)
/// 3. Ajouter FrontierId (string) dans AppUser pour mapper le claim "sub"
/// 4. Tous les autres fichiers (services, controllers métier, DTOs) = inchangés
/// </summary>
public class JwtConfig
{
    public const string Section = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpirationDays { get; set; } = 7;
}
