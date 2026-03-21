using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace GuildDashboard.Server.Services;

/// <summary>Service de diagnostic pour tester l'API Elite BGS sans affecter le flux de production.</summary>
public class EliteBgsDiagnosticService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<EliteBgsDiagnosticService> _log;

    private const string DefaultBaseUrl = "https://elitebgs.app/api/ebgs/v5";
    private const int TestTimeoutSeconds = 20;

    public EliteBgsDiagnosticService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<EliteBgsDiagnosticService> log)
    {
        _httpFactory = httpFactory;
        _config = config;
        _log = log;
    }

    public async Task<EliteBgsTestResult> TestAsync(string factionName, CancellationToken ct = default)
    {
        var baseUrl = _config["Bgs:EliteBgsBaseUrl"] ?? DefaultBaseUrl;
        var url = $"{baseUrl}/factions?name={Uri.EscapeDataString(factionName)}";

        var result = new EliteBgsTestResult
        {
            Url = url,
            Method = "GET",
            FactionName = factionName,
            TimeoutSeconds = TestTimeoutSeconds,
            UserAgent = "GuildDashboard-Diagnostic/1.0",
        };

        _log.LogInformation("[EliteBgsDiagnostic] Test URL={Url} faction={FactionName} timeout={Timeout}s",
            url, factionName, TestTimeoutSeconds);

        using var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(TestTimeoutSeconds);
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("User-Agent", result.UserAgent);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await client.GetAsync(url, ct);
            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
            result.HttpStatusCode = (int)response.StatusCode;

            var content = await response.Content.ReadAsStringAsync(ct);
            result.ResponseLength = content.Length;
            result.ResponsePreview = content.Length > 0
                ? (content.Length > 300 ? content[..300] + "..." : content)
                : "(vide)";

            if (!response.IsSuccessStatusCode)
            {
                result.ErrorType = "http_non_200";
                result.ErrorMessage = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                _log.LogWarning("[EliteBgsDiagnostic] Échec: {Error} durée={Duration}ms",
                    result.ErrorMessage, result.DurationMs);
                return result;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                result.ErrorType = "empty_response";
                result.ErrorMessage = "Réponse vide";
                _log.LogWarning("[EliteBgsDiagnostic] Réponse vide durée={Duration}ms", result.DurationMs);
                return result;
            }

            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                result.HasValidJson = true;
                if (root.TryGetProperty("docs", out var docs))
                {
                    result.DocsCount = docs.GetArrayLength();
                    if (result.DocsCount == 0)
                    {
                        result.ErrorType = "empty_docs";
                        result.ErrorMessage = "docs[] vide ou faction introuvable";
                    }
                }
                else
                {
                    result.ErrorType = "unexpected_structure";
                    result.ErrorMessage = "Propriété 'docs' absente. Clés: " + string.Join(", ", root.EnumerateObject().Select(p => p.Name));
                }
            }
            catch (JsonException ex)
            {
                result.ErrorType = "json_invalid";
                result.ErrorMessage = ex.Message;
                result.HasValidJson = false;
                _log.LogWarning("[EliteBgsDiagnostic] JSON invalide: {Msg}", ex.Message);
            }

            if (string.IsNullOrEmpty(result.ErrorType))
            {
                _log.LogInformation("[EliteBgsDiagnostic] Succès: HTTP {Status} durée={Duration}ms taille={Size} docs={Docs}",
                    result.HttpStatusCode, result.DurationMs, result.ResponseLength, result.DocsCount);
            }

            return result;
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
            result.ErrorType = "cancelled";
            result.ErrorMessage = "Requête annulée";
            _log.LogWarning("[EliteBgsDiagnostic] Annulé durée={Duration}ms", result.DurationMs);
            return result;
        }
        catch (TaskCanceledException ex)
        {
            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
            result.ErrorType = "timeout";
            result.ErrorMessage = $"Timeout après {result.DurationMs}ms (limite {TestTimeoutSeconds}s)";
            _log.LogWarning(ex, "[EliteBgsDiagnostic] Timeout durée={Duration}ms", result.DurationMs);
            return result;
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
            var inner = ex.InnerException;
            if (inner is System.Net.Sockets.SocketException se)
            {
                result.ErrorType = se.SocketErrorCode switch
                {
                    SocketError.HostNotFound => "dns_not_found",
                    SocketError.ConnectionRefused => "connection_refused",
                    SocketError.TimedOut => "connection_timeout",
                    _ => "network_error",
                };
                result.ErrorMessage = $"{se.SocketErrorCode}: {se.Message}";
            }
            else
            {
                result.ErrorType = "network_error";
                result.ErrorMessage = ex.Message;
            }
            _log.LogWarning(ex, "[EliteBgsDiagnostic] Erreur réseau: {Type} {Msg}", result.ErrorType, result.ErrorMessage);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
            result.ErrorType = "unknown";
            result.ErrorMessage = ex.GetType().Name + ": " + ex.Message;
            _log.LogError(ex, "[EliteBgsDiagnostic] Erreur inattendue: {Msg}", ex.Message);
            return result;
        }
    }
}

public record EliteBgsTestResult
{
    public string Url { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string? FactionName { get; set; }
    public int TimeoutSeconds { get; set; }
    public string? UserAgent { get; set; }
    public long DurationMs { get; set; }
    public int HttpStatusCode { get; set; }
    public int ResponseLength { get; set; }
    public string ResponsePreview { get; set; } = "";
    public bool HasValidJson { get; set; }
    public int DocsCount { get; set; }
    public string? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
}
