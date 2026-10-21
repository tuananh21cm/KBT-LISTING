using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Thêm thời gian xem trang bắt buộc sau khi load xong cho nhiệm vụ truy cập URL.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260824120000_AddShopTaskViewDurationSeconds")]
public class AddShopTaskViewDurationSeconds : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ViewDurationSeconds",
            table: "ShopTaskDefinitions",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ViewDurationSeconds",
            table: "ShopTaskDefinitions");
    }
}
