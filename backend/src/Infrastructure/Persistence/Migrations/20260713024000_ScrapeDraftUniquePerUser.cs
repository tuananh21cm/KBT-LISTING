using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260713024000_ScrapeDraftUniquePerUser")]
public class ScrapeDraftUniquePerUser : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ScrapeDrafts_FourSellerMoveDraftId",
            table: "ScrapeDrafts");

        migrationBuilder.CreateIndex(
            name: "IX_ScrapeDrafts_CreatedById_FourSellerMoveDraftId",
            table: "ScrapeDrafts",
            columns: new[] { "CreatedById", "FourSellerMoveDraftId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ScrapeDrafts_CreatedById_FourSellerMoveDraftId",
            table: "ScrapeDrafts");

        migrationBuilder.CreateIndex(
            name: "IX_ScrapeDrafts_FourSellerMoveDraftId",
            table: "ScrapeDrafts",
            column: "FourSellerMoveDraftId",
            unique: true);
    }
}
