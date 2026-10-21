using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260714173000_AddPricingTemplates")]
public class AddPricingTemplates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PricingTemplates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                SupplierShipping = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                Tax = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                DiscountPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                FastSalePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                AdsFee = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                TikTokShipping = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                PlatformFeePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                TargetMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                MarginPercent = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                ProfitAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PricingTemplates", x => x.Id);
                table.ForeignKey(
                    name: "FK_PricingTemplates_AspNetUsers_CreatedById",
                    column: x => x.CreatedById,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PricingTemplates_CreatedById",
            table: "PricingTemplates",
            column: "CreatedById");

        migrationBuilder.CreateIndex(
            name: "IX_PricingTemplates_CreatedById_Name",
            table: "PricingTemplates",
            columns: new[] { "CreatedById", "Name" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PricingTemplates");
    }
}
