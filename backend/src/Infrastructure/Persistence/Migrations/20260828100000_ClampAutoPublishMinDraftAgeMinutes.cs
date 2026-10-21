using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;
using Shared.Constants;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>Enforce minimum 5-minute draft wait before auto publish pick.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260828100000_ClampAutoPublishMinDraftAgeMinutes")]
public class ClampAutoPublishMinDraftAgeMinutes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var minMinutes = ListingAutoPublishSettingKeys.MinDraftAgeMinutes;

        migrationBuilder.Sql($"""
            UPDATE "Shops"
            SET "AutoPublishMinDraftAgeMinutes" = {minMinutes}
            WHERE "AutoPublishMinDraftAgeMinutes" IS NOT NULL
              AND "AutoPublishMinDraftAgeMinutes" < {minMinutes};
            """);

        migrationBuilder.Sql($"""
            UPDATE "Settings"
            SET "Value" = '{minMinutes}', "UpdatedAt" = NOW()
            WHERE "Key" = '{ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutes}'
              AND (
                "Value" IS NULL
                OR TRIM("Value") = ''
                OR (
                  "Value" ~ '^[0-9]+$'
                  AND CAST("Value" AS INTEGER) < {minMinutes}
                )
              );
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data clamp is not reversed — previous values below 5 are not recoverable.
    }
}
