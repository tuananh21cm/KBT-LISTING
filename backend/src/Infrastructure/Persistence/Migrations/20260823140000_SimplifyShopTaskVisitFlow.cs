using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Bỏ ba cột không còn nguồn dữ liệu sau khi nhiệm vụ truy cập URL chuyển sang do người dùng tự đi.
///
/// <para>Ban đầu extension tự mở tab nền và chờ trang load, nên cần <c>TimeoutSeconds</c> (hạn chờ),
/// <c>DurationMs</c> (đo được thời gian load) và <c>RunId</c> (visit thuộc về một lượt Sync). Giờ
/// người dùng tự tới trang — bấm nhiệm vụ trong extension hoặc đi bằng menu TikTok — nên không còn
/// ai chờ, không đo được thời lượng đồng nhất cho cả hai cách, và visit không thuộc lượt Sync nào.
/// Giữ lại chỉ tạo ra cột luôn null và một ô cấu hình admin chỉnh mà không có tác dụng.</para>
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260823140000_SimplifyShopTaskVisitFlow")]
public class SimplifyShopTaskVisitFlow : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TimeoutSeconds",
            table: "ShopTaskDefinitions");

        migrationBuilder.DropColumn(
            name: "DurationMs",
            table: "ShopTaskCompletions");

        migrationBuilder.DropColumn(
            name: "RunId",
            table: "ShopTaskCompletions");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "TimeoutSeconds",
            table: "ShopTaskDefinitions",
            type: "integer",
            nullable: false,
            defaultValue: 60);

        migrationBuilder.AddColumn<int>(
            name: "DurationMs",
            table: "ShopTaskCompletions",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RunId",
            table: "ShopTaskCompletions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
    }
}
