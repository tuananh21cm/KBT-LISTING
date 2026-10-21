using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// PrintPackShip USPS scan batches + order stamps (UspsScanBatchId / UspsScanSentAt).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260928120000_AddUspsScanBatches")]
public class AddUspsScanBatches : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "UspsScanBatchId",
            table: "Orders",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "UspsScanSentAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Orders_UspsScanBatchId",
            table: "Orders",
            column: "UspsScanBatchId",
            filter: "\"UspsScanBatchId\" IS NOT NULL");

        migrationBuilder.CreateTable(
            name: "UspsScanBatches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ExternalBatchId = table.Column<long>(type: "bigint", nullable: false),
                Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                TrackingCount = table.Column<int>(type: "integer", nullable: false),
                PricePerLabel = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                ScanFee = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                PaidPages = table.Column<int>(type: "integer", nullable: true),
                PagesTotal = table.Column<int>(type: "integer", nullable: true),
                PagesSuccess = table.Column<int>(type: "integer", nullable: true),
                PagesFailed = table.Column<int>(type: "integer", nullable: true),
                BalancePrevious = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                BalanceCurrent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                RawCreateResponse = table.Column<string>(type: "text", nullable: true),
                RawSendResponse = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UspsScanBatches", x => x.Id);
                table.ForeignKey(
                    name: "FK_UspsScanBatches_AspNetUsers_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_UspsScanBatches_ExternalBatchId",
            table: "UspsScanBatches",
            column: "ExternalBatchId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_UspsScanBatches_Status",
            table: "UspsScanBatches",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_UspsScanBatches_CreatedAt",
            table: "UspsScanBatches",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_UspsScanBatches_CreatedByUserId",
            table: "UspsScanBatches",
            column: "CreatedByUserId");

        migrationBuilder.CreateTable(
            name: "UspsScanBatchItems",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                TrackingNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UspsScanBatchItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_UspsScanBatchItems_UspsScanBatches_BatchId",
                    column: x => x.BatchId,
                    principalTable: "UspsScanBatches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_UspsScanBatchItems_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_UspsScanBatchItems_TrackingNo",
            table: "UspsScanBatchItems",
            column: "TrackingNo");

        migrationBuilder.CreateIndex(
            name: "IX_UspsScanBatchItems_BatchId_OrderId",
            table: "UspsScanBatchItems",
            columns: new[] { "BatchId", "OrderId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_UspsScanBatchItems_OrderId",
            table: "UspsScanBatchItems",
            column: "OrderId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "UspsScanBatchItems");
        migrationBuilder.DropTable(name: "UspsScanBatches");

        migrationBuilder.DropIndex(
            name: "IX_Orders_UspsScanBatchId",
            table: "Orders");

        migrationBuilder.DropColumn(name: "UspsScanBatchId", table: "Orders");
        migrationBuilder.DropColumn(name: "UspsScanSentAt", table: "Orders");
    }
}
