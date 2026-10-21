using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>Per-shop minimum draft age before auto publish pick.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260826163000_AddAutoPublishMinDraftAgeMinutes")]
public class AddAutoPublishMinDraftAgeMinutes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AutoPublishMinDraftAgeMinutes",
            table: "Shops",
            type: "integer",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AutoPublishMinDraftAgeMinutes", table: "Shops");
    }
}
