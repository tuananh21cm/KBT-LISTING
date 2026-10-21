using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Manual flag/timestamp: ops finished TikTok REFUND handling.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260808120000_AddOrderRefundProcessedAt")]
public class AddOrderRefundProcessedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "RefundProcessedAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RefundProcessedAt",
            table: "Orders");
    }
}
