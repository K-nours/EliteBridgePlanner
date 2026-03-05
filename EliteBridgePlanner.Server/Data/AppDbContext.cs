using EliteBridgePlanner.Server.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EliteBridgePlanner.Server.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    // Constructeur avec options — OBLIGATOIRE pour l'injection de dépendances et les tests
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Bridge> Bridges => Set<Bridge>();
    public DbSet<StarSystem> StarSystems => Set<StarSystem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ── Bridge ────────────────────────────────────────────────────────
        builder.Entity<Bridge>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Name).IsRequired().HasMaxLength(200);
            e.Property(b => b.Description).HasMaxLength(1000);

            e.HasOne(b => b.CreatedBy)
             .WithMany(u => u.CreatedBridges)
             .HasForeignKey(b => b.CreatedByUserId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ── StarSystem ────────────────────────────────────────────────────
        builder.Entity<StarSystem>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).IsRequired().HasMaxLength(200);

            // Stocker les enums en string pour lisibilité DB
            e.Property(s => s.Type)
             .HasConversion<string>()
             .HasMaxLength(20);

            e.Property(s => s.Status)
             .HasConversion<string>()
             .HasMaxLength(20);

            // Relation chaînée Previous
            e.HasOne(s => s.PreviousSystem)
             .WithOne(s => s.NextSystem)
             .HasForeignKey<StarSystem>(s => s.PreviousSystemId)
             .OnDelete(DeleteBehavior.NoAction)
             .IsRequired(false);

            e.HasOne(s => s.Bridge)
             .WithMany(b => b.Systems)
             .HasForeignKey(s => s.BridgeId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(s => s.Architect)
             .WithMany(u => u.ArchitectedSystems)
             .HasForeignKey(s => s.ArchitectId)
             .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
