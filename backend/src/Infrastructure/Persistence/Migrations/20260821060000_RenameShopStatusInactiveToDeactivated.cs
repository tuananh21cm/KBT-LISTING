using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Renames the ops shop status value <c>Inactive</c> to <c>Deactivated</c>.
///
/// The UI now labels the three shop states Active / Deactivated / Suspended, matching TikTok Seller
/// Center wording, and the stored value follows so API payloads, filters and labels all read the same.
/// <c>ShopOperationalPeriods.CloseSource</c> carries the same tag and is renamed with it.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260821060000_RenameShopStatusInactiveToDeactivated")]
public class RenameShopStatusInactiveToDeactivated : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "Shops"
            SET "Status" = 'Deactivated'
            WHERE "Status" = 'Inactive';
            """);

        migrationBuilder.Sql("""
            UPDATE "ShopOperationalPeriods"
            SET "CloseSource" = 'Deactivated'
            WHERE "CloseSource" = 'Inactive';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "ShopOperationalPeriods"
            SET "CloseSource" = 'Inactive'
            WHERE "CloseSource" = 'Deactivated';
            """);

        migrationBuilder.Sql("""
            UPDATE "Shops"
            SET "Status" = 'Inactive'
            WHERE "Status" = 'Deactivated';
            """);
    }
}
