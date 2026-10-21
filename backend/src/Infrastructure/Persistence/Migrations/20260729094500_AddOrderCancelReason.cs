using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>Persist TikTok/4Seller cancelReason and support PlatformOrderStatus REFUND.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260729094500_AddOrderCancelReason")]
public class AddOrderCancelReason : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CancelReason",
            table: "Orders",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CancelReason",
            table: "Orders");
    }
}
