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
    public DbSet<BridgeStarSystem> BridgeStarSystems => Set<BridgeStarSystem>();

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

            // Créer un index unique sur le nom pour la recherche rapide
            e.HasIndex(s => s.Name).IsUnique();

            e.HasOne(s => s.Architect)
             .WithMany(u => u.ArchitectedSystems)
             .HasForeignKey(s => s.ArchitectId)
             .OnDelete(DeleteBehavior.SetNull);
            e.Property(bs => bs.Status)
             .HasConversion<string>()
             .HasMaxLength(20);
        });

        // ── BridgeStarSystem (Jonction) ────────────────────────────────────
        builder.Entity<BridgeStarSystem>(e =>
        {
            e.HasKey(bs => bs.Id);

            // Clé composite unique : un système ne peut être dans un pont qu'une seule fois
            e.HasAlternateKey(bs => new { bs.BridgeId, bs.StarSystemId });

            // Relation vers Bridge
            e.HasOne(bs => bs.Bridge)
             .WithMany(b => b.Systems)
             .HasForeignKey(bs => bs.BridgeId)
             .OnDelete(DeleteBehavior.Cascade);

            // Relation vers StarSystem
            e.HasOne(bs => bs.StarSystem)
             .WithMany(s => s.BridgeAssociations)
             .HasForeignKey(bs => bs.StarSystemId)
             .OnDelete(DeleteBehavior.Cascade);

            // Chaînage intra-pont
            e.HasOne(bs => bs.PreviousSystem)
             .WithMany()
             .HasForeignKey(bs => bs.PreviousSystemId)
             .OnDelete(DeleteBehavior.NoAction)
             .IsRequired(false);

            // Enums en string
            e.Property(bs => bs.Type)
             .HasConversion<string>()
             .HasMaxLength(20);


        });
    }
}
