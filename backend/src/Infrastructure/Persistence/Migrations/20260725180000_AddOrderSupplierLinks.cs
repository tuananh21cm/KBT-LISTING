using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Multi SHEIN/Temu billnos per local order (Joint Shipment across shops).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260725180000_AddOrderSupplierLinks")]
public class AddOrderSupplierLinks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OrderSupplierLinks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                SupplierOrderNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                SupplierOrderUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                TrackingNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                Carrier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                TrackingKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                SupplierRetailPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                SupplierSubtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                SupplierShippingFee = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                SupplierOnTimeGuarantee = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                SupplierCouponPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                SupplierMembershipDiscount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                SupplierTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                SupplierTotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                OrderedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ProductIdsJson = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderSupplierLinks", x => x.Id);
                table.ForeignKey(
                    name: "FK_OrderSupplierLinks_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OrderSupplierLinks_SupplierOrderNo",
            table: "OrderSupplierLinks",
            column: "SupplierOrderNo",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_OrderSupplierLinks_OrderId",
            table: "OrderSupplierLinks",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_OrderSupplierLinks_TrackingNo",
            table: "OrderSupplierLinks",
            column: "TrackingNo");

        // Backfill one link per existing SupplierOrderNo; classify C… as warehouse.
        migrationBuilder.Sql(
            """
            INSERT INTO "OrderSupplierLinks" (
                "Id", "OrderId", "SupplierOrderNo", "SupplierOrderUrl",
                "TrackingNo", "Carrier", "TrackingKind",
                "SupplierRetailPrice", "SupplierSubtotal", "SupplierShippingFee",
                "SupplierOnTimeGuarantee", "SupplierCouponPrice", "SupplierMembershipDiscount",
                "SupplierTax", "SupplierTotal",
                "OrderedAt", "ProductIdsJson", "CreatedAt", "UpdatedAt")
            SELECT
                gen_random_uuid(),
                o."Id",
                BTRIM(o."SupplierOrderNo"),
                o."SupplierOrderUrl",
                o."TrackingNo",
                o."ShippingService",
                CASE
                    WHEN o."TrackingNo" IS NULL OR BTRIM(o."TrackingNo") = '' THEN 'none'
                    WHEN o."TrackingNo" ~ '^C[0-9]{10,}$' THEN 'warehouse'
                    ELSE 'carrier'
                END,
                o."SupplierRetailPrice", o."SupplierSubtotal", o."SupplierShippingFee",
                o."SupplierOnTimeGuarantee", o."SupplierCouponPrice", o."SupplierMembershipDiscount",
                o."SupplierTax", o."SupplierTotal",
                o."FulfilPurchasedAt",
                NULL,
                NOW(),
                NOW()
            FROM "Orders" o
            WHERE o."SupplierOrderNo" IS NOT NULL
              AND BTRIM(o."SupplierOrderNo") <> ''
              AND NOT EXISTS (
                  SELECT 1 FROM "OrderSupplierLinks" l
                  WHERE l."SupplierOrderNo" = BTRIM(o."SupplierOrderNo"));
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "OrderSupplierLinks");
    }
}
