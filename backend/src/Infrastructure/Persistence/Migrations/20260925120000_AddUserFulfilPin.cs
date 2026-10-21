using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260925120000_AddUserFulfilPin")]
public class AddUserFulfilPin : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FulfilPinHash",
            table: "AspNetUsers",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_AspNetUsers_FulfilPinHash",
            table: "AspNetUsers",
            column: "FulfilPinHash",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_AspNetUsers_FulfilPinHash",
            table: "AspNetUsers");

        migrationBuilder.DropColumn(
            name: "FulfilPinHash",
            table: "AspNetUsers");
    }
}
