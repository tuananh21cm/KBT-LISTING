using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShopProductSellerSetFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppliedRules",
                table: "ShopProducts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FourSellerGroupId",
                table: "ShopProducts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FourSellerGroupName",
                table: "ShopProducts",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ListingSourceUrl",
                table: "ShopProducts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppliedRules",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "FourSellerGroupId",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "FourSellerGroupName",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "ListingSourceUrl",
                table: "ShopProducts");
        }
    }
}
