using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260719120000_FulfilAccountUniqueUsernameEmail")]
public class FulfilAccountUniqueUsernameEmail : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Clear duplicate username/email before unique indexes (keep earliest CreatedAt).
        migrationBuilder.Sql(
            """
            WITH ranked AS (
              SELECT "Id",
                     ROW_NUMBER() OVER (
                       PARTITION BY LOWER(BTRIM("Username"))
                       ORDER BY "CreatedAt", "Id"
                     ) AS rn
              FROM "FulfilAccounts"
              WHERE "Username" IS NOT NULL AND BTRIM("Username") <> ''
            )
            UPDATE "FulfilAccounts" a
            SET "Username" = NULL,
                "UpdatedAt" = NOW() AT TIME ZONE 'utc'
            FROM ranked r
            WHERE a."Id" = r."Id" AND r.rn > 1;

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

        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS "IX_FulfilAccounts_Username";
            DROP INDEX IF EXISTS "IX_FulfilAccounts_Email";
            """);

        migrationBuilder.CreateIndex(
            name: "IX_FulfilAccounts_Username",
            table: "FulfilAccounts",
            column: "Username",
            unique: true,
            filter: "\"Username\" IS NOT NULL AND \"Username\" <> ''");

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
            name: "IX_FulfilAccounts_Username",
            table: "FulfilAccounts");

        migrationBuilder.DropIndex(
            name: "IX_FulfilAccounts_Email",
            table: "FulfilAccounts");
    }
}
