using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Models;
using EliteBridgePlanner.Server.Services.Contracts;
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

    [Test]
    public async Task AddSystemAsync_AtBeginning_InsertsAtHead()
    {
        // Arrange : 1 → 2 existant
        var db = DbContextFactory.CreateInMemory();
        var bridge = TestData.CreateBridge();
        bridge.Systems.Add(TestData.CreateSystem(1, 1, previousSystemId: null));
        bridge.Systems.Add(TestData.CreateSystem(2, 1, previousSystemId: 1));
        db.Bridges.Add(bridge);
        await db.SaveChangesAsync();
        var svc = new BridgeService(db);

        // Act — ajouter à la position 1 (tête)
        var request = new CreateSystemRequest("Nouveau", "TABLIER", "PLANIFIE", 
            InsertAtIndex: 1, ArchitectId: null, BridgeId: 1);
        var result = await svc.AddSystemAsync(request);

        // Assert
        Assert.That(result.Order, Is.EqualTo(1));
        var newSystem = db.StarSystems.First(s => s.Name == "Nouveau");
        Assert.That(newSystem.PreviousSystemId, Is.Null, "Nouveau doit devenir tête");
        Assert.That(db.StarSystems.First(s => s.Id == 1).PreviousSystemId, Is.EqualTo(newSystem.Id), "Ancien système 1 doit pointer vers Nouveau");
    }

    [Test]
    public async Task AddSystemAsync_InMiddle_InsertsAtCorrectPosition()
    {
        // Arrange : 1 → 2 → 3
        var db = DbContextFactory.CreateInMemory();
        var bridge = TestData.CreateBridge();
        bridge.Systems.Add(TestData.CreateSystem(1, 1, previousSystemId: null));
        bridge.Systems.Add(TestData.CreateSystem(2, 1, previousSystemId: 1));
        bridge.Systems.Add(TestData.CreateSystem(3, 1, previousSystemId: 2));
        db.Bridges.Add(bridge);
        await db.SaveChangesAsync();
        var svc = new BridgeService(db);

        // Act — ajouter à la position 3 (entre 2 et 3)
        var request = new CreateSystemRequest("Nouveau", "TABLIER", "PLANIFIE", 
            InsertAtIndex: 3, ArchitectId: null, BridgeId: 1);
        var result = await svc.AddSystemAsync(request);

        // Assert : 1 → 2 → Nouveau → 3
        Assert.That(result.Order, Is.EqualTo(3));
        var newSystem = db.StarSystems.First(s => s.Name == "Nouveau");
        Assert.That(newSystem.PreviousSystemId, Is.EqualTo(2), "Nouveau doit suivre 2");
        Assert.That(db.StarSystems.First(s => s.Id == 3).PreviousSystemId, Is.EqualTo(newSystem.Id), "Système 3 doit suivre Nouveau");
    }

    [Test]
    public async Task AddSystemAsync_AtEnd_InsertsAtQueue()
    {
        // Arrange : 1 → 2
        var db = DbContextFactory.CreateInMemory();
        var bridge = TestData.CreateBridge();
        bridge.Systems.Add(TestData.CreateSystem(1, 1, previousSystemId: null));
        bridge.Systems.Add(TestData.CreateSystem(2, 1, previousSystemId: 1));
        db.Bridges.Add(bridge);
        await db.SaveChangesAsync();
        var svc = new BridgeService(db);

        // Act — ajouter à la position 3 (après le dernier)
        var request = new CreateSystemRequest("Nouveau", "TABLIER", "PLANIFIE", 
            InsertAtIndex: 3, ArchitectId: null, BridgeId: 1);
        var result = await svc.AddSystemAsync(request);

        // Assert : 1 → 2 → Nouveau
        Assert.That(result.Order, Is.EqualTo(3));
        var newSystem = db.StarSystems.First(s => s.Name == "Nouveau");
        Assert.That(newSystem.PreviousSystemId, Is.EqualTo(2), "Nouveau doit suivre 2");
    }

    // ── UpdateSystem ───────────────────────────────────────────────────────

    [Test]
    public async Task UpdateSystemAsync_ChangesNameAndStatus()
    {
        // Arrange
        var db = DbContextFactory.CreateInMemory();
        var bridge = TestData.CreateBridge();
        bridge.Systems.Add(TestData.CreateSystem(1, 1, null));
        bridge.Systems.Add(TestData.CreateSystem(2, 1, 1));
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

    // ── MoveSystem ─────────────────────────────────────────────────────────

    [Test]
    public async Task MoveSystemAsync_MovingDown_ShiftsIntermediatesUp()
    {
        // Arrange : 1 → 2 → 3
        var db = DbContextFactory.CreateInMemory();
        var bridge = TestData.CreateBridge();
        bridge.Systems.Add(TestData.CreateSystem(1, 1, previousSystemId: null));
        bridge.Systems.Add(TestData.CreateSystem(2, 1, previousSystemId: 1));
        bridge.Systems.Add(TestData.CreateSystem(3, 1, previousSystemId: 2));
        db.Bridges.Add(bridge);
        await db.SaveChangesAsync();
        var svc = new BridgeService(db);

        // Act — déplacer le système 1 (ordre 1) à l'ordre 3 → 2 → 3 → 1
        var result = await svc.MoveSystemAsync(1, insertAtIndex: 3);

        // Assert
        Assert.That(result!.Order, Is.EqualTo(3));
        Assert.That(db.StarSystems.First(s => s.Id == 2).PreviousSystemId, Is.Null, "Système 2 doit devenir tête");
        Assert.That(db.StarSystems.First(s => s.Id == 3).PreviousSystemId, Is.EqualTo(2), "Système 3 doit suivre 2");
        Assert.That(db.StarSystems.First(s => s.Id == 1).PreviousSystemId, Is.EqualTo(3), "Système 1 doit suivre 3");
    }

    [Test]
    public async Task MoveSystemAsync_MovingUp_PreservesChain()
    {
        // Arrange : 1 → 2 → 3 → 4 → 5
        var db = DbContextFactory.CreateInMemory();
        var bridge = TestData.CreateBridge();
        bridge.Systems.Add(TestData.CreateSystem(1, 1, previousSystemId: null));
        bridge.Systems.Add(TestData.CreateSystem(2, 1, previousSystemId: 1));
        bridge.Systems.Add(TestData.CreateSystem(3, 1, previousSystemId: 2));
        bridge.Systems.Add(TestData.CreateSystem(4, 1, previousSystemId: 3));
        bridge.Systems.Add(TestData.CreateSystem(5, 1, previousSystemId: 4));
        db.Bridges.Add(bridge);
        await db.SaveChangesAsync();
        var svc = new BridgeService(db);

        // Act — déplacer le système 5 (ordre 5) à l'ordre 2 → 1 → 5 → 2 → 3 → 4
        var result = await svc.MoveSystemAsync(5, insertAtIndex: 2);

        // Assert
        Assert.That(result!.Order, Is.EqualTo(2));
        Assert.That(db.StarSystems.First(s => s.Id == 1).PreviousSystemId, Is.Null, "Système 1 doit rester tête");
        Assert.That(db.StarSystems.First(s => s.Id == 5).PreviousSystemId, Is.EqualTo(1), "Système 5 doit suivre 1");
        Assert.That(db.StarSystems.First(s => s.Id == 2).PreviousSystemId, Is.EqualTo(5), "Système 2 doit suivre 5");
        Assert.That(db.StarSystems.First(s => s.Id == 3).PreviousSystemId, Is.EqualTo(2), "Système 3 doit suivre 2");
        Assert.That(db.StarSystems.First(s => s.Id == 4).PreviousSystemId, Is.EqualTo(3), "Système 4 doit suivre 3");
    }

    [Test]
    public async Task MoveSystemAsync_MovingToHead_ChangesHead()
    {
        // Arrange : 1 → 2 → 3
        var db = DbContextFactory.CreateInMemory();
        var bridge = TestData.CreateBridge();
        bridge.Systems.Add(TestData.CreateSystem(1, 1, previousSystemId: null));
        bridge.Systems.Add(TestData.CreateSystem(2, 1, previousSystemId: 1));
        bridge.Systems.Add(TestData.CreateSystem(3, 1, previousSystemId: 2));
        db.Bridges.Add(bridge);
        await db.SaveChangesAsync();
        var svc = new BridgeService(db);

        // Act — déplacer le système 3 à la position 1 (tête) → 3 → 1 → 2
        var result = await svc.MoveSystemAsync(3, insertAtIndex: 1);

        // Assert
        Assert.That(result!.Order, Is.EqualTo(1));
        Assert.That(db.StarSystems.First(s => s.Id == 3).PreviousSystemId, Is.Null, "Système 3 doit devenir tête");
        Assert.That(db.StarSystems.First(s => s.Id == 1).PreviousSystemId, Is.EqualTo(3), "Système 1 doit suivre 3");
        Assert.That(db.StarSystems.First(s => s.Id == 2).PreviousSystemId, Is.EqualTo(1), "Système 2 doit suivre 1");
    }

    [Test]
    public async Task MoveSystemAsync_MovingByOnePosition_PreservesChain()
    {
        // Arrange : 1 → 2 → 3 → 4 → 5 (comme dans la BD de test)
        var db = DbContextFactory.CreateInMemory();
        var bridge = TestData.CreateBridge();
        bridge.Systems.Add(TestData.CreateSystem(1, 1, previousSystemId: null));
        bridge.Systems.Add(TestData.CreateSystem(2, 1, previousSystemId: 1));
        bridge.Systems.Add(TestData.CreateSystem(3, 1, previousSystemId: 2));
        bridge.Systems.Add(TestData.CreateSystem(4, 1, previousSystemId: 3));
        bridge.Systems.Add(TestData.CreateSystem(5, 1, previousSystemId: 4));
        db.Bridges.Add(bridge);
        await db.SaveChangesAsync();
        var svc = new BridgeService(db);

        // Act — déplacer le système 4 (ordre 4) à l'ordre 5 → 1 → 2 → 3 → 5 → 4
        var result = await svc.MoveSystemAsync(4, insertAtIndex: 5);

        // Assert
        Assert.That(result!.Order, Is.EqualTo(5));
        Assert.That(db.StarSystems.First(s => s.Id == 1).PreviousSystemId, Is.Null);
        Assert.That(db.StarSystems.First(s => s.Id == 2).PreviousSystemId, Is.EqualTo(1));
        Assert.That(db.StarSystems.First(s => s.Id == 3).PreviousSystemId, Is.EqualTo(2));
        Assert.That(db.StarSystems.First(s => s.Id == 5).PreviousSystemId, Is.EqualTo(3), "Système 5 doit suivre 3");
        Assert.That(db.StarSystems.First(s => s.Id == 4).PreviousSystemId, Is.EqualTo(5), "Système 4 doit suivre 5 (et non pointer vers lui-même)");
    }

    [Test]
    public async Task MoveSystemAsync_WhenNotExists_ReturnsNull()
    {
        // Arrange
        var db = DbContextFactory.CreateInMemory();
        var bridge = TestData.CreateBridge();
        bridge.Systems.Add(TestData.CreateSystem(1, 1, previousSystemId: null));
        db.Bridges.Add(bridge);
        await db.SaveChangesAsync();
        var svc = new BridgeService(db);

        // Act
        var result = await svc.MoveSystemAsync(999, insertAtIndex: 1);

        // Assert
        Assert.That(result, Is.Null);
    }

    // ── DeleteSystem ───────────────────────────────────────────────────────

    [Test]
    public async Task DeleteSystemAsync_RemovesAndCompactsOrders()
    {
        // Arrange
        var db = DbContextFactory.CreateInMemory();
        var bridge = TestData.CreateBridge();
        bridge.Systems.Add(TestData.CreateSystem(1, 1, null));
        bridge.Systems.Add(TestData.CreateSystem(2, 1, 1));
        bridge.Systems.Add(TestData.CreateSystem(3, 1, 2));
        db.Bridges.Add(bridge);
        await db.SaveChangesAsync();
        var svc = new BridgeService(db);

        // Act — supprimer le système du milieu (ordre 2)
        var ok = await svc.DeleteSystemAsync(2);

        // Assert
        Assert.That(ok, Is.True);
        Assert.That(db.StarSystems.Count(), Is.EqualTo(2));
        // Le système 3 doit être remonté à l'ordre 2        
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
        bridge.Systems.Add(TestData.CreateSystem(1, 1, previousSystemId: null, status: ColonizationStatus.FINI));
        bridge.Systems.Add(TestData.CreateSystem(2, 1, previousSystemId: 1, status: ColonizationStatus.FINI));
        bridge.Systems.Add(TestData.CreateSystem(3, 1, previousSystemId: 2, status: ColonizationStatus.PLANIFIE));
        bridge.Systems.Add(TestData.CreateSystem(4, 1, previousSystemId: 3, status: ColonizationStatus.PLANIFIE));
        db.Bridges.Add(bridge);
        await db.SaveChangesAsync();
        var svc = new BridgeService(db);

        // Act
        var result = await svc.GetBridgeByIdAsync(1);

        // Assert — 2 sur 4 = 50%
        Assert.That(result!.CompletionPercent, Is.EqualTo(50));
    }
}
