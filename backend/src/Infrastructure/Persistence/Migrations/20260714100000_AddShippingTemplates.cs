using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260714100000_AddShippingTemplates")]
public class AddShippingTemplates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ShippingTemplates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                PackageWeight = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                PackageLength = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                PackageWidth = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                PackageHeight = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                WeightUnit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                DimensionUnit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShippingTemplates", x => x.Id);
                table.ForeignKey(
                    name: "FK_ShippingTemplates_AspNetUsers_CreatedById",
                    column: x => x.CreatedById,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ShippingTemplates_CreatedById",
            table: "ShippingTemplates",
            column: "CreatedById");

        migrationBuilder.CreateIndex(
            name: "IX_ShippingTemplates_CreatedById_Name",
            table: "ShippingTemplates",
            columns: new[] { "CreatedById", "Name" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ShippingTemplates");
    }
}
