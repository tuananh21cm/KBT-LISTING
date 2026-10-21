using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Rename SHEIN-specific order cost columns to platform-agnostic Supplier* names
/// (reusable for Temu and other fulfil sources).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260723140000_RenameOrderSheinToSupplierColumns")]
public class RenameOrderSheinToSupplierColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "SheinBillNo",
            table: "Orders",
            newName: "SupplierOrderNo");

        migrationBuilder.RenameColumn(
            name: "SheinOrderUrl",
            table: "Orders",
            newName: "SupplierOrderUrl");

        migrationBuilder.RenameColumn(
            name: "SheinRetailPrice",
            table: "Orders",
            newName: "SupplierRetailPrice");

        migrationBuilder.RenameColumn(
            name: "SheinSubtotal",
            table: "Orders",
            newName: "SupplierSubtotal");

        migrationBuilder.RenameColumn(
            name: "SheinShippingFee",
            table: "Orders",
            newName: "SupplierShippingFee");

        migrationBuilder.RenameColumn(
            name: "SheinOnTimeGuarantee",
            table: "Orders",
            newName: "SupplierOnTimeGuarantee");

        migrationBuilder.RenameColumn(
            name: "SheinCouponPrice",
            table: "Orders",
            newName: "SupplierCouponPrice");

        migrationBuilder.RenameColumn(
            name: "SheinClubDiscount",
            table: "Orders",
            newName: "SupplierMembershipDiscount");

        migrationBuilder.RenameColumn(
            name: "SheinTax",
            table: "Orders",
            newName: "SupplierTax");

        migrationBuilder.RenameColumn(
            name: "SheinTotal",
            table: "Orders",
            newName: "SupplierTotal");

        migrationBuilder.RenameIndex(
            name: "IX_Orders_SheinBillNo",
            table: "Orders",
            newName: "IX_Orders_SupplierOrderNo");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameIndex(
            name: "IX_Orders_SupplierOrderNo",
            table: "Orders",
            newName: "IX_Orders_SheinBillNo");

        migrationBuilder.RenameColumn(
            name: "SupplierOrderNo",
            table: "Orders",
            newName: "SheinBillNo");

        migrationBuilder.RenameColumn(
            name: "SupplierOrderUrl",
            table: "Orders",
            newName: "SheinOrderUrl");

        migrationBuilder.RenameColumn(
            name: "SupplierRetailPrice",
            table: "Orders",
            newName: "SheinRetailPrice");

        migrationBuilder.RenameColumn(
            name: "SupplierSubtotal",
            table: "Orders",
            newName: "SheinSubtotal");

        migrationBuilder.RenameColumn(
            name: "SupplierShippingFee",
            table: "Orders",
            newName: "SheinShippingFee");

        migrationBuilder.RenameColumn(
            name: "SupplierOnTimeGuarantee",
            table: "Orders",
            newName: "SheinOnTimeGuarantee");

        migrationBuilder.RenameColumn(
            name: "SupplierCouponPrice",
            table: "Orders",
            newName: "SheinCouponPrice");

        migrationBuilder.RenameColumn(
            name: "SupplierMembershipDiscount",
            table: "Orders",
            newName: "SheinClubDiscount");

        migrationBuilder.RenameColumn(
            name: "SupplierTax",
            table: "Orders",
            newName: "SheinTax");

        migrationBuilder.RenameColumn(
            name: "SupplierTotal",
            table: "Orders",
            newName: "SheinTotal");
    }
}
