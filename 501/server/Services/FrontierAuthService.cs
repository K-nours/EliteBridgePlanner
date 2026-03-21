using System.Security.Cryptography;
using System.Text;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Service OAuth2 Frontier pour Elite Dangerous CAPI.
/// Flux PKCE : auth.frontierstore.net/auth → callback → token → CAPI companion.orerve.net
/// </summary>
/// <remarks>
/// Scopes : auth capi. Audience : frontier.
/// Réf. https://user.frontierstore.net/developer/docs, Athanasius/fd-api
/// </remarks>
public class FrontierAuthService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<FrontierAuthService> _log;

    private const string AuthBase = "https://auth.frontierstore.net";
    private const string CapiHost = "https://companion.orerve.net";

    public FrontierAuthService(IConfiguration config, IHttpClientFactory httpFactory, ILogger<FrontierAuthService> log)
    {
        _config = config;
        _httpFactory = httpFactory;
        _log = log;
    }

    public string? ClientId => _config["Frontier:ClientId"];
    public string? ClientSecret => _config["Frontier:ClientSecret"];
    public string? RedirectUri => _config["Frontier:RedirectUri"];
    public string? FrontendBaseUrl => _config["Frontier:FrontendBaseUrl"];

    /// <summary>Vérifie si la config auth est suffisante (ClientId + RedirectUri).</summary>
    public bool IsAuthConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(RedirectUri);

    /// <summary>Génère l'URL pour démarrer le flux OAuth2 PKCE. Retourne (url, state, codeVerifier) pour le callback.</summary>
    /// <remarks>Ordre et format exigés par Frontier : audience, scope, response_type=code, client_id, code_challenge, code_challenge_method=S256, state, redirect_uri (URL encodée).</remarks>
    public (string AuthUrl, string State, string CodeVerifier) BuildAuthorizationUrl()
    {
        var clientId = ClientId;
        var redirectUri = RedirectUri;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(redirectUri))
        {
            _log.LogError("[FrontierAuth] BuildAuthorizationUrl: ClientId ou RedirectUri manquant");
            throw new InvalidOperationException("Frontier:ClientId et Frontier:RedirectUri doivent être configurés.");
        }

        var codeVerifier = GenerateBase64UrlSafe(32, stripPadding: false);
        var codeChallenge = ComputeSha256Base64Url(codeVerifier);
        var state = GenerateBase64UrlSafe(32, stripPadding: true);

        var redirectUriTrimmed = redirectUri.Trim();
        var redirectUriEncoded = Uri.EscapeDataString(redirectUriTrimmed);
        var scopeEncoded = Uri.EscapeDataString("auth capi");
        var queryParams = new[]
        {
            "audience=frontier",
            "scope=" + scopeEncoded,
            "response_type=code",
            "client_id=" + Uri.EscapeDataString(clientId.Trim()),
            "code_challenge=" + Uri.EscapeDataString(codeChallenge),
            "code_challenge_method=S256",
            "state=" + Uri.EscapeDataString(state),
            "redirect_uri=" + redirectUriEncoded,
        };
        var query = string.Join("&", queryParams);
        var url = $"{AuthBase}/auth?{query}";

        _log.LogInformation("[FrontierAuth] Authorize URL (complète): {Url}", url);
        _log.LogInformation("[FrontierAuth] Params: audience=frontier scope={Scope} response_type=code client_id_len={ClientIdLen} code_challenge_len={ChLen} redirect_uri={RedirectUri}",
            scopeEncoded, clientId.Trim().Length, codeChallenge.Length, redirectUriTrimmed);

        if (codeChallenge.Length != 43)
            _log.LogWarning("[FrontierAuth] code_challenge length={Len} (attendu 43 pour SHA256 base64url sans padding)", codeChallenge.Length);
        if (string.IsNullOrEmpty(clientId))
            _log.LogError("[FrontierAuth] client_id vide !");

        return (url, state, codeVerifier);
    }

    /// <summary>Génère une URL d'autorisation pour inspection (debug). Ne pas utiliser pour le flux réel — pas de verifier stocké.</summary>
    public object GetAuthorizationUrlDebug()
    {
        var clientId = ClientId ?? "";
        var redirectUri = RedirectUri ?? "";
        var codeVerifier = GenerateBase64UrlSafe(32, stripPadding: false);
        var codeChallenge = ComputeSha256Base64Url(codeVerifier);
        var state = GenerateBase64UrlSafe(32, stripPadding: true);

        var redirectUriEncoded = Uri.EscapeDataString(redirectUri.Trim());
        var scopeEncoded = Uri.EscapeDataString("auth capi");
        var queryParams = new[]
        {
            "audience=frontier",
            "scope=" + scopeEncoded,
            "response_type=code",
            "client_id=" + Uri.EscapeDataString(clientId.Trim()),
            "code_challenge=" + Uri.EscapeDataString(codeChallenge),
            "code_challenge_method=S256",
            "state=" + Uri.EscapeDataString(state),
            "redirect_uri=" + redirectUriEncoded,
        };
        var query = string.Join("&", queryParams);
        var url = $"{AuthBase}/auth?{query}";

        return new
        {
            fullUrl = url,
            paramsCheck = new
            {
                audience = "frontier",
                scope = "auth%20capi",
                response_type = "code",
                client_id = string.IsNullOrEmpty(clientId) ? "VIDE!" : $"{clientId.Length} chars",
                client_id_configured = !string.IsNullOrWhiteSpace(clientId),
                code_challenge = $"{codeChallenge.Length} chars (attendu 43)",
                code_challenge_method = "S256",
                redirect_uri_raw = redirectUri,
                redirect_uri_encoded = redirectUriEncoded,
                expected_redirect_uri = "https://localhost:7294/api/integrations/frontier/callback",
                match = redirectUri.Trim() == "https://localhost:7294/api/integrations/frontier/callback",
            },
        };
    }

    /// <summary>Échange le code d'autorisation contre un access_token.</summary>
    public async Task<FrontierTokenResult?> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct = default)
    {
        var url = $"{AuthBase}/token";
        _log.LogInformation("[FrontierAuth] Token request: url={Url}", url);

        using var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        var body = new Dictionary<string, string>
        {
            ["redirect_uri"] = RedirectUri!,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = codeVerifier,
            ["client_id"] = ClientId!,
        };
        var content = new FormUrlEncodedContent(body);

        try
        {
            var response = await client.PostAsync(url, content, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            _log.LogInformation("[FrontierAuth] Token response: status={Status} len={Len}", (int)response.StatusCode, json.Length);

            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("[FrontierAuth] Token failed: {Status} body={Body}", (int)response.StatusCode, json);
                return null;
            }

            var result = System.Text.Json.JsonSerializer.Deserialize<FrontierTokenResponse>(json);
            if (result == null || string.IsNullOrEmpty(result.AccessToken))
                return null;

            return new FrontierTokenResult(
                result.AccessToken,
                result.RefreshToken ?? "",
                result.ExpiresIn,
                result.TokenType ?? "Bearer",
                result.Scope);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[FrontierAuth] Token request failed");
            return null;
        }
    }

    /// <summary>Rafraîchit l'access_token avec le refresh_token. Nécessite ClientSecret.</summary>
    public async Task<FrontierTokenResult?> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var url = $"{AuthBase}/token";
        var secret = ClientSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            _log.LogWarning("[FrontierAuth] Refresh impossible : Frontier:ClientSecret non configuré");
            return null;
        }

        using var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId!,
            ["client_secret"] = secret,
            ["refresh_token"] = refreshToken,
        };
        var content = new FormUrlEncodedContent(body);

        try
        {
            var response = await client.PostAsync(url, content, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("[FrontierAuth] Refresh failed: {Status} body={Body}", (int)response.StatusCode, json);
                return null;
            }
            var result = System.Text.Json.JsonSerializer.Deserialize<FrontierTokenResponse>(json);
            if (result == null || string.IsNullOrEmpty(result.AccessToken))
                return null;
            return new FrontierTokenResult(result.AccessToken, result.RefreshToken ?? refreshToken, result.ExpiresIn, result.TokenType ?? "Bearer", result.Scope);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[FrontierAuth] Refresh failed");
            return null;
        }
    }

    /// <summary>Récupère le JSON brut d'un endpoint CAPI (ex. /profile). Pour parsing complet.</summary>
    public async Task<(int StatusCode, string? Body)> FetchCapiRawAsync(string accessToken, string capiEndpoint = "/profile", CancellationToken ct = default)
    {
        var url = $"{CapiHost}{capiEndpoint}";
        using var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        try
        {
            var response = await client.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return ((int)response.StatusCode, body);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[FrontierCapi] Fetch failed: {Url}", url);
            return (0, null);
        }
    }

    /// <summary>Appelle un endpoint CAPI avec le token. Retourne le diagnostic (URL, status, taille, aperçu).</summary>
    public async Task<FrontierCapiTestResult> TestCapiAsync(string accessToken, string capiEndpoint = "/profile", CancellationToken ct = default)
    {
        var url = $"{CapiHost}{capiEndpoint}";
        var result = new FrontierCapiTestResult
        {
            Url = url,
            Endpoint = capiEndpoint,
            HttpStatusCode = 0,
            ResponseLength = 0,
            ResponsePreview = "",
            DetectedFields = new List<string>(),
        };

        using var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

        try
        {
            var response = await client.GetAsync(url, ct);
            result.HttpStatusCode = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(ct);
            result.ResponseLength = body.Length;
            result.ResponsePreview = body.Length > 500 ? body[..500] + "..." : body;

            if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(body))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    result.DetectedFields = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
                }
                catch { /* ignore */ }
            }

            _log.LogInformation("[FrontierCapi] {Endpoint} status={Status} len={Len} fields=[{Fields}]",
                capiEndpoint, result.HttpStatusCode, result.ResponseLength, string.Join(", ", result.DetectedFields));
        }
        catch (TaskCanceledException ex)
        {
            result.ErrorMessage = ex.InnerException == null ? "Timeout" : ex.Message;
            _log.LogWarning(ex, "[FrontierCapi] Timeout or cancelled: {Url}", url);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _log.LogError(ex, "[FrontierCapi] Request failed: {Url}", url);
        }

        return result;
    }

    private static string GenerateBase64UrlSafe(int byteCount, bool stripPadding = false)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        var b64 = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_');
        if (stripPadding) b64 = b64.TrimEnd('=');
        return b64;
    }

    private static string ComputeSha256Base64Url(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        var b64 = Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return b64;
    }

    /// <summary>GET /me et /decode sur auth.frontierstore.net — vérification du token.</summary>
    public async Task<FrontierAuthEndpointResult> CallMeAsync(string accessToken, CancellationToken ct = default)
    {
        return await CallAuthEndpointAsync($"{AuthBase}/me", accessToken, "me", ct);
    }

    public async Task<FrontierAuthEndpointResult> CallDecodeAsync(string accessToken, CancellationToken ct = default)
    {
        return await CallAuthEndpointAsync($"{AuthBase}/decode", accessToken, "decode", ct);
    }

    private async Task<FrontierAuthEndpointResult> CallAuthEndpointAsync(string url, string accessToken, string stepName, CancellationToken ct = default)
    {
        var result = new FrontierAuthEndpointResult { Url = url, Step = stepName };
        using var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

        try
        {
            var response = await client.GetAsync(url, ct);
            result.HttpStatusCode = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(ct);
            result.ResponseLength = body.Length;
            result.ResponsePreview = body.Length > 400 ? body[..400] + "..." : body;

            if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(body))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    result.DetectedFields = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
                }
                catch { /* ignore */ }
            }

            _log.LogInformation("[FrontierAuth] {Step} status={Status} len={Len} fields=[{Fields}]",
                stepName, result.HttpStatusCode, result.ResponseLength, string.Join(", ", result.DetectedFields ?? new List<string>()));
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _log.LogWarning(ex, "[FrontierAuth] {Step} failed: {Url}", stepName, url);
        }
        return result;
    }

    /// <summary>Exécute la validation complète : token, /me, /decode, CAPI /profile. Log chaque étape.</summary>
    public async Task<FrontierValidationReport> RunFullValidationAsync(FrontierTokenResult token, CancellationToken ct = default)
    {
        var report = new FrontierValidationReport
        {
            OAuthOk = true,
            TokenOk = true,
            AccessTokenObtained = !string.IsNullOrEmpty(token.AccessToken),
            RefreshTokenObtained = !string.IsNullOrEmpty(token.RefreshToken),
            TokenExpiresIn = token.ExpiresIn,
            TokenType = token.TokenType,
            Scope = token.Scope,
        };

        _log.LogInformation("[FrontierValidation] Démarrage: accessToken={Len} refreshToken={RLen} expiresIn={Exp}",
            token.AccessToken?.Length ?? 0, token.RefreshToken?.Length ?? 0, token.ExpiresIn);

        report.MeResult = await CallMeAsync(token.AccessToken ?? "", ct);
        report.MeOk = report.MeResult.HttpStatusCode >= 200 && report.MeResult.HttpStatusCode < 300;
        _log.LogInformation("[FrontierValidation] /me → HTTP {Status} ok={Ok}", report.MeResult.HttpStatusCode, report.MeOk);

        report.DecodeResult = await CallDecodeAsync(token.AccessToken ?? "", ct);
        report.DecodeOk = report.DecodeResult.HttpStatusCode >= 200 && report.DecodeResult.HttpStatusCode < 300;
        _log.LogInformation("[FrontierValidation] /decode → HTTP {Status} ok={Ok}", report.DecodeResult.HttpStatusCode, report.DecodeOk);

        report.CapiResult = await TestCapiAsync(token.AccessToken ?? "", "/profile", ct);
        report.CapiOk = report.CapiResult.HttpStatusCode >= 200 && report.CapiResult.HttpStatusCode < 300;
        _log.LogInformation("[FrontierValidation] CAPI /profile → HTTP {Status} ok={Ok} fields=[{Fields}]",
            report.CapiResult.HttpStatusCode, report.CapiOk, string.Join(", ", report.CapiResult.DetectedFields ?? new List<string>()));

        report.BuildConclusion();
        _log.LogInformation("[FrontierValidation] Conclusion: {Conclusion}", report.Conclusion);
        return report;
    }

    private class FrontierTokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }
}

public record FrontierTokenResult(string AccessToken, string RefreshToken, int ExpiresIn, string TokenType, string? Scope = null);

public class FrontierAuthEndpointResult
{
    public string Url { get; set; } = "";
    public string Step { get; set; } = "";
    public int HttpStatusCode { get; set; }
    public int ResponseLength { get; set; }
    public string ResponsePreview { get; set; } = "";
    public List<string>? DetectedFields { get; set; }
    public string? ErrorMessage { get; set; }
}

public class FrontierValidationReport
{
    public bool OAuthOk { get; set; }
    public bool TokenOk { get; set; }
    public bool AccessTokenObtained { get; set; }
    public bool RefreshTokenObtained { get; set; }
    public int TokenExpiresIn { get; set; }
    public string? TokenType { get; set; }
    public string? Scope { get; set; }
    public bool MeOk { get; set; }
    public FrontierAuthEndpointResult MeResult { get; set; } = new();
    public bool DecodeOk { get; set; }
    public FrontierAuthEndpointResult DecodeResult { get; set; } = new();
    public bool CapiOk { get; set; }
    public FrontierCapiTestResult CapiResult { get; set; } = new();
    public string Conclusion { get; set; } = "";

    public void BuildConclusion()
    {
        var parts = new List<string>
        {
            OAuthOk ? "OAuth Frontier: OK" : "OAuth Frontier: ÉCHEC",
            TokenOk && AccessTokenObtained ? "Token: OK (access_token reçu)" : "Token: ÉCHEC",
            RefreshTokenObtained ? "Refresh token: reçu" : "Refresh token: non reçu",
            MeOk ? "/me: OK" : $"/me: ÉCHEC (HTTP {MeResult.HttpStatusCode})",
            DecodeOk ? "/decode: OK" : $"/decode: ÉCHEC (HTTP {DecodeResult.HttpStatusCode})",
            CapiOk ? "CAPI: OK" : $"CAPI: ÉCHEC (HTTP {CapiResult.HttpStatusCode})",
        };
        if (CapiOk && CapiResult.DetectedFields?.Count > 0)
            parts.Add($"Données CAPI: {string.Join(", ", CapiResult.DetectedFields)}");
        Conclusion = string.Join(" | ", parts);
    }
}

public class FrontierCapiTestResult
{
    public string Url { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public int HttpStatusCode { get; set; }
    public int ResponseLength { get; set; }
    public string ResponsePreview { get; set; } = "";
    public List<string> DetectedFields { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
