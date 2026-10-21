using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260715150000_AddUserFourSellerCookieLoginStatus")]
public class AddUserFourSellerCookieLoginStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FourSellerCookieLoginStatus",
            table: "AspNetUsers",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "none");

        migrationBuilder.Sql(
            """
            UPDATE "AspNetUsers"
            SET "FourSellerCookieLoginStatus" = 'unchecked'
            WHERE "FourSellerCookie" IS NOT NULL
              AND TRIM("FourSellerCookie") <> '';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "FourSellerCookieLoginStatus",
            table: "AspNetUsers");
    }
}
