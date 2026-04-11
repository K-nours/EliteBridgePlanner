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
    public DbSet<EddnRawMessage> EddnRawMessages => Set<EddnRawMessage>();
    public DbSet<FrontierProfile> FrontierProfiles => Set<FrontierProfile>();
    public DbSet<FrontierUser> FrontierUsers => Set<FrontierUser>();
    public DbSet<FrontierOAuthSession> FrontierOAuthSessions => Set<FrontierOAuthSession>();
    public DbSet<DeclaredChantier> DeclaredChantiers => Set<DeclaredChantier>();

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
            e.HasIndex(x => new { x.GuildId, x.Name }).IsUnique();
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

        modelBuilder.Entity<EddnRawMessage>(e =>
        {
            e.ToTable("EddnRawMessages");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ReceivedAt);
            e.HasIndex(x => x.SchemaRef);
        });

        modelBuilder.Entity<FrontierProfile>(e =>
        {
            e.ToTable("FrontierProfiles");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.FrontierCustomerId).IsUnique();
            e.HasIndex(x => x.GuildId);
            e.HasOne(x => x.Guild).WithMany().HasForeignKey(x => x.GuildId).IsRequired(false);
        });

        modelBuilder.Entity<FrontierUser>(e =>
        {
            e.ToTable("FrontierUsers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CustomerId).IsUnique();
            e.HasIndex(x => x.GuildId);
            e.HasOne(x => x.Guild).WithMany().HasForeignKey(x => x.GuildId).IsRequired(false);
        });

        modelBuilder.Entity<FrontierOAuthSession>(e =>
        {
            e.ToTable("FrontierOAuthSessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.AccessTokenProtected).HasColumnType("varbinary(max)");
            e.Property(x => x.RefreshTokenProtected).HasColumnType("varbinary(max)");
        });

        modelBuilder.Entity<DeclaredChantier>(e =>
        {
            e.ToTable("DeclaredChantiers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.GuildId);
            e.Property(x => x.SystemName).HasMaxLength(512);
            e.Property(x => x.StationName).HasMaxLength(512);
            e.Property(x => x.CmdrName).HasMaxLength(256);
            e.Property(x => x.MarketId).HasMaxLength(64);
            e.Property(x => x.SystemNameKey).HasMaxLength(512);
            e.Property(x => x.StationNameKey).HasMaxLength(512);
            e.Property(x => x.ConstructionResourcesJson).HasColumnType("nvarchar(max)");
            e.HasOne(x => x.Guild).WithMany().HasForeignKey(x => x.GuildId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.GuildId, x.MarketId })
                .IsUnique()
                .HasFilter("[MarketId] IS NOT NULL");
            e.HasIndex(x => new { x.GuildId, x.SystemNameKey, x.StationNameKey })
                .IsUnique()
                .HasFilter("[MarketId] IS NULL");
        });
    }
}
