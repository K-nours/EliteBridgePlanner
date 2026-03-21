using EliteBridgePlanner.Server.Utils;
using NUnit.Framework;

namespace EliteBridgePlanner.Tests.Utils;

[TestFixture]
public class DateTimeHelperTests
{
    // ── ConvertToUserTimeZone ─────────────────────────────────────────────

    [Test]
    public void ConvertToUserTimeZone_UTC_ToParis_ReturnsLocalTime()
    {
        // Arrange
        var utcTime = new DateTime(2025, 3, 5, 10, 0, 0, DateTimeKind.Utc);
        var parisTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

        // Act
        var result = DateTimeHelper.ConvertToUserTimeZone(utcTime, parisTimeZone);

        // Assert
        // Paris est UTC+1 en hiver (avant le dernier dimanche de mars)
        // Donc 10:00 UTC = 11:00 CET
        Assert.That(result.Hour, Is.EqualTo(11));
        Assert.That(result.Kind, Is.EqualTo(DateTimeKind.Unspecified));
    }

    [Test]
    public void ConvertToUserTimeZone_UTC_ToNewYork_ReturnsLocalTime()
    {
        // Arrange
        var utcTime = new DateTime(2025, 3, 5, 15, 0, 0, DateTimeKind.Utc);
        var nyTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        // Act
        var result = DateTimeHelper.ConvertToUserTimeZone(utcTime, nyTimeZone);

        // Assert
        // New York est UTC-5 en mars
        // Donc 15:00 UTC = 10:00 EST
        Assert.That(result.Hour, Is.EqualTo(10));
    }

    [Test]
    public void ConvertToUserTimeZone_NonUtcKind_SpecifiesAsUtc()
    {
        // Arrange
        var unspecifiedTime = new DateTime(2025, 3, 5, 10, 0, 0, DateTimeKind.Unspecified);
        var parisTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

        // Act
        var result = DateTimeHelper.ConvertToUserTimeZone(unspecifiedTime, parisTimeZone);

        // Assert — devrait traiter comme UTC et convertir
        Assert.That(result.Hour, Is.EqualTo(11));
    }

    // ── ConvertToUtc ──────────────────────────────────────────────────────

    [Test]
    public void ConvertToUtc_ParisToParis_ReturnsUTC()
    {
        // Arrange
        var parisTime = new DateTime(2025, 3, 5, 11, 0, 0, DateTimeKind.Unspecified); // 11:00 CET
        var parisTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

        // Act
        var result = DateTimeHelper.ConvertToUtc(parisTime, parisTimeZone);

        // Assert
        // 11:00 CET (UTC+1) = 10:00 UTC
        Assert.That(result.Hour, Is.EqualTo(10));
        Assert.That(result.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public void ConvertToUtc_AlreadyUtc_ReturnsUnchanged()
    {
        // Arrange
        var utcTime = new DateTime(2025, 3, 5, 10, 0, 0, DateTimeKind.Utc);
        var parisTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

        // Act
        var result = DateTimeHelper.ConvertToUtc(utcTime, parisTimeZone);

        // Assert
        Assert.That(result, Is.EqualTo(utcTime));
        Assert.That(result.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    // ── FormatForApi ──────────────────────────────────────────────────────

    [Test]
    public void FormatForApi_ReturnsISO8601Format()
    {
        // Arrange
        var dateTime = new DateTime(2025, 3, 5, 10, 30, 45, DateTimeKind.Utc);

        // Act
        var result = DateTimeHelper.FormatForApi(dateTime);

        // Assert
        Assert.That(result, Does.StartWith("2025-03-05"));
        Assert.That(result, Does.Contain("T10:30:45"));
        Assert.That(result, Does.EndWith("Z"));
    }

    // ── TryGetTimeZoneInfo ────────────────────────────────────────────────

    [Test]
    public void TryGetTimeZoneInfo_ValidTimeZone_ReturnsTimeZoneInfo()
    {
        // Arrange & Act
        var result = DateTimeHelper.TryGetTimeZoneInfo("Europe/Paris");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("Europe/Paris"));
    }

    [Test]
    public void TryGetTimeZoneInfo_InvalidTimeZone_ReturnsNull()
    {
        // Arrange & Act
        var result = DateTimeHelper.TryGetTimeZoneInfo("Invalid/TimeZone");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryGetTimeZoneInfo_UTC_ReturnsUtcTimeZone()
    {
        // Arrange & Act
        var result = DateTimeHelper.TryGetTimeZoneInfo("UTC");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("UTC"));
    }
}
