using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260807103000_AddSupplierWalletAndRetailDeliveryFee")]
public class AddSupplierWalletAndRetailDeliveryFee : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "SupplierWalletPrice",
            table: "SupplierPurchases",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "SupplierRetailDeliveryFee",
            table: "SupplierPurchases",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SupplierWalletPrice",
            table: "SupplierPurchases");

        migrationBuilder.DropColumn(
            name: "SupplierRetailDeliveryFee",
            table: "SupplierPurchases");
    }
}
