using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>Persist auto publish step checkpoints on listings for resume on retry.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260827100000_AddListingAutoPublishStepCheckpoints")]
public class AddListingAutoPublishStepCheckpoints : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AutoPublishCompletedStepsJson",
            table: "Listings",
            type: "jsonb",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AutoPublishCompletedStepsJson",
            table: "Listings");
    }
}
