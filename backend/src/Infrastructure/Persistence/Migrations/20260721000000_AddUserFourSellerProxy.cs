using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260721000000_AddUserFourSellerProxy")]
public class AddUserFourSellerProxy : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FourSellerProxy",
            table: "AspNetUsers",
            type: "character varying(300)",
            maxLength: 300,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "FourSellerProxy",
            table: "AspNetUsers");
    }
}
