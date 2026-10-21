using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Catalog preference: prefer US warehouse products.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260814030000_AddShopPreferUsWarehouse")]
public class AddShopPreferUsWarehouse : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "PreferUsWarehouse",
            table: "Shops",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PreferUsWarehouse",
            table: "Shops");
    }
}
