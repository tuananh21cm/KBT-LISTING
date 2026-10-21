using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260719140000_FulfilAccountDropUniqueEmail")]
public class FulfilAccountDropUniqueEmail : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_FulfilAccounts_Email";""");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilAccounts_Email",
            table: "FulfilAccounts",
            column: "Email");
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
            column: "Email",
            unique: true,
            filter: "\"Email\" IS NOT NULL AND \"Email\" <> ''");
    }
}
