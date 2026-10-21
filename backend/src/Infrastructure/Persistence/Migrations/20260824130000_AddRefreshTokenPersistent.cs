using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Thêm cờ phiên dài hạn (stay signed in) cho refresh token.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260824130000_AddRefreshTokenPersistent")]
public class AddRefreshTokenPersistent : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "Persistent",
            table: "RefreshTokens",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Persistent",
            table: "RefreshTokens");
    }
}
