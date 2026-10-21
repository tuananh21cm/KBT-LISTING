using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Persist 4Seller searchTerms / productHighlights on Listings (fill-once from RawPayload).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260825033000_AddListingSearchTermsAndHighlights")]
public class AddListingSearchTermsAndHighlights : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SearchTermsJson",
            table: "Listings",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ProductHighlightsJson",
            table: "Listings",
            type: "jsonb",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "Listings"
            SET "SearchTermsJson" = "RawPayload" -> 'searchTerms'
            WHERE "SearchTermsJson" IS NULL
              AND "RawPayload" IS NOT NULL
              AND jsonb_typeof("RawPayload" -> 'searchTerms') = 'array';

            UPDATE "Listings"
            SET "ProductHighlightsJson" = "RawPayload" -> 'productHighlights'
            WHERE "ProductHighlightsJson" IS NULL
              AND "RawPayload" IS NOT NULL
              AND jsonb_typeof("RawPayload" -> 'productHighlights') = 'array';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SearchTermsJson",
            table: "Listings");

        migrationBuilder.DropColumn(
            name: "ProductHighlightsJson",
            table: "Listings");
    }
}
