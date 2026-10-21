using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>Shein Local US + mall_stock Quick ship flags on SupplierProductInfos.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260806010000_AddSupplierProductLocalQuickShip")]
public class AddSupplierProductLocalQuickShip : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsLocalProduct",
            table: "SupplierProductInfos",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsSupportQuickShip",
            table: "SupplierProductInfos",
            type: "boolean",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsLocalProduct",
            table: "SupplierProductInfos");

        migrationBuilder.DropColumn(
            name: "IsSupportQuickShip",
            table: "SupplierProductInfos");
    }
}
