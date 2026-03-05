using EliteBridgePlanner.Server.Data;
using EliteBridgePlanner.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EliteBridgePlanner.Tests.Helpers;

/// <summary>
/// Fabrique une AppDbContext en mémoire isolée pour chaque test.
/// Chaque test reçoit une DB vide — pas d'effets de bord entre tests.
/// </summary>
public static class DbContextFactory
{
    public static AppDbContext CreateInMemory(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}

/// <summary>
/// Données de test réutilisables — évite la duplication dans les fixtures.
/// </summary>
public static class TestData
{
    public static AppUser CreateUser(string id = "user-1", string cmdName = "CMDR_TEST") => new()
    {
        Id = id,
        Email = $"{cmdName.ToLower()}@test.local",
        UserName = $"{cmdName.ToLower()}@test.local",
        CommanderName = cmdName
    };

    public static Bridge CreateBridge(string userId = "user-1") => new()
    {
        Id = 1,
        Name = "Pont Test",
        CreatedByUserId = userId,
        Systems = []
    };

    public static StarSystem CreateSystem(int id, int bridgeId, int? previousSystemId = null, int? nextSystemId = null,
        SystemType type = SystemType.TABLIER,
        ColonizationStatus status = ColonizationStatus.PLANIFIE) => new()
        {
            Id = id,
            Name = $"Système {id}",
            PreviousSystemId = previousSystemId,
            NextSystemId = nextSystemId,
            Type = type,
            Status = status,
            BridgeId = bridgeId
        };
}
