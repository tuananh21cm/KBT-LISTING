using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260710045000_AddVariationSpecTemplates")]
public class AddVariationSpecTemplates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "VariationSpecTemplates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SpecType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VariationSpecTemplates", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_VariationSpecTemplates_SpecType_SortOrder",
            table: "VariationSpecTemplates",
            columns: new[] { "SpecType", "SortOrder" });

        migrationBuilder.CreateIndex(
            name: "IX_VariationSpecTemplates_SpecType_Value",
            table: "VariationSpecTemplates",
            columns: new[] { "SpecType", "Value" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "VariationSpecTemplates");
    }
}
