using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260710113000_AddProductCertificationFields")]
public class AddProductCertificationFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "HazardousMaterials",
            table: "Products",
            type: "character varying(10)",
            maxLength: 10,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Prop65ReproChems",
            table: "Products",
            type: "character varying(10)",
            maxLength: 10,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Prop65Carcinogens",
            table: "Products",
            type: "character varying(10)",
            maxLength: 10,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "HazardousMaterials", table: "Products");
        migrationBuilder.DropColumn(name: "Prop65ReproChems", table: "Products");
        migrationBuilder.DropColumn(name: "Prop65Carcinogens", table: "Products");
    }
}
