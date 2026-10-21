using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Mốc người xử lý tự chọn đơn cho một lần mua NCC — khoá để luồng sync không tự chuyển nó sang
/// đơn khác. Dữ liệu cũ để null: mọi link hiện có đều coi như do máy khớp.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260919100000_AddSupplierPurchaseManualLinkedAt")]
public class AddSupplierPurchaseManualLinkedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "ManualLinkedAt",
            table: "SupplierPurchases",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ManualLinkedAt",
            table: "SupplierPurchases");
    }
}
