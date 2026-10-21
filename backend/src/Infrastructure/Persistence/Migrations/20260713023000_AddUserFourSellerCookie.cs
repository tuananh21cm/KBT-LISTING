using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260713023000_AddUserFourSellerCookie")]
public class AddUserFourSellerCookie : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FourSellerCookie",
            table: "AspNetUsers",
            type: "text",
            nullable: true);

        migrationBuilder.Sql(
            """DELETE FROM "Settings" WHERE "Key" = '4seller.cookie';""");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "FourSellerCookie",
            table: "AspNetUsers");

        migrationBuilder.Sql(
            """
            INSERT INTO "Settings" ("Id", "Group", "Key", "Label", "Value", "Type", "Description", "CreatedAt")
            SELECT gen_random_uuid(), '4seller Config', '4seller.cookie', 'Session Cookie', '', 'password',
                   '4Seller HTTP session cookie cho API calls', NOW()
            WHERE NOT EXISTS (SELECT 1 FROM "Settings" WHERE "Key" = '4seller.cookie');
            """);
    }
}
