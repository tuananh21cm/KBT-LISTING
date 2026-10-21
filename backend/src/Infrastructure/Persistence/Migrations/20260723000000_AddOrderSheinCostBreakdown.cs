using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260723000000_AddOrderSheinCostBreakdown")]
public class AddOrderSheinCostBreakdown : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SheinBillNo",
            table: "Orders",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "SheinRetailPrice",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "SheinSubtotal",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "SheinShippingFee",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "SheinOnTimeGuarantee",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "SheinDiscount",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "SheinClubSavings",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "SheinSalesTax",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "SheinTotal",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Orders_SheinBillNo",
            table: "Orders",
            column: "SheinBillNo");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Orders_SheinBillNo",
            table: "Orders");

        migrationBuilder.DropColumn(name: "SheinBillNo", table: "Orders");
        migrationBuilder.DropColumn(name: "SheinRetailPrice", table: "Orders");
        migrationBuilder.DropColumn(name: "SheinSubtotal", table: "Orders");
        migrationBuilder.DropColumn(name: "SheinShippingFee", table: "Orders");
        migrationBuilder.DropColumn(name: "SheinOnTimeGuarantee", table: "Orders");
        migrationBuilder.DropColumn(name: "SheinDiscount", table: "Orders");
        migrationBuilder.DropColumn(name: "SheinClubSavings", table: "Orders");
        migrationBuilder.DropColumn(name: "SheinSalesTax", table: "Orders");
        migrationBuilder.DropColumn(name: "SheinTotal", table: "Orders");
    }
}
