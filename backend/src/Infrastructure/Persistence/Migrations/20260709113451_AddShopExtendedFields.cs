using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShopExtendedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DayLive",
                table: "Shops",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Deli",
                table: "Shops",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Shops",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LlcName",
                table: "Shops",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewBankNumber",
                table: "Shops",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewPhoneNumber",
                table: "Shops",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OldBankNumber",
                table: "Shops",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Proxy",
                table: "Shops",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TiktokPassword",
                table: "Shops",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorAuth",
                table: "Shops",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DayLive",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "Deli",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "LlcName",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "NewBankNumber",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "NewPhoneNumber",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "OldBankNumber",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "Proxy",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "TiktokPassword",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "TwoFactorAuth",
                table: "Shops");
        }
    }
}
