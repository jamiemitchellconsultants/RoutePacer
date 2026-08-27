using Microsoft.EntityFrameworkCore;
using RoutePacer.Persistence.Handoffs;

namespace RoutePacer.Persistence;

public sealed class RoutePacerDbContext(DbContextOptions<RoutePacerDbContext> options) : DbContext(options)
{
    public DbSet<HandoffRecord> Handoffs => Set<HandoffRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<HandoffRecord>();
        entity.ToTable("handoffs");
        entity.HasKey(x => x.TokenHash);
        entity.Property(x => x.TokenHash).HasColumnName("token_hash").HasColumnType("bytea");
        entity.Property(x => x.Content).HasColumnName("content").HasColumnType("bytea").IsRequired();
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz");
    }
}
