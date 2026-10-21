using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Multi-image / multi-category jsonb plus Order/GMV metrics on Product opportunities.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260814140000_AddProductOpportunityMetricsAndArrays")]
public class AddProductOpportunityMetricsAndArrays : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PicUrls",
            table: "ProductOpportunities",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Categories",
            table: "ProductOpportunities",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "OrderCount",
            table: "ProductOpportunities",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OrderRaw",
            table: "ProductOpportunities",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OrderRingRatio",
            table: "ProductOpportunities",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "Gmv",
            table: "ProductOpportunities",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GmvRaw",
            table: "ProductOpportunities",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GmvRingRatio",
            table: "ProductOpportunities",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PicUrls", table: "ProductOpportunities");
        migrationBuilder.DropColumn(name: "Categories", table: "ProductOpportunities");
        migrationBuilder.DropColumn(name: "OrderCount", table: "ProductOpportunities");
        migrationBuilder.DropColumn(name: "OrderRaw", table: "ProductOpportunities");
        migrationBuilder.DropColumn(name: "OrderRingRatio", table: "ProductOpportunities");
        migrationBuilder.DropColumn(name: "Gmv", table: "ProductOpportunities");
        migrationBuilder.DropColumn(name: "GmvRaw", table: "ProductOpportunities");
        migrationBuilder.DropColumn(name: "GmvRingRatio", table: "ProductOpportunities");
    }
}
