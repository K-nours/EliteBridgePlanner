using EliteBridgePlanner.Server.Data;
using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Models;
using EliteBridgePlanner.Server.Services.Contracts;
using EliteBridgePlanner.Server.Services.Implementations;
using EliteBridgePlanner.Tests.Helpers;
using NUnit.Framework;

namespace EliteBridgePlanner.Tests.Services;

/// <summary>
/// Tests unitaires de BridgeService avec la nouvelle architecture BridgeStarSystem.
/// Utilise EF Core InMemory — pas de mock nécessaire pour le service lui-même.
/// Pattern : Arrange / Act / Assert clairement séparés.
/// </summary>
[TestFixture]
public class BridgeServiceTests
{
    private IBridgeService _service = null!;
    private AppDbContext _db = null!;

    [SetUp]
    public void SetUp()
    {
        // Nouvelle DB en mémoire isolée pour chaque test
        _db = DbContextFactory.CreateInMemory();
        _service = new BridgeService(_db);
    }

    [TearDown]
    public void TearDown()
    {
        _db?.Dispose();
    }

    // ── GetBridgeById ──────────────────────────────────────────────────────

    [Test]
    public async Task GetBridgeByIdAsync_WhenExists_ReturnsBridgeDto()
    {
        // Arrange
        var bridge = TestData.CreateBridge();
        _db.Bridges.Add(bridge);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetBridgeByIdAsync(1);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(1));
        Assert.That(result.Name, Is.EqualTo("Pont Test"));
    }

    [Test]
    public async Task GetBridgeByIdAsync_WhenNotExists_ReturnsNull()
    {
        // Act
        var result = await _service.GetBridgeByIdAsync(999);

        // Assert
        Assert.That(result, Is.Null);
    }

    // ── CreateBridge ───────────────────────────────────────────────────────

    [Test]
    public async Task CreateBridgeAsync_PersistsAndReturnsBridgeDto()
    {
        // Arrange
        var request = new CreateBridgeRequest("Pont Sol-Colonia", "Pont de test");

        // Act
        var result = await _service.CreateBridgeAsync(request, "user-1");

        // Assert
        Assert.That(result.Id, Is.GreaterThan(0));
        Assert.That(result.Name, Is.EqualTo("Pont Sol-Colonia"));
        Assert.That(_db.Bridges.Count(), Is.EqualTo(1));
    }

    // ── AddSystem ──────────────────────────────────────────────────────────

    [Test]
    public async Task AddSystemAsync_CreatesNewSystemAndAssociation()
    {
        // Arrange
        var bridge = TestData.CreateBridge();
        _db.Bridges.Add(bridge);
        await _db.SaveChangesAsync();

        // Act
        var request = new CreateSystemRequest(
            Name: "Sol",
            Type: "DEBUT",
            Status: "PLANIFIE",
            InsertAtIndex: 1,
            ArchitectId: null,
            BridgeId: 1,
            X: 0,
            Y: 0,
            Z: 0
        );
        var result = await _service.AddSystemAsync(request);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("Sol"));
        Assert.That(result.Type, Is.EqualTo("DEBUT"));
        Assert.That(result.Order, Is.EqualTo(1));
        
        var system = _db.StarSystems.First(s => s.Name == "Sol");
        Assert.That(system.X, Is.EqualTo(0));
        Assert.That(system.Y, Is.EqualTo(0));
        Assert.That(system.Z, Is.EqualTo(0));
    }

    [Test]
    public async Task AddSystemAsync_WithExistingSystem_CreatesOnlyAssociation()
    {
        // Arrange
        var bridge = TestData.CreateBridge();
        var starSystem = TestData.CreateStarSystem(1, "Sol", 0, 0, 0);
        _db.Bridges.Add(bridge);
        _db.StarSystems.Add(starSystem);
        await _db.SaveChangesAsync();

        // Act — ajouter le même système au pont
        var request = new CreateSystemRequest(
            Name: "Sol",
            Type: "DEBUT",
            Status: "PLANIFIE",
            InsertAtIndex: 1,
            ArchitectId: null,
            BridgeId: 1,
            X: null,
            Y: null,
            Z: null
        );
        var result = await _service.AddSystemAsync(request);

        // Assert
        Assert.That(result.Name, Is.EqualTo("Sol"));
        Assert.That(_db.StarSystems.Count(), Is.EqualTo(1), "Pas de doublon de StarSystem");
        Assert.That(_db.BridgeStarSystems.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task AddSystemAsync_AtBeginning_InsertsAtHead()
    {
        // Arrange : créer 2 systèmes déjà chaînés
        var bridge = TestData.CreateBridge();
        _db.Bridges.Add(bridge);
        var sol = TestData.CreateStarSystem(1, "Sol");
        var alphaCentauri = TestData.CreateStarSystem(2, "Alpha Centauri");
        _db.StarSystems.AddRange(sol, alphaCentauri);
        await _db.SaveChangesAsync();

        var bss1 = TestData.CreateBridgeStarSystem(1, 1, 1, previousSystemId: null, type: SystemType.DEBUT);
        var bss2 = TestData.CreateBridgeStarSystem(2, 1, 2, previousSystemId: 1, type: SystemType.FIN);
        _db.BridgeStarSystems.AddRange(bss1, bss2);
        await _db.SaveChangesAsync();

        // Act — ajouter un nouveau système à la position 1 (tête)
        var request = new CreateSystemRequest(
            Name: "Novo",
            Type: "DEBUT",
            Status: "PLANIFIE",
            InsertAtIndex: 1,
            ArchitectId: null,
            BridgeId: 1,
            X: 1,
            Y: 1,
            Z: 1
        );
        var result = await _service.AddSystemAsync(request);

        // Assert
        Assert.That(result.Order, Is.EqualTo(1));
        
        // Vérifier la chaîne : Novo(head) → Sol → AlphaCentauri
        var novo = _db.StarSystems.First(s => s.Name == "Novo");
        var novoAssoc = _db.BridgeStarSystems.First(bs => bs.StarSystemId == novo.Id);
        Assert.That(novoAssoc.PreviousSystemId, Is.Null, "Novo doit être la tête");
        
        var solAssoc = _db.BridgeStarSystems.First(bs => bs.StarSystemId == 1);
        Assert.That(solAssoc.PreviousSystemId, Is.EqualTo(novoAssoc.Id), "Sol doit suivre Novo");
    }

    [Test]
    public async Task AddSystemAsync_InMiddle_InsertsAtCorrectPosition()
    {
        // Arrange : 1 → 2 → 3
        var bridge = TestData.CreateBridge();
        _db.Bridges.Add(bridge);
        
        var sys1 = TestData.CreateStarSystem(1, "Système 1");
        var sys2 = TestData.CreateStarSystem(2, "Système 2");
        var sys3 = TestData.CreateStarSystem(3, "Système 3");
        _db.StarSystems.AddRange(sys1, sys2, sys3);
        await _db.SaveChangesAsync();

        var bss1 = TestData.CreateBridgeStarSystem(1, 1, 1, previousSystemId: null);
        var bss2 = TestData.CreateBridgeStarSystem(2, 1, 2, previousSystemId: 1);
        var bss3 = TestData.CreateBridgeStarSystem(3, 1, 3, previousSystemId: 2);
        _db.BridgeStarSystems.AddRange(bss1, bss2, bss3);
        await _db.SaveChangesAsync();

        // Act — ajouter à la position 3 (entre 2 et 3)
        var request = new CreateSystemRequest(
            Name: "Nouveau",
            Type: "TABLIER",
            Status: "PLANIFIE",
            InsertAtIndex: 3,
            ArchitectId: null,
            BridgeId: 1,
            X: 2,
            Y: 2,
            Z: 2
        );
        var result = await _service.AddSystemAsync(request);

        // Assert : 1 → 2 → Nouveau → 3
        Assert.That(result.Order, Is.EqualTo(3));
        var nouveau = _db.StarSystems.First(s => s.Name == "Nouveau");
        var nouveauAssoc = _db.BridgeStarSystems.First(bs => bs.StarSystemId == nouveau.Id);
        Assert.That(nouveauAssoc.PreviousSystemId, Is.EqualTo(2), "Nouveau doit suivre l'ID de bss2");
        
        var bss3Updated = _db.BridgeStarSystems.First(bs => bs.StarSystemId == 3);
        Assert.That(bss3Updated.PreviousSystemId, Is.EqualTo(nouveauAssoc.Id), "Système 3 doit suivre Nouveau");
    }

    // ── UpdateSystem ───────────────────────────────────────────────────────

    [Test]
    public async Task UpdateSystemAsync_ChangesNameAndCoordinates()
    {
        // Arrange
        var bridge = TestData.CreateBridge();
        _db.Bridges.Add(bridge);

        var starSystem = TestData.CreateStarSystem(1, "Sol", 0, 0, 0);
        _db.StarSystems.Add(starSystem);
        await _db.SaveChangesAsync();

        // Créer une association pour que le système soit lié à un pont
        var bss = TestData.CreateBridgeStarSystem(1, 1, 1, previousSystemId: null);
        _db.BridgeStarSystems.Add(bss);
        await _db.SaveChangesAsync();

        // Act
        var update = new UpdateSystemRequest(
            starSystemId: 1,
            Name: "Solaris",
            Type: null,
            Status: null,
            ArchitectId: null,
            X: 10,
            Y: 20,
            Z: 30
        );
        var result = await _service.UpdateSystemAsync(1, update);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("Solaris"));
        Assert.That(result.X, Is.EqualTo(10));
        Assert.That(result.Y, Is.EqualTo(20));
        Assert.That(result.Z, Is.EqualTo(30));
    }

    [Test]
    public async Task UpdateSystemAsync_WhenNotExists_ReturnsNull()
    {
        // Act
        var result = await _service.UpdateSystemAsync(999, new UpdateSystemRequest(
            starSystemId: 999,
            Name: "X",
            Type: null,
            Status: null,
            ArchitectId: null,
            X: null,
            Y: null,
            Z: null
        ));

        // Assert
        Assert.That(result, Is.Null);
    }

    // ── DeleteSystem ───────────────────────────────────────────────────────

    [Test]
    public async Task DeleteSystemAsync_RemovesAssociation_AndMaintainsChain()
    {
        // Arrange : 1 → 2 → 3
        var bridge = TestData.CreateBridge();
        _db.Bridges.Add(bridge);
        
        var sys1 = TestData.CreateStarSystem(1, "Système 1");
        var sys2 = TestData.CreateStarSystem(2, "Système 2");
        var sys3 = TestData.CreateStarSystem(3, "Système 3");
        _db.StarSystems.AddRange(sys1, sys2, sys3);
        await _db.SaveChangesAsync();

        var bss1 = TestData.CreateBridgeStarSystem(1, 1, 1, previousSystemId: null);
        var bss2 = TestData.CreateBridgeStarSystem(2, 1, 2, previousSystemId: 1);
        var bss3 = TestData.CreateBridgeStarSystem(3, 1, 3, previousSystemId: 2);
        _db.BridgeStarSystems.AddRange(bss1, bss2, bss3);
        await _db.SaveChangesAsync();

        // Act — supprimer l'association bss2 (Système 2)
        var result = await _service.DeleteSystemAsync(2);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(_db.BridgeStarSystems.Count(), Is.EqualTo(2), "Une seule association supprimée");
        
        // StarSystem 2 persiste (n'est pas un système, c'est une association)
        // Mais maintenant la chaîne est : 1 → 3
        var bss3Updated = _db.BridgeStarSystems.First(bs => bs.StarSystemId == 3);
        Assert.That(bss3Updated.PreviousSystemId, Is.EqualTo(1), "Système 3 doit suivre 1");
    }

    // ── Integration Tests ──────────────────────────────────────────────────

    [Test]
    public async Task Bridge_CanContainMultipleSystemsWithDifferentRoles()
    {
        // Arrange
        var bridge = TestData.CreateBridge();
        _db.Bridges.Add(bridge);

        var sol = TestData.CreateStarSystem(1, "Sol");
        var alphaCentauri = TestData.CreateStarSystem(2, "Alpha Centauri");
        _db.StarSystems.AddRange(sol, alphaCentauri);
        await _db.SaveChangesAsync();

        // Act — ajouter Sol comme DEBUT et Alpha Centauri comme PILE au même pont
        var req1 = new CreateSystemRequest("Sol", "DEBUT", "FINI", 1, null, 1, 0, 0, 0);
        var req2 = new CreateSystemRequest("Alpha Centauri", "PILE", "PLANIFIE", 2, null, 1, 4.37f, 0, 0);
        
        await _service.AddSystemAsync(req1);
        await _service.AddSystemAsync(req2);

        // Assert
        var bridgeSystems = _db.BridgeStarSystems.Where(bs => bs.BridgeId == 1).ToList();
        Assert.That(bridgeSystems.Count, Is.EqualTo(2));
        
        var solAssoc = bridgeSystems.First(bs => bs.StarSystemId == 1);
        var acAssoc = bridgeSystems.First(bs => bs.StarSystemId == 2);
        
        Assert.That(solAssoc.Type.ToString(), Is.EqualTo("DEBUT"));
        Assert.That(acAssoc.Type.ToString(), Is.EqualTo("PILE"));
    }

    [Test]
    public async Task SameSystem_CanBeInMultipleBridgesWithDifferentRoles()
    {
        // Arrange
        var bridge1 = TestData.CreateBridge();
        var bridge2 = new Bridge { Id = 2, Name = "Pont 2", CreatedByUserId = "user-1" };
        _db.Bridges.AddRange(bridge1, bridge2);

        var sol = TestData.CreateStarSystem(1, "Sol");
        _db.StarSystems.Add(sol);
        await _db.SaveChangesAsync();

        // Act — ajouter Sol dans les deux ponts avec des rôles différents
        var req1 = new CreateSystemRequest("Sol", "DEBUT", "FINI", 1, null, 1, 0, 0, 0);
        var req2 = new CreateSystemRequest("Sol", "PILE", "PLANIFIE", 1, null, 2, 0, 0, 0);

        await _service.AddSystemAsync(req1);
        await _service.AddSystemAsync(req2);

        // Assert
        var solAssociations = _db.BridgeStarSystems.Where(bs => bs.StarSystemId == 1).ToList();
        Assert.That(solAssociations.Count, Is.EqualTo(2), "Sol doit être dans 2 ponts");

        var solInBridge1 = solAssociations.First(bs => bs.BridgeId == 1);
        var solInBridge2 = solAssociations.First(bs => bs.BridgeId == 2);

        Assert.That(solInBridge1.Type.ToString(), Is.EqualTo("DEBUT"));
        Assert.That(solInBridge2.Type.ToString(), Is.EqualTo("PILE"));
    }
}
