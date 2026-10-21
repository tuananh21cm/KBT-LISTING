using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Per-seller delta watermark for listing sync, so a run can ask 4Seller only for rows changed since
/// the last fully successful pull instead of re-reading every page.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260820110000_AddListingSyncWatermarks")]
public class AddListingSyncWatermarks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ListingSyncWatermarks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                LastUpdateTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastFullScanAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ListingSyncWatermarks", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ListingSyncWatermarks_OwnerId_Platform_Status",
            table: "ListingSyncWatermarks",
            columns: new[] { "OwnerId", "Platform", "Status" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ListingSyncWatermarks");
    }
}
