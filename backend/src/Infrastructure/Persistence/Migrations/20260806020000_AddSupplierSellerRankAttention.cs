using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>Shein shopRank + storeAttention on SupplierSellerInfos.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260806020000_AddSupplierSellerRankAttention")]
public class AddSupplierSellerRankAttention : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "ShopRank",
            table: "SupplierSellerInfos",
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "StoreAttention",
            table: "SupplierSellerInfos",
            type: "integer",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ShopRank",
            table: "SupplierSellerInfos");

        migrationBuilder.DropColumn(
            name: "StoreAttention",
            table: "SupplierSellerInfos");
    }
}
