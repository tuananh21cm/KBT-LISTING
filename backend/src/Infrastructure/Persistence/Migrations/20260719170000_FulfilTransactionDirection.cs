using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260719170000_FulfilTransactionDirection")]
public class FulfilTransactionDirection : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Direction",
            table: "FulfilTransactions",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Debit");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilTransactions_Direction",
            table: "FulfilTransactions",
            column: "Direction");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_FulfilTransactions_Direction",
            table: "FulfilTransactions");

        migrationBuilder.DropColumn(
            name: "Direction",
            table: "FulfilTransactions");
    }
}
