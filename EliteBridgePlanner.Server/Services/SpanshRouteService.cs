using System.Net.Http.Json;
using System.Text.Json;

namespace EliteBridgePlanner.Server.Services;

public class SpanshRouteService : ISpanshRouteService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _httpFactory;
    private const string BaseUrl = "https://www.spansh.co.uk";

    public SpanshRouteService(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    public async Task<IReadOnlyList<SpanshJump>> GetRouteAsync(string source, string destination, CancellationToken ct = default)
    {
        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EliteBridgePlanner/1.0");

        // 1. Créer le job
        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("source_system", source),
            new KeyValuePair<string, string>("destination_system", destination)
        });
        var createResponse = await client.PostAsync($"{BaseUrl}/api/colonisation/route", form, ct);
        createResponse.EnsureSuccessStatusCode();
        var createJson = await createResponse.Content.ReadFromJsonAsync<SpanshJobResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Réponse Spansh invalide (job)");
        var jobId = createJson.Job ?? throw new InvalidOperationException("Aucun job ID dans la réponse Spansh");

        // 2. Poller jusqu'au résultat (max ~5 min)
        for (var attempt = 0; attempt < 120; attempt++)
        {
            await Task.Delay(2500, ct);
            var resultResponse = await client.GetAsync($"{BaseUrl}/api/results/{jobId}", ct);
            resultResponse.EnsureSuccessStatusCode();
            var resultJson = await resultResponse.Content.ReadFromJsonAsync<SpanshResultResponse>(JsonOptions, ct);
            if (resultJson?.Result?.Jumps != null && resultJson.Result.Jumps.Count > 0)
            {
                return resultJson.Result.Jumps
                    .Select(j => new SpanshJump(
                        j.Name ?? "",
                        j.Distance,
                        j.X,
                        j.Y,
                        j.Z
                    ))
                    .ToList();
            }
            if (resultJson?.Status == "error")
                throw new InvalidOperationException($"Spansh erreur: {resultJson.Error ?? "inconnue"}");
        }
        throw new TimeoutException("Délai dépassé en attendant le résultat Spansh.");
    }

    private class SpanshJobResponse
    {
        public string? Job { get; set; }
    }

    private class SpanshResultResponse
    {
        public string? Status { get; set; }
        public string? Error { get; set; }
        public SpanshResult? Result { get; set; }
    }

    private class SpanshResult
    {
        public List<SpanshJumpDto>? Jumps { get; set; }
    }

    private class SpanshJumpDto
    {
        public string? Name { get; set; }
        public double Distance { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Z { get; set; }
    }
}
