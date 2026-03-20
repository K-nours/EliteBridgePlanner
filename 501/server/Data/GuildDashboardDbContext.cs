using GuildDashboard.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Data;

/// <summary>DbContext minimal pour Guild Systems uniquement.</summary>
public class GuildDashboardDbContext : DbContext
{
    public GuildDashboardDbContext(DbContextOptions<GuildDashboardDbContext> options)
        : base(options) { }

    public DbSet<Guild> Guilds => Set<Guild>();
    public DbSet<GuildSystem> GuildSystems => Set<GuildSystem>();
    public DbSet<ControlledSystem> ControlledSystems => Set<ControlledSystem>();

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
    }
}
