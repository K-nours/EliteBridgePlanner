using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Data;

/// <summary>DbContext minimal pour Guild Systems uniquement.</summary>
public class GuildDashboardDbContext : DbContext
{
    public GuildDashboardDbContext(DbContextOptions<GuildDashboardDbContext> options)
        : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    public DbSet<Guild> Guilds => Set<Guild>();
    public DbSet<GuildSystem> GuildSystems => Set<GuildSystem>();
    public DbSet<ControlledSystem> ControlledSystems => Set<ControlledSystem>();
    public DbSet<SquadronMember> SquadronMembers => Set<SquadronMember>();
    public DbSet<SquadronSnapshot> SquadronSnapshots => Set<SquadronSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Guild>(e =>
        {
            e.ToTable("Guilds");
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<GuildSystem>(e =>
        {
            e.ToTable("GuildSystems");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.GuildId);
        });

        modelBuilder.Entity<ControlledSystem>(e =>
        {
            e.ToTable("ControlledSystems");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.GuildId);
        });

        modelBuilder.Entity<SquadronMember>(e =>
        {
            e.ToTable("SquadronMembers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.GuildId);
            e.HasIndex(x => new { x.GuildId, x.CommanderName }).IsUnique();
            e.HasOne(x => x.Guild).WithMany(g => g.SquadronMembers).HasForeignKey(x => x.GuildId);
        });

        modelBuilder.Entity<SquadronSnapshot>(e =>
        {
            e.ToTable("SquadronSnapshots");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.GuildId);
            e.HasOne(x => x.Guild).WithMany(g => g.SquadronSnapshots).HasForeignKey(x => x.GuildId);
        });
    }
}
