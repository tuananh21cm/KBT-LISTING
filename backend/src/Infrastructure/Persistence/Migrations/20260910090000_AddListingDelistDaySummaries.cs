using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Precomputed delist counters per shop / Vietnam snapshot day for the optimize board chip.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260910090000_AddListingDelistDaySummaries")]
public class AddListingDelistDaySummaries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ListingDelistDaySummaries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                Scanned = table.Column<int>(type: "integer", nullable: false),
                DelistCount = table.Column<int>(type: "integer", nullable: false),
                ReviewCount = table.Column<int>(type: "integer", nullable: false),
                SkippedWinners = table.Column<int>(type: "integer", nullable: false),
                SkippedTooNew = table.Column<int>(type: "integer", nullable: false),
                SkippedHealthy = table.Column<int>(type: "integer", nullable: false),
                RecommendedDelistCount = table.Column<int>(type: "integer", nullable: false),
                PublishLimitTotal = table.Column<int>(type: "integer", nullable: true),
                PublishLimitPublished = table.Column<int>(type: "integer", nullable: true),
                ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ListingDelistDaySummaries", x => x.Id);
                table.ForeignKey(
                    name: "FK_ListingDelistDaySummaries_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ListingDelistDaySummaries_ShopId_SnapshotDate",
            table: "ListingDelistDaySummaries",
            columns: ["ShopId", "SnapshotDate"],
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ListingDelistDaySummaries");
    }
}
