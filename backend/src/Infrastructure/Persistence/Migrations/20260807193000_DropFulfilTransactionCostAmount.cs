using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>Bỏ snapshot «Trước giảm» trên sổ cái — không còn dùng.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260807193000_DropFulfilTransactionCostAmount")]
public class DropFulfilTransactionCostAmount : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CostAmount",
            table: "FulfilTransactions");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "CostAmount",
            table: "FulfilTransactions",
            type: "numeric(18,4)",
            nullable: true);
    }
}
