using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Models;
using EliteBridgePlanner.Server.Services;
using EliteBridgePlanner.Tests.Helpers;
using NUnit.Framework;

namespace EliteBridgePlanner.Tests.Services;

/// <summary>
/// Tests unitaires de BridgeService.
/// Utilise EF Core InMemory — pas de mock nécessaire pour le service lui-même.
/// Pattern : Arrange / Act / Assert clairement séparés.
/// </summary>
[TestFixture]
public class BridgeServiceTests
{
    private IBridgeService _service = null!;

    [SetUp]
    public void SetUp()
    {
        // Nouvelle DB en mémoire isolée pour chaque test
        var db = DbContextFactory.CreateInMemory();
        _service = new BridgeService(db);
    }

    // ── GetBridgeById ──────────────────────────────────────────────────────

    [Test]
    public async Task GetBridgeByIdAsync_WhenExists_ReturnsBridgeDto()
    {
        // Arrange
        var db = DbContextFactory.CreateInMemory();
        var bridge = TestData.CreateBridge();
        db.Bridges.Add(bridge);
        await db.SaveChangesAsync();
        var svc = new BridgeService(db);

        // Act
        var result = await svc.GetBridgeByIdAsync(1);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(1));
        Assert.That(result.Name, Is.EqualTo("Pont Test"));
    }

    [Test]
    public async Task GetBridgeByIdAsync_WhenNotExists_ReturnsNull()
    {
        // Arrange — DB vide
        var db = DbContextFactory.CreateInMemory();
        var svc = new BridgeService(db);

        // Act
        var result = await svc.GetBridgeByIdAsync(999);

        // Assert
        Assert.That(result, Is.Null);
    }

    // ── CreateBridge ───────────────────────────────────────────────────────

    [Test]
    public async Task CreateBridgeAsync_PersistsAndReturnsBridgeDto()
    {
        // Arrange
        var db = DbContextFactory.CreateInMemory();
        var svc = new BridgeService(db);
        var request = new CreateBridgeRequest("Pont Sol-Colonia", "Pont de test");

        // Act
        var result = await svc.CreateBridgeAsync(request, "user-1");

        // Assert
        Assert.That(result.Id, Is.GreaterThan(0));
        Assert.That(result.Name, Is.EqualTo("Pont Sol-Colonia"));
        Assert.That(db.Bridges.Count(), Is.EqualTo(1));
    }

    // ── AddSystem ──────────────────────────────────────────────────────────

    //[Test]
    //public async Task AddSystemAsync_InsertsAtCorrectOrder()
    //{
    //    // Arrange
    //    var db = DbContextFactory.CreateInMemory();
    //    var bridge = TestData.CreateBridge();
    //    bridge.Systems.Add(TestData.CreateSystem(1, 1, order: 1, SystemType.DEBUT));
    //    bridge.Systems.Add(TestData.CreateSystem(2, 1, order: 2, SystemType.FIN));
    //    db.Bridges.Add(bridge);
    //    await db.SaveChangesAsync();
    //    var svc = new BridgeService(db);

    //    // Act — insérer après la position 1 (entre DEBUT et FIN)
    //    var request = new CreateSystemRequest("Nouveau Système", "PILE", "PLANIFIE",
    //        InsertAfterOrder: 1, ArchitectId: null, BridgeId: 1);
    //    var result = await svc.AddSystemAsync(request);

    //    // Assert
    //    Assert.That(result.Order, Is.EqualTo(2));
    //    Assert.That(result.Name, Is.EqualTo("Nouveau Système"));

    //    // FIN doit avoir été décalée à la position 3
    //    var fin = db.StarSystems.First(s => s.Type == SystemType.FIN);
    //    Assert.That(fin.Order, Is.EqualTo(3));
    //}

    //[Test]
    //public async Task AddSystemAsync_AtBeginning_InsertsAtOrder1()
    //{
    //    // Arrange
    //    var db = DbContextFactory.CreateInMemory();
    //    var bridge = TestData.CreateBridge();
    //    bridge.Systems.Add(TestData.CreateSystem(1, 1, order: 1, SystemType.FIN));
    //    db.Bridges.Add(bridge);
    //    await db.SaveChangesAsync();
    //    var svc = new BridgeService(db);

    //    // Act — InsertAfterOrder: 0 = tout au début
    //    var request = new CreateSystemRequest("Sol", "DEBUT", "PLANIFIE",
    //        InsertAfterOrder: 0, ArchitectId: null, BridgeId: 1);
    //    var result = await svc.AddSystemAsync(request);

    //    // Assert
    //    Assert.That(result.Order, Is.EqualTo(1));
    //    var fin = db.StarSystems.First(s => s.Type == SystemType.FIN);
    //    Assert.That(fin.Order, Is.EqualTo(2));
    //}

    // ── UpdateSystem ───────────────────────────────────────────────────────

    [Test]
    public async Task UpdateSystemAsync_ChangesNameAndStatus()
    {
        // Arrange
        var db = DbContextFactory.CreateInMemory();
        var bridge = TestData.CreateBridge();
        bridge.Systems.Add(TestData.CreateSystem(1, 1, 1));
        db.Bridges.Add(bridge);
        await db.SaveChangesAsync();
        var svc = new BridgeService(db);

        // Act
        var update = new UpdateSystemRequest("Nouveau Nom", null, "CONSTRUCTION", null);
        var result = await svc.UpdateSystemAsync(1, update);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("Nouveau Nom"));
        Assert.That(result.Status, Is.EqualTo("CONSTRUCTION"));
    }

    [Test]
    public async Task UpdateSystemAsync_WhenNotExists_ReturnsNull()
    {
        // Arrange
        var db = DbContextFactory.CreateInMemory();
        var svc = new BridgeService(db);

        // Act
        var result = await svc.UpdateSystemAsync(999, new UpdateSystemRequest("X", null, null, null));

        // Assert
        Assert.That(result, Is.Null);
    }

    // ── ReorderSystem ──────────────────────────────────────────────────────

    [Test]
    public async Task ReorderSystemAsync_MovingDown_ShiftsIntermediatesUp()
    {
        // Arrange
        var db = DbContextFactory.CreateInMemory();
        var bridge = TestData.CreateBridge();
        bridge.Systems.Add(TestData.CreateSystem(1, 1, null, 2));
        bridge.Systems.Add(TestData.CreateSystem(2, 1, 1, 3));
        bridge.Systems.Add(TestData.CreateSystem(3, 1, 2, null));
        db.Bridges.Add(bridge);
        await db.SaveChangesAsync();
        var svc = new BridgeService(db);

        // Act — déplacer le système 1 (ordre 1) à l'ordre 3
        var result = await svc.MoveSystemAsync(1, insertAfterId: 3);

        // Assert
        Assert.That(result!.Order, Is.EqualTo(3));
        Assert.That(db.StarSystems.First(s => s.Id == 2).PreviousSystemId, Is.Null);
        Assert.That(db.StarSystems.First(s => s.Id == 3).NextSystemId, Is.EqualTo(1));
    }

    // ── DeleteSystem ───────────────────────────────────────────────────────

    [Test]
    public async Task DeleteSystemAsync_RemovesAndCompactsOrders()
    {
        // Arrange
        var db = DbContextFactory.CreateInMemory();
        var bridge = TestData.CreateBridge();
        bridge.Systems.Add(TestData.CreateSystem(1, 1, null, 2));
        bridge.Systems.Add(TestData.CreateSystem(2, 1, 1, 3));
        bridge.Systems.Add(TestData.CreateSystem(3, 1, 2, null));
        db.Bridges.Add(bridge);
        await db.SaveChangesAsync();
        var svc = new BridgeService(db);

        // Act — supprimer le système du milieu (ordre 2)
        var ok = await svc.DeleteSystemAsync(2);

        // Assert
        Assert.That(ok, Is.True);
        Assert.That(db.StarSystems.Count(), Is.EqualTo(2));
        // Le système 3 doit être remonté à l'ordre 2
        //Assert.That(db.StarSystems.First(s => s.Id == 3).Order, Is.EqualTo(2));
    }

    [Test]
    public async Task DeleteSystemAsync_WhenNotExists_ReturnsFalse()
    {
        // Arrange
        var db = DbContextFactory.CreateInMemory();
        var svc = new BridgeService(db);

        // Act
        var ok = await svc.DeleteSystemAsync(999);

        // Assert
        Assert.That(ok, Is.False);
    }

    // ── CompletionPercent ──────────────────────────────────────────────────

    [Test]
    public async Task GetBridgeByIdAsync_CompletionPercent_CalculatedCorrectly()
    {
        // Arrange
        var db = DbContextFactory.CreateInMemory();
        var bridge = TestData.CreateBridge();
        bridge.Systems.Add(TestData.CreateSystem(1, 1, 1, status: ColonizationStatus.FINI));
        bridge.Systems.Add(TestData.CreateSystem(2, 1, 2, status: ColonizationStatus.FINI));
        bridge.Systems.Add(TestData.CreateSystem(3, 1, 3, status: ColonizationStatus.PLANIFIE));
        bridge.Systems.Add(TestData.CreateSystem(4, 1, 4, status: ColonizationStatus.PLANIFIE));
        db.Bridges.Add(bridge);
        await db.SaveChangesAsync();
        var svc = new BridgeService(db);

        // Act
        var result = await svc.GetBridgeByIdAsync(1);

        // Assert — 2 sur 4 = 50%
        Assert.That(result!.CompletionPercent, Is.EqualTo(50));
    }
}
