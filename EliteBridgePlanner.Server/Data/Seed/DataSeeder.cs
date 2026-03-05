using EliteBridgePlanner.Server.Models;
using Microsoft.AspNetCore.Identity;

namespace EliteBridgePlanner.Server.Data.Seed;

public class DataSeeder
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;

    // Injection de dépendances — pas de new UserManager ici
    public DataSeeder(AppDbContext db, UserManager<AppUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task SeedAsync()
    {
        if (_db.Bridges.Any()) return;

        var cmdr = new AppUser
        {
            Email = "cmdr@demo.local",
            UserName = "cmdr@demo.local",
            CommanderName = "CMDR_DEMO"
        };

        if (await _userManager.FindByEmailAsync(cmdr.Email) is null)
            await _userManager.CreateAsync(cmdr, "Demo1234!");

        var user = await _userManager.FindByEmailAsync(cmdr.Email);

        // Créer le pont sans systèmes d'abord
        var bridge = new Bridge
        {
            Name = "Pont Sol → Colonia",
            Description = "Pont de démonstration POC",
            CreatedByUserId = user!.Id
        };
        _db.Bridges.Add(bridge);
        await _db.SaveChangesAsync();

        // Créer les systèmes sans liens — les IDs sont générés après SaveChanges
        var sol = new StarSystem { Name = "Sol", Type = SystemType.DEBUT, Status = ColonizationStatus.FINI, BridgeId = bridge.Id, ArchitectId = user.Id };
        var alphaCentauri = new StarSystem { Name = "Alpha Centauri", Type = SystemType.TABLIER, Status = ColonizationStatus.FINI, BridgeId = bridge.Id, ArchitectId = user.Id };
        var barnardStar = new StarSystem { Name = "Barnard's Star", Type = SystemType.PILE, Status = ColonizationStatus.CONSTRUCTION, BridgeId = bridge.Id };
        var wolf359 = new StarSystem { Name = "Wolf 359", Type = SystemType.TABLIER, Status = ColonizationStatus.PLANIFIE, BridgeId = bridge.Id };
        var sirius = new StarSystem { Name = "Sirius", Type = SystemType.PILE, Status = ColonizationStatus.PLANIFIE, BridgeId = bridge.Id };
        var colonia = new StarSystem { Name = "Colonia", Type = SystemType.FIN, Status = ColonizationStatus.PLANIFIE, BridgeId = bridge.Id };

        _db.StarSystems.AddRange(sol, alphaCentauri, barnardStar, wolf359, sirius, colonia);
        await _db.SaveChangesAsync(); // Les IDs sont maintenant générés

        // Chaîner dans l'ordre : Sol → Alpha Centauri → Barnard's Star → Wolf 359 → Sirius → Colonia
        sol.NextSystemId = alphaCentauri.Id;

        alphaCentauri.PreviousSystemId = sol.Id;
        alphaCentauri.NextSystemId = barnardStar.Id;

        barnardStar.PreviousSystemId = alphaCentauri.Id;
        barnardStar.NextSystemId = wolf359.Id;

        wolf359.PreviousSystemId = barnardStar.Id;
        wolf359.NextSystemId = sirius.Id;

        sirius.PreviousSystemId = wolf359.Id;
        sirius.NextSystemId = colonia.Id;

        colonia.PreviousSystemId = sirius.Id;
        // colonia.NextSystemId = null → fin de chaîne

        await _db.SaveChangesAsync();
    }
}
