using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Shop catalog niche: fashion style codes (jsonb string array).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260814170000_AddShopStyleCodes")]
public class AddShopStyleCodes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "StyleCodes",
            table: "Shops",
            type: "jsonb",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "StyleCodes",
            table: "Shops");
    }
}
