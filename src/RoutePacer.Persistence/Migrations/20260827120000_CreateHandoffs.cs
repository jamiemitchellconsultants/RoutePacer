using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoutePacer.Persistence.Migrations;

// EF Core discovers migrations by this attribute; without it MigrateAsync applies nothing and a fresh
// deployment comes up with no handoffs table.
[DbContext(typeof(RoutePacerDbContext))]
[Migration("20260827120000_CreateHandoffs")]
public partial class CreateHandoffs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("handoffs", table => new
        {
            token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
            content = table.Column<byte[]>(type: "bytea", nullable: false),
            created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
            expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_handoffs", x => x.token_hash));
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("handoffs");
}
