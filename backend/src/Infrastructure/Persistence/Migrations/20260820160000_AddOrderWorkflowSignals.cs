using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Denormalized workflow signals on Orders so the list tab filter and its badge counts read an
/// index instead of a correlated EXISTS over SupplierPurchases plus LOWER(...) LIKE '%usps%'.
///
/// <c>WorkflowTab</c> stays nullable and the flags default to false: rows are only correct after
/// <c>OrderWorkflowSignalsBackfill</c> stamps them at startup, and a null tab is the signal that a
/// row has not been stamped yet.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260820160000_AddOrderWorkflowSignals")]
public class AddOrderWorkflowSignals : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "WorkflowTab",
            table: "Orders",
            type: "character varying(40)",
            maxLength: 40,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "HasCarrierTracking",
            table: "Orders",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "HasSupplierPurchase",
            table: "Orders",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "HasUspsSignal",
            table: "Orders",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "HasWarehouseSignal",
            table: "Orders",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateIndex(
            name: "IX_Orders_WorkflowTab",
            table: "Orders",
            column: "WorkflowTab");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_OwnerId_WorkflowTab",
            table: "Orders",
            columns: ["OwnerId", "WorkflowTab"]);

        migrationBuilder.CreateIndex(
            name: "IX_Orders_HasUspsSignal_UspsProcessedAt",
            table: "Orders",
            columns: ["HasUspsSignal", "UspsProcessedAt"]);

        migrationBuilder.CreateIndex(
            name: "IX_Orders_HasWarehouseSignal_ThankCardProcessedAt",
            table: "Orders",
            columns: ["HasWarehouseSignal", "ThankCardProcessedAt"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Orders_HasWarehouseSignal_ThankCardProcessedAt", table: "Orders");
        migrationBuilder.DropIndex(name: "IX_Orders_HasUspsSignal_UspsProcessedAt", table: "Orders");
        migrationBuilder.DropIndex(name: "IX_Orders_OwnerId_WorkflowTab", table: "Orders");
        migrationBuilder.DropIndex(name: "IX_Orders_WorkflowTab", table: "Orders");

        migrationBuilder.DropColumn(name: "HasWarehouseSignal", table: "Orders");
        migrationBuilder.DropColumn(name: "HasUspsSignal", table: "Orders");
        migrationBuilder.DropColumn(name: "HasSupplierPurchase", table: "Orders");
        migrationBuilder.DropColumn(name: "HasCarrierTracking", table: "Orders");
        migrationBuilder.DropColumn(name: "WorkflowTab", table: "Orders");
    }
}
