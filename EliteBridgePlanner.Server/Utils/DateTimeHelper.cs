namespace EliteBridgePlanner.Server.Utils;

/// <summary>
/// Utilitaire pour gérer les conversions de dates UTC ↔ Local
/// Toutes les dates en base de données DOIVENT être stockées en UTC (DateTime.UtcNow)
/// </summary>
public static class DateTimeHelper
{
    /// <summary>
    /// Convertit une date UTC en format local pour un utilisateur
    /// </summary>
    /// <param name="utcDateTime">La date en UTC</param>
    /// <param name="userTimeZone">La timezone de l'utilisateur</param>
    /// <returns>La date convertie dans la timezone de l'utilisateur</returns>
    public static DateTime ConvertToUserTimeZone(DateTime utcDateTime, TimeZoneInfo userTimeZone)
    {
        if (utcDateTime.Kind != DateTimeKind.Utc)
            utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

        return TimeZoneInfo.ConvertTime(utcDateTime, TimeZoneInfo.Utc, userTimeZone);
    }

    /// <summary>
    /// Convertit une date locale en UTC
    /// </summary>
    /// <param name="localDateTime">La date en timezone local</param>
    /// <param name="userTimeZone">La timezone de l'utilisateur</param>
    /// <returns>La date convertie en UTC</returns>
    public static DateTime ConvertToUtc(DateTime localDateTime, TimeZoneInfo userTimeZone)
    {
        if (localDateTime.Kind == DateTimeKind.Utc)
            return localDateTime;

        var utc = TimeZoneInfo.ConvertTime(localDateTime, userTimeZone, TimeZoneInfo.Utc);
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
    }

    /// <summary>
    /// Format standardisé pour les retours API (ISO 8601)
    /// </summary>
    public static string FormatForApi(DateTime dateTime)
    {
        return dateTime.ToString("o"); // "2025-03-05T10:00:00Z"
    }

    /// <summary>
    /// Obtient la TimeZoneInfo à partir d'un identifiant
    /// </summary>
    /// <returns>TimeZoneInfo ou null si invalide</returns>
    public static TimeZoneInfo? TryGetTimeZoneInfo(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            return null;
        }
    }
}
