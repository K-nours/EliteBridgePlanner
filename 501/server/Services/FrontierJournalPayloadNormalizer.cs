using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace GuildDashboard.Server.Services;

/// <summary>
/// Normalise un jour de journal CAPI : tableau JSON unique ou NDJSON (plusieurs racines),
/// comme pour l’upload EDSM et le parse carte.
/// </summary>
public static class FrontierJournalPayloadNormalizer
{
    /// <summary>
    /// Un jour = un tableau JSON unique, ou NDJSON (export CAPI typique : une ligne par événement),
    /// ou plusieurs objets JSON concaténés (Utf8JsonReader par valeur).
    /// </summary>
    public static bool TryOpenJournalDayAsArray(string payload, [NotNullWhen(true)] out JsonDocument? doc, out string? error)
    {
        doc = null;
        error = null;
        var t = payload.Trim();
        if (t.Length == 0)
        {
            error = "Journal local (CAPI) vide pour ce jour.";
            return false;
        }

        try
        {
            var d = JsonDocument.Parse(t);
            if (d.RootElement.ValueKind == JsonValueKind.Array)
            {
                doc = d;
                return true;
            }

            d.Dispose();
            error = "Journal local (CAPI) : racine JSON inattendue (un tableau [...] était attendu seul).";
            return false;
        }
        catch (JsonException)
        {
            // Plusieurs valeurs JSON à la suite (ex. une ligne d'événement par ligne).
        }

        try
        {
            ReadOnlySpan<byte> span = Encoding.UTF8.GetBytes(t);
            if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
                span = span.Slice(3);

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartArray();
                while (!span.IsEmpty)
                {
                    var i = 0;
                    while (i < span.Length && span[i] <= 0x20)
                        i++;
                    if (i >= span.Length)
                        break;
                    span = span.Slice(i);

                    var reader = new Utf8JsonReader(span, isFinalBlock: true, state: default);
                    if (!reader.Read())
                        break;
                    var el = JsonElement.ParseValue(ref reader);
                    var consumed = (int)reader.BytesConsumed;
                    if (consumed <= 0)
                    {
                        error = "Lecture du journal local (CAPI) : segment JSON invalide.";
                        return false;
                    }

                    if (el.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in el.EnumerateArray())
                            item.WriteTo(writer);
                    }
                    else
                        el.WriteTo(writer);

                    span = span.Slice(consumed);
                }

                writer.WriteEndArray();
            }

            doc = JsonDocument.Parse(stream.ToArray());
            if (doc.RootElement.GetArrayLength() == 0)
            {
                doc.Dispose();
                doc = null;
                error = "Aucun événement dans le journal local (CAPI) après lecture NDJSON.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = "Erreur de lecture du journal local (CAPI, souvent NDJSON). " + ex.Message;
            return false;
        }
    }
}
