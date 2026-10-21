using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Indexes for admin order list filter/sort by status / owner + OrderPaidTime.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260724130000_AddOrderListIndexes")]
public class AddOrderListIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Orders_OrderStatus_OrderPaidTime",
            table: "Orders",
            columns: new[] { "OrderStatus", "OrderPaidTime" });

        migrationBuilder.CreateIndex(
            name: "IX_Orders_OwnerId_OrderPaidTime",
            table: "Orders",
            columns: new[] { "OwnerId", "OrderPaidTime" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Orders_OwnerId_OrderPaidTime",
            table: "Orders");

        migrationBuilder.DropIndex(
            name: "IX_Orders_OrderStatus_OrderPaidTime",
            table: "Orders");
    }
}
