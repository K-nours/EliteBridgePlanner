using GuildDashboard.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace GuildDashboard.Server.Controllers;

[ApiController]
[Route("api/integrations/frontier")]
public class FrontierController : ControllerBase
{
    private readonly FrontierAuthService _auth;
    private readonly FrontierTokenStore _store;
    private readonly FrontierUserService _userService;

    public FrontierController(FrontierAuthService auth, FrontierTokenStore store, FrontierUserService userService)
    {
        _auth = auth;
        _store = store;
        _userService = userService;
    }

    /// <summary>POST /api/integrations/frontier/logout — déconnexion Frontier (efface le token).</summary>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        _store.ClearToken();
        return Ok(new { success = true });
    }

    /// <summary>GET /api/integrations/frontier/me — état de connexion Frontier (connecté/non, commander, squadron).</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var profile = await _userService.GetProfileAsync(ct);
        if (profile == null)
            return Ok(new { connected = false });
        return Ok(new
        {
            connected = true,
            commander = profile.CommanderName,
            squadron = profile.SquadronName,
            customerId = profile.FrontierCustomerId,
        });
    }

    /// <summary>GET /api/integrations/frontier/profile — profil CMDR complet (CAPI), mappé squadron→guild.</summary>
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var profile = await _userService.GetProfileAsync(ct);
        return profile == null ? NotFound(new { message = "Aucun profil Frontier (connexion OAuth requise)." }) : Ok(profile);
    }

    /// <summary>GET /api/integrations/frontier/debug-url — retourne l'URL d'autorisation complète pour inspection (éviter 406).</summary>
    [HttpGet("debug-url")]
    public IActionResult DebugAuthorizationUrl()
    {
        if (!_auth.IsAuthConfigured)
            return BadRequest(new { error = "Frontier:ClientId et Frontier:RedirectUri requis." });
        var debug = _auth.GetAuthorizationUrlDebug();
        if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return Ok(debug);
        var html = BuildDebugUrlHtml(debug);
        return Content(html, "text/html; charset=utf-8");
    }

    private static string BuildDebugUrlHtml(object debug)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(debug, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var escaped = System.Net.WebUtility.HtmlEncode(json);
        var css = "body{font-family:monospace;margin:1rem;background:#1a1a2e;color:#e6edf3;} pre{background:#2d2d44;padding:1rem;overflow-x:auto;border-radius:6px;} a{color:#6ee7b7;}";
        return $"<!DOCTYPE html><html><head><meta charset='utf-8'><title>Frontier OAuth Debug</title><style>{css}</style></head><body><h1>URL OAuth Frontier (debug)</h1><pre>{escaped}</pre><p><a href='/api/integrations/frontier/start'>→ Lancer OAuth</a></p></body></html>";
    }

    /// <summary>GET /api/integrations/frontier/start — redirection 302 directe vers Frontier login (lance le flux OAuth).</summary>
    [HttpGet("start")]
    public IActionResult StartOAuth()
    {
        if (!_auth.IsAuthConfigured)
            return BadRequest("Frontier:ClientId et Frontier:RedirectUri requis. Configurer appsettings.");
        var (authUrl, state, verifier) = _auth.BuildAuthorizationUrl();
        _store.SetPendingAuth(state, verifier);
        return Redirect(authUrl);
    }

    /// <summary>GET /api/integrations/frontier/test — diagnostic auth + CAPI. HTML si Accept:text/html.</summary>
    [HttpGet("test")]
    public async Task<IActionResult> Test([FromQuery] bool startOAuth = false, CancellationToken ct = default)
    {
        var result = new
        {
            authConfigured = _auth.IsAuthConfigured,
            clientIdConfigured = !string.IsNullOrWhiteSpace(_auth.ClientId),
            redirectUriConfigured = !string.IsNullOrWhiteSpace(_auth.RedirectUri),
            clientSecretConfigured = !string.IsNullOrWhiteSpace(_auth.ClientSecret),
            oauthStartUrl = (string?)null,
            tokenObtained = false,
            capiTest = (object?)null,
            conclusion = "",
        };

        // 1. Config status
        var hasToken = _store.GetToken() != null;
        object? capiTest = null;

        // 2. Start OAuth flow if requested
        if (startOAuth && _auth.IsAuthConfigured)
        {
            var (authUrl, state, verifier) = _auth.BuildAuthorizationUrl();
            _store.SetPendingAuth(state, verifier);
            var oauthPayload = new
            {
                authConfigured = true,
                clientIdConfigured = true,
                redirectUriConfigured = true,
                clientSecretConfigured = !string.IsNullOrWhiteSpace(_auth.ClientSecret),
                oauthStartUrl = authUrl,
                tokenObtained = hasToken,
                capiTest,
                conclusion = "Cliquer sur le lien ci-dessous pour vous connecter à Frontier.",
            };
            if (!Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return Content(BuildTestHtml(true, hasToken, authUrl, null, null, null, "Cliquer sur le lien ci-dessous pour vous connecter à Frontier."), "text/html; charset=utf-8");
            return Ok(oauthPayload);
        }

        // 3. Token existant : afficher le rapport complet (du store ou en recalculant)
        if (hasToken)
        {
            var token = _store.GetToken()!;
            var report = _store.GetReport();
            if (report == null)
            {
                report = await _auth.RunFullValidationAsync(token, ct);
                _store.SetToken(token, report);
            }

            var payload = new
            {
                authConfigured = _auth.IsAuthConfigured,
                clientIdConfigured = !string.IsNullOrWhiteSpace(_auth.ClientId),
                redirectUriConfigured = !string.IsNullOrWhiteSpace(_auth.RedirectUri),
                clientSecretConfigured = !string.IsNullOrWhiteSpace(_auth.ClientSecret),
                oauthStartUrl = (string?)null,
                tokenObtained = true,
                report = new
                {
                    oAuthOk = report.OAuthOk,
                    tokenOk = report.TokenOk,
                    accessTokenObtained = report.AccessTokenObtained,
                    refreshTokenObtained = report.RefreshTokenObtained,
                    tokenExpiresIn = report.TokenExpiresIn,
                    scope = report.Scope,
                    meOk = report.MeOk,
                    me = new { report.MeResult.HttpStatusCode, report.MeResult.ResponseLength, report.MeResult.DetectedFields, preview = Truncate(report.MeResult.ResponsePreview, 300) },
                    decodeOk = report.DecodeOk,
                    decode = new { report.DecodeResult.HttpStatusCode, report.DecodeResult.ResponseLength, report.DecodeResult.DetectedFields, preview = Truncate(report.DecodeResult.ResponsePreview, 300) },
                    capiOk = report.CapiOk,
                    capi = new { report.CapiResult.HttpStatusCode, report.CapiResult.ResponseLength, report.CapiResult.DetectedFields, preview = Truncate(report.CapiResult.ResponsePreview, 300) },
                    conclusion = report.Conclusion,
                    dataAvailable = report.CapiOk ? report.CapiResult.DetectedFields : null,
                },
            };
            if (!Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return Content(BuildTestReportHtml(report), "text/html; charset=utf-8");
            return Ok(payload);
        }

        // 4. No token, just config
        var msg = !_auth.IsAuthConfigured
            ? "Configurer Frontier:ClientId et Frontier:RedirectUri dans appsettings. Puis ?startOAuth=true pour lancer le flux."
            : "Pas de token. Appeler ?startOAuth=true pour obtenir l'URL OAuth, terminer le flux via /callback.";
        var configPayload = new
        {
            result.authConfigured,
            result.clientIdConfigured,
            result.redirectUriConfigured,
            result.clientSecretConfigured,
            result.oauthStartUrl,
            result.tokenObtained,
            result.capiTest,
            conclusion = msg,
        };
        if (!Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return Content(BuildTestHtml(result.authConfigured, result.tokenObtained, null, null, null, null, msg), "text/html; charset=utf-8");
        return Ok(configPayload);
    }

    private static string BuildTestReportHtml(FrontierValidationReport r)
    {
        var css = "body{font-family:system-ui;margin:0;padding:1rem;background:#1a1a2e;color:#e6edf3;} .card{background:#2d2d44;padding:1rem;border-radius:8px;margin-bottom:1rem;} h1,h2{color:#6ee7b7;} .ok{color:#6ee7b7;} .err{color:#f87171;} pre{font-size:0.85em;overflow-x:auto;} a{color:#93c5fd;}";
        var row = (string lab, bool ok, int status, int len, List<string>? fields) =>
            $"<tr><td>{lab}</td><td class='{(ok ? "ok" : "err")}'>{status}</td><td>{len} bytes</td><td>{string.Join(", ", fields ?? new List<string>())}</td></tr>";
        var table = "<table style='border-collapse:collapse;'><tr><th>Étape</th><th>HTTP</th><th>Taille</th><th>Champs</th></tr>" +
            $"<tr><td>Token</td><td class='{(r.TokenOk ? "ok" : "err")}'>—</td><td>access+refresh</td><td>expires_in={r.TokenExpiresIn}s</td></tr>" +
            row("/me", r.MeOk, r.MeResult.HttpStatusCode, r.MeResult.ResponseLength, r.MeResult.DetectedFields) +
            row("/decode", r.DecodeOk, r.DecodeResult.HttpStatusCode, r.DecodeResult.ResponseLength, r.DecodeResult.DetectedFields) +
            row("CAPI /profile", r.CapiOk, r.CapiResult.HttpStatusCode, r.CapiResult.ResponseLength, r.CapiResult.DetectedFields) +
            "</table>";
        var previews = $"<details><summary>/me</summary><pre>{System.Net.WebUtility.HtmlEncode(Truncate(r.MeResult.ResponsePreview, 500))}</pre></details>" +
            $"<details><summary>/decode</summary><pre>{System.Net.WebUtility.HtmlEncode(Truncate(r.DecodeResult.ResponsePreview, 500))}</pre></details>" +
            $"<details><summary>CAPI</summary><pre>{System.Net.WebUtility.HtmlEncode(Truncate(r.CapiResult.ResponsePreview, 500))}</pre></details>";
        return $"<!DOCTYPE html><html><head><meta charset='utf-8'><title>Frontier Test</title><style>{css}</style></head><body>" +
            "<div class='card'><h1>Frontier OAuth2 / CAPI</h1><p><a href='/api/integrations/frontier/start'>Démarrer OAuth</a> | <a href='/api/integrations/frontier/test'>Rafraîchir</a></p></div>" +
            "<div class='card'><h2>Compte rendu</h2><p><strong>" + System.Net.WebUtility.HtmlEncode(r.Conclusion) + "</strong></p></div>" +
            "<div class='card'><h2>Détail</h2>" + table + "</div>" +
            "<div class='card'><h2>Aperçus JSON</h2>" + previews + "</div></body></html>";
    }

    private static string BuildTestHtml(bool authConfigured, bool tokenObtained, string? oauthStartUrl, int? capiStatus, int? capiLen, IReadOnlyList<string>? capiFields, string conclusion)
    {
        var css = "body{font-family:system-ui;margin:0;padding:1rem;background:#1a1a2e;color:#e6edf3;} .card{background:#2d2d44;padding:1rem;border-radius:8px;margin-bottom:1rem;} h1{color:#6ee7b7;} .ok{color:#6ee7b7;} .err{color:#f87171;} a{color:#93c5fd;}";
        var auth = authConfigured ? "<span class='ok'>Oui</span>" : "<span class='err'>Non</span>";
        var token = tokenObtained ? "<span class='ok'>Oui</span>" : "<span class='err'>Non</span>";
        var oauthLink = !string.IsNullOrEmpty(oauthStartUrl)
            ? $"<p><strong>Option A (redirection directe):</strong> <a href='/api/integrations/frontier/start'>Démarrer OAuth (redirige vers Frontier)</a></p><p><strong>Option B (nouvel onglet):</strong> <a href='{System.Net.WebUtility.HtmlEncode(oauthStartUrl)}' target='_blank'>Ouvrir dans un nouvel onglet</a></p>"
            : "";
        var capiSection = capiStatus.HasValue
            ? $"<div class='card'><h2>CAPI /profile</h2><p>HTTP {capiStatus.Value} | {capiLen ?? 0} bytes</p><p>Champs: {string.Join(", ", capiFields ?? Array.Empty<string>())}</p></div>"
            : "";
        return $"<!DOCTYPE html><html><head><meta charset='utf-8'><title>Frontier Diagnostic</title><style>{css}</style></head><body>" +
            "<div class='card'><h1>Frontier OAuth2 / CAPI</h1><p>Auth configuré: " + auth + " | Token obtenu: " + token + "</p>" + oauthLink +
            "<p><a href='/api/integrations/frontier/start'>Démarrer OAuth</a> (redirige vers Frontier) | <a href='/api/integrations/frontier/test'>Rafraîchir</a></p></div>" +
            capiSection +
            "<div class='card'><p>" + System.Net.WebUtility.HtmlEncode(conclusion) + "</p></div></body></html>";
    }

    /// <summary>GET /api/integrations/frontier/callback — callback OAuth après auth utilisateur.</summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(error))
        {
            return Ok(new { success = false, error, message = "L'utilisateur a refusé ou une erreur OAuth s'est produite." });
        }

        if (string.IsNullOrEmpty(code))
        {
            return BadRequest(new { success = false, error = "code manquant" });
        }

        var consumed = _store.ConsumePendingAuth(state ?? "");
        if (consumed == null || string.IsNullOrEmpty(consumed.Value.Verifier))
        {
            var msg = "Tentative OAuth expirée ou remplacée. Relancez la connexion Frontier.";
            var frontendBase = _auth.FrontendBaseUrl;
            if (!string.IsNullOrWhiteSpace(frontendBase))
            {
                var redirectUri = frontendBase.TrimEnd('/') + "/?frontier=error&message=" + Uri.EscapeDataString(msg);
                return Redirect(redirectUri);
            }
            return BadRequest(new { success = false, error = "state_invalid", message = msg });
        }

        var verifier = consumed.Value.Verifier!;

        var token = await _auth.ExchangeCodeAsync(code, verifier, ct);
        if (token == null)
        {
            return Ok(new { success = false, error = "Échec échange code/token. Vérifier les logs." });
        }

        var report = await _auth.RunFullValidationAsync(token, ct);
        _store.SetToken(token, report);

        _ = await _userService.GetProfileAsync(ct);

        var baseUrl = _auth.FrontendBaseUrl;
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            return Redirect(baseUrl.TrimEnd('/') + "/?frontier=success");
        }

        if (!Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return Content(BuildCallbackReportHtml(report), "text/html; charset=utf-8");
        return Ok(BuildCallbackReportPayload(report));
    }

    private static object BuildCallbackReportPayload(FrontierValidationReport r)
    {
        return new
        {
            success = true,
            report = new
            {
                oAuthOk = r.OAuthOk,
                tokenOk = r.TokenOk,
                accessTokenObtained = r.AccessTokenObtained,
                refreshTokenObtained = r.RefreshTokenObtained,
                tokenExpiresIn = r.TokenExpiresIn,
                scope = r.Scope,
                meOk = r.MeOk,
                me = new { r.MeResult.HttpStatusCode, r.MeResult.ResponseLength, r.MeResult.DetectedFields, preview = Truncate(r.MeResult.ResponsePreview, 300) },
                decodeOk = r.DecodeOk,
                decode = new { r.DecodeResult.HttpStatusCode, r.DecodeResult.ResponseLength, r.DecodeResult.DetectedFields, preview = Truncate(r.DecodeResult.ResponsePreview, 300) },
                capiOk = r.CapiOk,
                capi = new { r.CapiResult.HttpStatusCode, r.CapiResult.ResponseLength, r.CapiResult.DetectedFields, preview = Truncate(r.CapiResult.ResponsePreview, 300) },
                conclusion = r.Conclusion,
                dataAvailable = r.CapiOk ? r.CapiResult.DetectedFields : null,
            },
        };
    }

    private static string BuildCallbackReportHtml(FrontierValidationReport r)
    {
        var css = "body{font-family:system-ui;margin:0;padding:1rem;background:#1a1a2e;color:#e6edf3;} .card{background:#2d2d44;padding:1rem;border-radius:8px;margin-bottom:1rem;} h1,h2{color:#6ee7b7;} .ok{color:#6ee7b7;} .err{color:#f87171;} pre{font-size:0.85em;overflow-x:auto;} a{color:#93c5fd;}";
        var row = (string lab, bool ok, int status, int len, List<string>? fields) =>
            $"<tr><td>{lab}</td><td class='{(ok ? "ok" : "err")}'>{status}</td><td>{len} bytes</td><td>{string.Join(", ", fields ?? new List<string>())}</td></tr>";
        var table = "<table style='border-collapse:collapse;'><tr><th>Étape</th><th>HTTP</th><th>Taille</th><th>Champs</th></tr>" +
            $"<tr><td>Token</td><td class='{(r.TokenOk ? "ok" : "err")}'>—</td><td>access+refresh reçus</td><td>expires_in={r.TokenExpiresIn}s</td></tr>" +
            row("/me", r.MeOk, r.MeResult.HttpStatusCode, r.MeResult.ResponseLength, r.MeResult.DetectedFields) +
            row("/decode", r.DecodeOk, r.DecodeResult.HttpStatusCode, r.DecodeResult.ResponseLength, r.DecodeResult.DetectedFields) +
            row("CAPI /profile", r.CapiOk, r.CapiResult.HttpStatusCode, r.CapiResult.ResponseLength, r.CapiResult.DetectedFields) +
            "</table>";
        var previews = $"<details><summary>/me</summary><pre>{System.Net.WebUtility.HtmlEncode(Truncate(r.MeResult.ResponsePreview, 500))}</pre></details>" +
            $"<details><summary>/decode</summary><pre>{System.Net.WebUtility.HtmlEncode(Truncate(r.DecodeResult.ResponsePreview, 500))}</pre></details>" +
            $"<details><summary>CAPI</summary><pre>{System.Net.WebUtility.HtmlEncode(Truncate(r.CapiResult.ResponsePreview, 500))}</pre></details>";
        return $"<!DOCTYPE html><html><head><meta charset='utf-8'><title>Frontier Validation</title><style>{css}</style></head><body>" +
            "<div class='card'><h1>Compte rendu Frontier</h1><p><strong>" + System.Net.WebUtility.HtmlEncode(r.Conclusion) + "</strong></p></div>" +
            "<div class='card'><h2>Résumé</h2>" + table + "</div>" +
            "<div class='card'><h2>Aperçus JSON</h2>" + previews + "</div>" +
            "<div class='card'><p><a href='/api/integrations/frontier/test'>Voir test complet</a></p></div></body></html>";
    }

    private static string Truncate(string? s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "...";
}
