using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Company operating income/expense ledger, separate from shop payout and fulfil cost.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260919170000_AddCompanyLedgerEntries")]
public class AddCompanyLedgerEntries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CompanyLedgerEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CategoryCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: true),
                OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CompanyLedgerEntries", x => x.Id);
                table.ForeignKey(
                    name: "FK_CompanyLedgerEntries_AspNetUsers_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CompanyLedgerEntries_AspNetUsers_OwnerUserId",
                    column: x => x.OwnerUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_CompanyLedgerEntries_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CompanyLedgerEntries_CategoryCode",
            table: "CompanyLedgerEntries",
            column: "CategoryCode");

        migrationBuilder.CreateIndex(
            name: "IX_CompanyLedgerEntries_CreatedByUserId",
            table: "CompanyLedgerEntries",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_CompanyLedgerEntries_Direction",
            table: "CompanyLedgerEntries",
            column: "Direction");

        migrationBuilder.CreateIndex(
            name: "IX_CompanyLedgerEntries_OccurredAt",
            table: "CompanyLedgerEntries",
            column: "OccurredAt");

        migrationBuilder.CreateIndex(
            name: "IX_CompanyLedgerEntries_OwnerUserId",
            table: "CompanyLedgerEntries",
            column: "OwnerUserId");

        migrationBuilder.CreateIndex(
            name: "IX_CompanyLedgerEntries_ShopId",
            table: "CompanyLedgerEntries",
            column: "ShopId");

        migrationBuilder.CreateIndex(
            name: "IX_CompanyLedgerEntries_ShopId_ShopAcquisition",
            table: "CompanyLedgerEntries",
            column: "ShopId",
            unique: true,
            filter: "\"ShopId\" IS NOT NULL AND \"Source\" = 'ShopAcquisition'");

        migrationBuilder.CreateIndex(
            name: "IX_CompanyLedgerEntries_Source",
            table: "CompanyLedgerEntries",
            column: "Source");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CompanyLedgerEntries");
    }
}
