using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Repairs <c>IX_ShopOperationalPeriods_ShopId_Open</c>.
///
/// The database ended up with the filter <c>"EndedOn" IS NOT NULL</c>, the inverse of the model
/// (<see cref="Configurations.ShopOperationalPeriodConfiguration"/>). Instead of "one open period per
/// shop" it enforced "one closed period per shop", so closing the second period of a shop — e.g. the
/// extension syncing a seller-us account TikTok reports as deactivated — failed with 23505 and the
/// whole request rolled back.
///
/// Any shop left with more than one open row is collapsed to the newest one first, otherwise the
/// corrected index cannot be created.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260821040000_FixShopOperationalPeriodOpenIndex")]
public class FixShopOperationalPeriodOpenIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            WITH ranked AS (
                SELECT
                    "Id",
                    ROW_NUMBER() OVER (
                        PARTITION BY "ShopId"
                        ORDER BY "StartedOn" DESC, "CreatedAt" DESC, "Id" DESC
                    ) AS rn
                FROM "ShopOperationalPeriods"
                WHERE "EndedOn" IS NULL
            )
            UPDATE "ShopOperationalPeriods" p
            SET "EndedOn" = GREATEST(p."StartedOn", (NOW() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date),
                "CloseSource" = 'Backfill',
                "UpdatedAt" = NOW() AT TIME ZONE 'UTC'
            FROM ranked r
            WHERE r."Id" = p."Id"
              AND r.rn > 1;
            """);

        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_ShopOperationalPeriods_ShopId_Open";""");

        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "IX_ShopOperationalPeriods_ShopId_Open"
              ON "ShopOperationalPeriods" ("ShopId")
              WHERE "EndedOn" IS NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to roll back: the previous database state contradicted the model.
    }
}
