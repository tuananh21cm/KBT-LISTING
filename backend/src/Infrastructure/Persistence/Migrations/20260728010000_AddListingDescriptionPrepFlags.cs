using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Local-only Description prep checklist flags for Publish gate (Rewrite + Insert Images).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260728010000_AddListingDescriptionPrepFlags")]
public class AddListingDescriptionPrepFlags : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "DescriptionRewritten",
            table: "Listings",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "DescriptionImagesInserted",
            table: "Listings",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DescriptionRewritten",
            table: "Listings");

        migrationBuilder.DropColumn(
            name: "DescriptionImagesInserted",
            table: "Listings");
    }
}
