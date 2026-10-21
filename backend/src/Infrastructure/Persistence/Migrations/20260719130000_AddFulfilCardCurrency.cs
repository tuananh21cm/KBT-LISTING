using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260719130000_AddFulfilCardCurrency")]
public class AddFulfilCardCurrency : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Currency",
            table: "FulfilCards",
            type: "character varying(10)",
            maxLength: 10,
            nullable: false,
            defaultValue: "USD");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilCards_Currency",
            table: "FulfilCards",
            column: "Currency");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_FulfilCards_Currency",
            table: "FulfilCards");

        migrationBuilder.DropColumn(
            name: "Currency",
            table: "FulfilCards");
    }
}
