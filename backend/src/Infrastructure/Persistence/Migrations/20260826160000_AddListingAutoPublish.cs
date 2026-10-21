using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>Auto publish runs, listing publish source, shop auto publish settings.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260826160000_AddListingAutoPublish")]
public class AddListingAutoPublish : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "AutoPublishEnabled",
            table: "Shops",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "AutoPublishIntervalSeconds",
            table: "Shops",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "LastAutoPublishAt",
            table: "Shops",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "AutoPublishSkipped",
            table: "Listings",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "AutoPublishSkippedAt",
            table: "Listings",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AutoPublishSkipReason",
            table: "Listings",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "LastAutoPublishRunId",
            table: "Listings",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PublishSource",
            table: "Listings",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "ListingAutoPublishRuns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DurationMs = table.Column<int>(type: "integer", nullable: false),
                StepsJson = table.Column<string>(type: "jsonb", nullable: true),
                FailedStepCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ListingAutoPublishRuns", x => x.Id);
                table.ForeignKey(
                    name: "FK_ListingAutoPublishRuns_Listings_ListingId",
                    column: x => x.ListingId,
                    principalTable: "Listings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ListingAutoPublishRuns_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Listings_AutoPublishSkipped",
            table: "Listings",
            column: "AutoPublishSkipped");

        migrationBuilder.CreateIndex(
            name: "IX_Listings_ShopId_PageType_AutoPublishSkipped_CreatedAt",
            table: "Listings",
            columns: new[] { "ShopId", "PageType", "AutoPublishSkipped", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_ListingAutoPublishRuns_ListingId_StartedAt",
            table: "ListingAutoPublishRuns",
            columns: new[] { "ListingId", "StartedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_ListingAutoPublishRuns_ShopId_StartedAt",
            table: "ListingAutoPublishRuns",
            columns: new[] { "ShopId", "StartedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ListingAutoPublishRuns");

        migrationBuilder.DropIndex(
            name: "IX_Listings_AutoPublishSkipped",
            table: "Listings");

        migrationBuilder.DropIndex(
            name: "IX_Listings_ShopId_PageType_AutoPublishSkipped_CreatedAt",
            table: "Listings");

        migrationBuilder.DropColumn(name: "AutoPublishEnabled", table: "Shops");
        migrationBuilder.DropColumn(name: "AutoPublishIntervalSeconds", table: "Shops");
        migrationBuilder.DropColumn(name: "LastAutoPublishAt", table: "Shops");
        migrationBuilder.DropColumn(name: "AutoPublishSkipped", table: "Listings");
        migrationBuilder.DropColumn(name: "AutoPublishSkippedAt", table: "Listings");
        migrationBuilder.DropColumn(name: "AutoPublishSkipReason", table: "Listings");
        migrationBuilder.DropColumn(name: "LastAutoPublishRunId", table: "Listings");
        migrationBuilder.DropColumn(name: "PublishSource", table: "Listings");
    }
}
