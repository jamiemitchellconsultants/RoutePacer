using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RoutePacer.Persistence;

#nullable disable

namespace RoutePacer.Persistence.Migrations;

[DbContext(typeof(RoutePacerDbContext))]
partial class RoutePacerDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.10");
        modelBuilder.Entity("RoutePacer.Persistence.Handoffs.HandoffRecord", b =>
        {
            b.Property<byte[]>("TokenHash").HasColumnType("bytea").HasColumnName("token_hash");
            b.Property<byte[]>("Content").IsRequired().HasColumnType("bytea").HasColumnName("content");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamptz").HasColumnName("created_at");
            b.Property<DateTimeOffset>("ExpiresAt").HasColumnType("timestamptz").HasColumnName("expires_at");
            b.HasKey("TokenHash");
            b.ToTable("handoffs");
        });
    }
}
