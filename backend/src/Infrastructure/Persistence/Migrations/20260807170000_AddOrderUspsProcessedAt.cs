using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Manual flag/timestamp: USPS last-mile extra handling completed.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260807170000_AddOrderUspsProcessedAt")]
public class AddOrderUspsProcessedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "UspsProcessedAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "UspsProcessedAt",
            table: "Orders");
    }
}
