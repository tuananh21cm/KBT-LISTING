using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// SHEIN Joint Shipment group keys: relation_billno + confluence_batch_no.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260725183000_AddOrderSupplierLinkRelationBillno")]
public class AddOrderSupplierLinkRelationBillno : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RelationBillno",
            table: "OrderSupplierLinks",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ConfluenceBatchNo",
            table: "OrderSupplierLinks",
            type: "character varying(80)",
            maxLength: 80,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_OrderSupplierLinks_RelationBillno",
            table: "OrderSupplierLinks",
            column: "RelationBillno");

        migrationBuilder.CreateIndex(
            name: "IX_OrderSupplierLinks_ConfluenceBatchNo",
            table: "OrderSupplierLinks",
            column: "ConfluenceBatchNo");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OrderSupplierLinks_ConfluenceBatchNo",
            table: "OrderSupplierLinks");

        migrationBuilder.DropIndex(
            name: "IX_OrderSupplierLinks_RelationBillno",
            table: "OrderSupplierLinks");

        migrationBuilder.DropColumn(
            name: "ConfluenceBatchNo",
            table: "OrderSupplierLinks");

        migrationBuilder.DropColumn(
            name: "RelationBillno",
            table: "OrderSupplierLinks");
    }
}
