using GuildDashboard.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace GuildDashboard.Server.Controllers;

[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly FrontierUserService _userService;
    private readonly InaraApiService _inara;
    private readonly ILogger<UserController> _log;

    public UserController(FrontierUserService userService, InaraApiService inara, ILogger<UserController> log)
    {
        _userService = userService;
        _inara = inara;
        _log = log;
    }

    /// <summary>GET /api/user/me — utilisateur courant (Frontier). commander, squadron, guildId, avatarUrl (Inara si match fiable).</summary>
    /// <param name="debugSimulateInaraMiss">Si 1 ou true: simule un CMDR non trouvé sur Inara (debug temporaire).</param>
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct, [FromQuery] bool debugSimulateInaraMiss = false)
    {
        var user = await _userService.GetCurrentUserAsync(ct);
        if (user == null)
            return Ok(new { connected = false });

        string? avatarUrl = null;
        if (!string.IsNullOrWhiteSpace(user.Commander))
        {
            var frontierName = user.Commander.Trim();
            var normalizedName = frontierName;
            if (normalizedName.StartsWith("CMDR ", StringComparison.OrdinalIgnoreCase))
                normalizedName = normalizedName["CMDR ".Length..].Trim();

            var searchNames = new List<string> { normalizedName };
            if (normalizedName.Contains('0')) searchNames.Add(normalizedName.Replace('0', 'O'));
            if (normalizedName.Contains('O') || normalizedName.Contains('o'))
                searchNames.Add(normalizedName.Replace('O', '0').Replace('o', '0'));

            if (debugSimulateInaraMiss)
            {
                _log.LogWarning("DEBUG: debugSimulateInaraMiss actif — remplacement des recherches Inara par un nom inexistant");
                searchNames = new List<string> { "DEBUG_NONEXISTENT_CMDR_" + Guid.NewGuid().ToString("N")[..8] };
            }

            foreach (var searchName in searchNames.Distinct())
            {
                var result = await _inara.GetCommanderProfileAsync(searchName, ct);
                if (result == null) continue;

                var matchResult = NamesMatchWithLog(frontierName, result.CommanderName ?? "");
                var isReliableMatch = result.EventStatus == 200
                    && result.HasEventData
                    && !string.IsNullOrEmpty(result.CommanderName)
                    && matchResult.Match;

                if (isReliableMatch && !string.IsNullOrEmpty(result.AvatarImageUrl))
                {
                    avatarUrl = result.AvatarImageUrl;
                    _log.LogInformation("Avatar Inara accepté: nomFrontier={FrontierName} nomInara={InaraName}", frontierName, result.CommanderName);
                    break;
                }

                LogAvatarRejection(frontierName, searchName, result);
            }
        }

        return Ok(new
        {
            connected = true,
            commander = user.Commander,
            squadron = user.Squadron,
            guildId = user.GuildId,
            guildName = user.GuildName,
            customerId = user.CustomerId,
            avatarUrl,
        });
    }

    /// <summary>Compare les noms en tenant compte de 0/O (équivalence in-game) + log explicite.</summary>
    private (bool Match, string NormalizedFrontier, string NormalizedInara) NamesMatchWithLog(string frontier, string inara)
    {
        var n1 = NormalizeForMatch(frontier);
        var n2 = NormalizeForMatch(inara);
        var match = string.Equals(n1, n2, StringComparison.OrdinalIgnoreCase);
        _log.LogInformation(
            "Match 0/O: frontier={Frontier} inara={Inara} normalizedFrontier={NormFrontier} normalizedInara={NormInara} match={Match}",
            frontier, inara, n1, n2, match);
        return (match, n1, n2);
    }

    private static string NormalizeForMatch(string name)
    {
        var n = name.Trim();
        if (n.StartsWith("CMDR ", StringComparison.OrdinalIgnoreCase))
            n = n["CMDR ".Length..].Trim();
        return n.Replace('0', 'O');
    }

    private void LogAvatarRejection(string frontierName, string searchName, InaraApiService.GetCommanderProfileResult result)
    {
        _log.LogInformation(
            "Avatar Inara rejeté: nomFrontier={FrontierName} nomRecherché={SearchName} eventStatus={EventStatus} eventStatusText={EventStatusText} commanderNameInara={InaraCommanderName} avatarPresent={AvatarPresent} otherNamesFound=[{OtherNames}]",
            frontierName,
            searchName,
            result.EventStatus,
            result.EventStatusText ?? "",
            result.CommanderName ?? "",
            !string.IsNullOrEmpty(result.AvatarImageUrl),
            result.OtherNamesFound != null ? string.Join(", ", result.OtherNamesFound) : "");
    }

    /// <summary>GET /api/user/debug/inara-profile — appelle getCommanderProfile. HTML en navigateur, JSON si Accept:application/json.</summary>
    [HttpGet("debug/inara-profile")]
    public async Task<IActionResult> DebugInaraProfile([FromQuery] string? searchName, CancellationToken ct)
    {
        if (PrefersHtml())
        {
            if (string.IsNullOrWhiteSpace(searchName))
                return Content(BuildInaraProfileHtml(null, 0, null, null, null, Array.Empty<string>(), false, "searchName requis (ex: ?searchName=Bib0xkn0x)"), "text/html; charset=utf-8");

            var result = await _inara.GetCommanderProfileAsync(searchName.Trim(), ct);
            return Content(BuildInaraProfileHtml(
                searchName.Trim(),
                result?.EventStatus ?? 0,
                result?.EventStatusText,
                result?.CommanderName,
                result?.AvatarImageUrl,
                result?.OtherNamesFound ?? Array.Empty<string>(),
                result?.HasEventData ?? false,
                result == null ? "Appel Inara échoué ou Inara:ApiKey non configurée" : null), "text/html; charset=utf-8");
        }

        if (string.IsNullOrWhiteSpace(searchName))
            return BadRequest(new { error = "searchName requis" });

        var res = await _inara.GetCommanderProfileAsync(searchName.Trim(), ct);
        if (res == null)
            return Ok(new { searchName = searchName.Trim(), error = "Appel Inara échoué ou Inara:ApiKey non configurée" });

        return Ok(new
        {
            searchName = searchName.Trim(),
            res.EventStatus,
            res.EventStatusText,
            res.CommanderName,
            avatarImageURL = res.AvatarImageUrl,
            res.OtherNamesFound,
            res.HasEventData,
        });
    }

    private bool PrefersHtml() =>
        !Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

    private static string BuildInaraProfileHtml(string? searchName, int eventStatus, string? eventStatusText,
        string? commanderName, string? avatarImageUrl, IReadOnlyList<string> otherNamesFound, bool hasEventData, string? error)
    {
        var css = "body{font-family:system-ui;margin:0;padding:1rem;background:#1a1a2e;color:#e6edf3;}" +
            "h1{color:#6ee7b7;margin:0 0 1rem;} .card{background:#2d2d44;padding:1rem;border-radius:8px;margin-bottom:1rem;} h2{color:#6ee7b7;font-size:1rem;}" +
            ".ok{color:#6ee7b7;} .err{color:#f87171;} table{border-collapse:collapse;width:100%;} th,td{border:1px solid #444;padding:0.4rem;} th{background:#1a1a2e;width:200px;}";
        var val = System.Net.WebUtility.HtmlEncode(searchName ?? "Bib0xkn0x");
        var form = "<form method=\"get\" action=\"/api/user/debug/inara-profile\" style=\"display:flex;gap:0.5rem;align-items:center;margin-bottom:1rem;\">" +
            "<input type=\"text\" name=\"searchName\" value=\"" + val + "\" placeholder=\"Nom CMDR\" style=\"padding:0.4rem;width:140px;\" />" +
            "<button type=\"submit\" style=\"background:#6ee7b7;color:#1a1a2e;border:none;padding:0.5rem 0.8rem;border-radius:6px;cursor:pointer;\">Tester</button></form>";

        if (error != null)
            return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Inara Profile Debug</title><style>" + css + "</style></head><body>" +
                "<h1>Inara getCommanderProfile — Debug</h1><div class=\"card\">" + form + "<p class=\"err\">" + System.Net.WebUtility.HtmlEncode(error) + "</p></div></body></html>";

        var statusOk = eventStatus == 200 && !string.IsNullOrEmpty(commanderName);
        var statusHtml = "<p>Profil trouvé : <span class=\"" + (statusOk ? "ok" : "err") + "\">" + (statusOk ? "Oui" : "Non") + "</span></p>";
        var rows = new[]
        {
            ("searchName", searchName ?? ""),
            ("eventStatus", eventStatus.ToString()),
            ("eventStatusText", eventStatusText ?? ""),
            ("commanderName", commanderName ?? ""),
            ("avatarImageURL", avatarImageUrl ?? ""),
            ("otherNamesFound", string.Join(", ", otherNamesFound)),
            ("hasEventData", hasEventData.ToString()),
        };
        var table = "<table><tbody>" + string.Join("", rows.Select(r =>
            "<tr><th>" + System.Net.WebUtility.HtmlEncode(r.Item1) + "</th><td>" + System.Net.WebUtility.HtmlEncode(r.Item2) + "</td></tr>")) + "</tbody></table>";
        if (!string.IsNullOrEmpty(avatarImageUrl))
            table += "<p><img src=\"" + System.Net.WebUtility.HtmlEncode(avatarImageUrl) + "\" alt=\"avatar\" style=\"max-width:100px;border-radius:8px;\" /></p>";

        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Inara Profile Debug</title><style>" + css + "</style></head><body>" +
            "<h1>Inara getCommanderProfile — Debug</h1><div class=\"card\">" + form + statusHtml + table + "</div></body></html>";
    }

    /// <summary>GET /api/user/debug/match-names — teste la logique 0/O sans compte réel. Debug temporaire.</summary>
    [HttpGet("debug/match-names")]
    public IActionResult DebugMatchNames([FromQuery] string frontier, [FromQuery] string inara)
    {
        var n1 = NormalizeForMatch(frontier);
        var n2 = NormalizeForMatch(inara);
        var match = string.Equals(n1, n2, StringComparison.OrdinalIgnoreCase);
        _log.LogInformation(
            "Debug match-names: frontier={Frontier} inara={Inara} normalizedFrontier={NormFrontier} normalizedInara={NormInara} match={Match}",
            frontier, inara, n1, n2, match);
        return Ok(new
        {
            frontier,
            inara,
            normalizedFrontier = n1,
            normalizedInara = n2,
            match,
        });
    }
}
