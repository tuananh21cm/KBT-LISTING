using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260902120000_AddPlatformShippingServices")]
public class AddPlatformShippingServices : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlatformShippingServices",
            columns: table => new
            {
                FourSellerId = table.Column<long>(type: "bigint", nullable: false),
                Carrier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                Region = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlatformShippingServices", x => x.FourSellerId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PlatformShippingServices_Platform",
            table: "PlatformShippingServices",
            column: "Platform");

        migrationBuilder.CreateIndex(
            name: "IX_PlatformShippingServices_SyncedAt",
            table: "PlatformShippingServices",
            column: "SyncedAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PlatformShippingServices");
    }
}
