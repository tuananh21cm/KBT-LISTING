using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260807180000_AddAutoMarkShipRequestId")]
public class AddAutoMarkShipRequestId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AutoMarkShipRequestId",
            table: "SupplierPurchases",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_SupplierPurchases_AutoMarkShipStatus_TrackingKind",
            table: "SupplierPurchases",
            columns: new[] { "AutoMarkShipStatus", "TrackingKind" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_SupplierPurchases_AutoMarkShipStatus_TrackingKind",
            table: "SupplierPurchases");

        migrationBuilder.DropColumn(
            name: "AutoMarkShipRequestId",
            table: "SupplierPurchases");
    }
}
