using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// TrueSpend = thẻ + ví + club (Membership). Club cộng vào vì giảm membership
/// chỉ có nhờ đã mua SHEIN Club — backfill Orders.ChargedTrueSpend.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260807190000_TrueSpendIncludesClub")]
public class TrueSpendIncludesClub : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE "Orders"
            SET "ChargedTrueSpend" =
                CASE
                    WHEN "ChargedSupplierTotal" IS NULL
                     AND "ChargedSupplierWalletPrice" IS NULL
                     AND "ChargedSupplierMembershipDiscount" IS NULL
                    THEN NULL
                    ELSE COALESCE("ChargedSupplierTotal", 0)
                       + COALESCE("ChargedSupplierWalletPrice", 0)
                       + COALESCE("ChargedSupplierMembershipDiscount", 0)
                END
            WHERE "ChargedSupplierTotal" IS NOT NULL
               OR "ChargedSupplierWalletPrice" IS NOT NULL
               OR "ChargedSupplierMembershipDiscount" IS NOT NULL
               OR "ChargedTrueSpend" IS NOT NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Khôi phục công thức cũ: thẻ + ví (không cộng club).
        migrationBuilder.Sql(
            """
            UPDATE "Orders"
            SET "ChargedTrueSpend" =
                CASE
                    WHEN "ChargedSupplierTotal" IS NULL
                     AND "ChargedSupplierWalletPrice" IS NULL
                    THEN NULL
                    ELSE COALESCE("ChargedSupplierTotal", 0)
                       + COALESCE("ChargedSupplierWalletPrice", 0)
                END
            WHERE "ChargedSupplierTotal" IS NOT NULL
               OR "ChargedSupplierWalletPrice" IS NOT NULL
               OR "ChargedSupplierMembershipDiscount" IS NOT NULL
               OR "ChargedTrueSpend" IS NOT NULL;
            """);
    }
}
