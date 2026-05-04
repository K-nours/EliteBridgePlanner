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

        // Créer le pont
        var bridge = new Bridge
        {
            Name = "Pont Sol → Colonia",
            Description = "Pont de démonstration POC",
            CreatedByUserId = user!.Id
        };
        _db.Bridges.Add(bridge);
        await _db.SaveChangesAsync();

        // Créer les systèmes stellaires (indépendants des ponts)
        var sol = new StarSystem { Name = "Sol", ArchitectId = user.Id, X = 0, Y = 0, Z = 0 , Status = ColonizationStatus.FINI };
        var alphaCentauri = new StarSystem { Name = "Alpha Centauri", ArchitectId = user.Id, X = 4.37f, Y = 0, Z = 0, Status = ColonizationStatus.FINI };
        var barnardStar = new StarSystem { Name = "Barnard's Star", X = 5.96f, Y = 0, Z = 0, Status = ColonizationStatus.CONSTRUCTION };
        var wolf359 = new StarSystem { Name = "Wolf 359", X = 7.78f, Y = 0, Z = 0, Status = ColonizationStatus.PLANIFIE };
        var sirius = new StarSystem { Name = "Sirius", X = 8.6f, Y = 0, Z = 0, Status = ColonizationStatus.PLANIFIE };
        var colonia = new StarSystem { Name = "Colonia", X = 22000, Y = 0, Z = 0, Status = ColonizationStatus.PLANIFIE };

        _db.StarSystems.AddRange(sol, alphaCentauri, barnardStar, wolf359, sirius, colonia);
        await _db.SaveChangesAsync(); // Les IDs sont générés

        // Créer les associations Bridge-StarSystem avec leurs rôles spécifiques au pont
        var bss1 = new BridgeStarSystem { BridgeId = bridge.Id, StarSystemId = sol.Id, Type = SystemType.DEBUT };
        var bss2 = new BridgeStarSystem { BridgeId = bridge.Id, StarSystemId = alphaCentauri.Id, Type = SystemType.TABLIER };
        var bss3 = new BridgeStarSystem { BridgeId = bridge.Id, StarSystemId = barnardStar.Id, Type = SystemType.PILE};
        var bss4 = new BridgeStarSystem { BridgeId = bridge.Id, StarSystemId = wolf359.Id, Type = SystemType.TABLIER };
        var bss5 = new BridgeStarSystem { BridgeId = bridge.Id, StarSystemId = sirius.Id, Type = SystemType.PILE};
        var bss6 = new BridgeStarSystem { BridgeId = bridge.Id, StarSystemId = colonia.Id, Type = SystemType.FIN };

        _db.BridgeStarSystems.AddRange(bss1, bss2, bss3, bss4, bss5, bss6);
        await _db.SaveChangesAsync();

        // Chaîner dans l'ordre du pont : Sol → Alpha Centauri → Barnard's Star → Wolf 359 → Sirius → Colonia
        bss2.PreviousSystemId = bss1.Id;
        bss3.PreviousSystemId = bss2.Id;
        bss4.PreviousSystemId = bss3.Id;
        bss5.PreviousSystemId = bss4.Id;
        bss6.PreviousSystemId = bss5.Id;

        await _db.SaveChangesAsync();
    }
}
