using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Display labels from TikTok lead_demands[].lead_tag_text on Product opportunities.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260814150000_AddProductOpportunityLeadTagTexts")]
public class AddProductOpportunityLeadTagTexts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LeadTagTexts",
            table: "ProductOpportunities",
            type: "jsonb",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "LeadTagTexts", table: "ProductOpportunities");
    }
}
