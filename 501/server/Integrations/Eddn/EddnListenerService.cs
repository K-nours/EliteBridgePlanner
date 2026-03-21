using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GuildDashboard.Server.Models;
using Microsoft.Extensions.Hosting;
using NetMQ;
using NetMQ.Sockets;

namespace GuildDashboard.Server.Integrations.Eddn;

/// <summary>Listener ZeroMQ minimal : connecte au flux EDDN, reçoit et stocke les messages bruts.</summary>
public class EddnListenerService : BackgroundService
{
    private const string EddnRelayUrl = "tcp://eddn.edcd.io:9500";

    private readonly IServiceProvider _services;
    private readonly ILogger<EddnListenerService> _log;
    private readonly EddnStatusService _status;

    private long _receivedCount;

    public EddnListenerService(
        IServiceProvider services,
        ILogger<EddnListenerService> log,
        EddnStatusService status)
    {
        _services = services;
        _log = log;
        _status = status;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("EDDN listener démarré — connexion à {Url}", EddnRelayUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunListenerAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _status.SetConnected(false);
                _log.LogError(ex, "EDDN listener erreur — reconnexion dans 10s");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _status.SetConnected(false);
        _log.LogInformation("EDDN listener arrêté");
    }

    private async Task RunListenerAsync(CancellationToken ct)
    {
        using var subscriber = new SubscriberSocket();
        subscriber.Connect(EddnRelayUrl);
        subscriber.SubscribeToAnyTopic();

        _status.SetConnected(true);
        _log.LogInformation("EDDN connecté à {Url} — attente des messages...", EddnRelayUrl);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                byte[]? raw = ReceivePayload(subscriber);
                if (raw == null || raw.Length == 0)
                    continue;

                _receivedCount++;
                _status.SetReceivedCount(_receivedCount);

                var msg = DecompressAndParse(raw);
                if (msg != null)
                {
                    await StoreMessageAsync(msg, ct);
                    _status.SetLastReceivedAt(msg.ReceivedAt);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "EDDN erreur traitement message");
            }
        }

        subscriber.Disconnect(EddnRelayUrl);
    }

    /// <summary>Reçoit le payload zlib (EDDN envoie [topic][payload] en multipart).</summary>
    private static byte[]? ReceivePayload(SubscriberSocket socket)
    {
        if (!socket.TryReceiveFrameBytes(TimeSpan.FromSeconds(30), out byte[]? first))
            return null;
        if (first == null) return null;
        if (first.Length > 2 && first[0] == 0x78) // zlib header: 78 9C or 78 01
            return first;
        if (socket.TryReceiveFrameBytes(TimeSpan.FromMilliseconds(100), out byte[]? second))
            return second;
        return first;
    }

    private EddnRawMessage? DecompressAndParse(byte[] raw)
    {
        try
        {
            using var input = new MemoryStream(raw);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(zlib, Encoding.UTF8);
            var json = reader.ReadToEnd();

            return ParseAndBuildMessage(json);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "EDDN decompression/parse échoué");
            return null;
        }
    }

    private static EddnRawMessage? ParseAndBuildMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var schemaRef = root.TryGetProperty("$schemaRef", out var sr) ? sr.GetString() : null;
            var header = root.TryGetProperty("header", out var h) ? h : (JsonElement?)null;
            var message = root.TryGetProperty("message", out var m) ? m : (JsonElement?)null;

            var sourceSoftware = header?.TryGetProperty("softwareName", out var sw) == true ? sw.GetString() : null;
            var sourceUploader = header?.TryGetProperty("uploaderID", out var up) == true ? up.GetString() : null;
            var gatewayTs = root.TryGetProperty("gatewayTimestamp", out var gt) ? ParseDateTime(gt) : null;
            var systemName = message?.TryGetProperty("systemName", out var sn) == true ? sn.GetString() : null;
            var stationName = message?.TryGetProperty("stationName", out var st) == true ? st.GetString() : null;

            return new EddnRawMessage
            {
                SchemaRef = schemaRef,
                GatewayTimestamp = gatewayTs,
                SourceSoftware = sourceSoftware,
                SourceUploader = sourceUploader,
                SystemName = systemName,
                StationName = stationName,
                MessageJson = json,
                ReceivedAt = DateTime.UtcNow,
            };
        }
        catch
        {
            return new EddnRawMessage
            {
                MessageJson = json,
                ReceivedAt = DateTime.UtcNow,
            };
        }
    }

    private static DateTime? ParseDateTime(JsonElement el)
    {
        var s = el.GetString();
        return DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
    }

    private async Task StoreMessageAsync(EddnRawMessage msg, CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<EddnMessageStore>();
            await store.StoreAsync(msg, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "EDDN store échoué pour SchemaRef={SchemaRef}", msg.SchemaRef);
        }
    }
}
