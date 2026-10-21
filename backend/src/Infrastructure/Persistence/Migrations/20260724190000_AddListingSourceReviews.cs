using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Shein (source) review metrics on Listings + SourceReviewSnapshots staging table.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260724190000_AddListingSourceReviews")]
public class AddListingSourceReviews : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ReviewCount",
            table: "Listings",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "AverageRating",
            table: "Listings",
            type: "numeric(3,2)",
            precision: 3,
            scale: 2,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "SourceReviewSnapshots",
            columns: table => new
            {
                SourceUrlKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                SourceUrl = table.Column<string>(type: "text", nullable: true),
                ReviewCount = table.Column<int>(type: "integer", nullable: true),
                AverageRating = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SourceReviewSnapshots", x => x.SourceUrlKey);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SourceReviewSnapshots");
        migrationBuilder.DropColumn(name: "ReviewCount", table: "Listings");
        migrationBuilder.DropColumn(name: "AverageRating", table: "Listings");
    }
}
