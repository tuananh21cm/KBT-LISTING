using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260719150000_FulfilAccountRestoreUniqueEmail")]
public class FulfilAccountRestoreUniqueEmail : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Keep earliest account email; clear duplicates so unique index can apply.
        migrationBuilder.Sql(
            """
            WITH ranked AS (
              SELECT "Id",
                     ROW_NUMBER() OVER (
                       PARTITION BY LOWER(BTRIM("Email"))
                       ORDER BY "CreatedAt", "Id"
                     ) AS rn
              FROM "FulfilAccounts"
              WHERE "Email" IS NOT NULL AND BTRIM("Email") <> ''
            )
            UPDATE "FulfilAccounts" a
            SET "Email" = NULL,
                "UpdatedAt" = NOW() AT TIME ZONE 'utc'
            FROM ranked r
            WHERE a."Id" = r."Id" AND r.rn > 1;
            """);

        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_FulfilAccounts_Email";""");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilAccounts_Email",
            table: "FulfilAccounts",
            column: "Email",
            unique: true,
            filter: "\"Email\" IS NOT NULL AND \"Email\" <> ''");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_FulfilAccounts_Email",
            table: "FulfilAccounts");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilAccounts_Email",
            table: "FulfilAccounts",
            column: "Email");
    }
}
