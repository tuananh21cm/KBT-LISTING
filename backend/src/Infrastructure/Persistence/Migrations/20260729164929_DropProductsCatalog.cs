using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropProductsCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ListingAttributes");
            migrationBuilder.DropTable(name: "ListingCertifications");
            migrationBuilder.DropTable(name: "ListingImages");
            migrationBuilder.DropTable(name: "ListingSkus");
            migrationBuilder.DropTable(name: "ShopProducts");
            migrationBuilder.DropTable(name: "ProductImages");
            migrationBuilder.DropTable(name: "ProductVariants");
            migrationBuilder.DropTable(name: "Products");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Catalog tables were removed permanently; restore from older migrations if needed.
            throw new System.NotSupportedException(
                "Down migration for DropProductsCatalog is not supported. Re-apply prior catalog migrations instead.");
        }
    }
}
