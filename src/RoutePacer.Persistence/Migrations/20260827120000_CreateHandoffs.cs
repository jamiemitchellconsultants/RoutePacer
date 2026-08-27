using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoutePacer.Persistence.Migrations;

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
