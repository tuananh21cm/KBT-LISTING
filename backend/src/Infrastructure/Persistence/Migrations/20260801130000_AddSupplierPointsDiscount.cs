using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260801130000_AddSupplierPointsDiscount")]
public class AddSupplierPointsDiscount : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "SupplierPointsDiscount",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "SupplierPointsDiscount",
            table: "OrderSupplierLinks",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SupplierPointsDiscount",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "SupplierPointsDiscount",
            table: "OrderSupplierLinks");
    }
}
