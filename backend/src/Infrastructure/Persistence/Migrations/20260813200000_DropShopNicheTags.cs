using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Drop unused Shop.NicheTags — TTS ProductCategories is the only shop-category JSON.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260813200000_DropShopNicheTags")]
public class DropShopNicheTags : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "NicheTags",
            table: "Shops");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "NicheTags",
            table: "Shops",
            type: "jsonb",
            nullable: true);
    }
}
