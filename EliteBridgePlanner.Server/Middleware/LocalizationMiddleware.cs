using System.Globalization;
using EliteBridgePlanner.Server.Models;
using Microsoft.AspNetCore.Identity;

namespace EliteBridgePlanner.Server.Middleware;

/// <summary>
/// Middleware qui applique la culture et timezone de l'utilisateur
/// basée sur ses préférences ou le header Accept-Language
/// </summary>
public class LocalizationMiddleware
{
    private readonly RequestDelegate _next;

    public LocalizationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, UserManager<AppUser> userManager)
    {
        var user = context.User.Identity?.IsAuthenticated == true
            ? await userManager.GetUserAsync(context.User)
            : null;

        // Déterminer la culture (langue)
        var culture = user?.PreferredLanguage ?? ExtractLanguageFromHeader(context) ?? "en-GB";

        // Déterminer la timezone
        var timeZone = user?.PreferredTimeZone ?? "UTC";

        // Appliquer la culture au contexte
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        CultureInfo.CurrentUICulture = new CultureInfo(culture);

        // Stocker dans HttpContext.Items pour usage dans les contrôleurs
        context.Items["Culture"] = culture;
        context.Items["TimeZone"] = timeZone;
        context.Items["UserTimeZoneInfo"] = TimeZoneInfo.FindSystemTimeZoneById(timeZone);

        await _next(context);
    }

    /// <summary>
    /// Extrait la langue du header Accept-Language
    /// </summary>
    /// <returns>Code de langue (ex: "fr-FR", "en-GB") ou null</returns>
    private static string? ExtractLanguageFromHeader(HttpContext context)
    {
        var acceptLanguage = context.Request.Headers["Accept-Language"].ToString();
        if (string.IsNullOrEmpty(acceptLanguage))
            return null;

        // Extraire "fr" de "fr-FR,fr;q=0.9"
        var language = acceptLanguage.Split(',')[0].Split('-')[0];
        return language.ToLower() switch
        {
            "fr" => "fr-FR",
            "en" => "en-GB",
            _ => "en-GB"
        };
    }
}
