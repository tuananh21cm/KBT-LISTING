using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceOrderSupplierLinksWithSupplierPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SaveFailed",
                table: "SheinSyncRunLogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SupplierPurchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierOrderNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MatchState = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SkipReason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SkipEvidenceJson = table.Column<string>(type: "text", nullable: true),
                    IsCharged = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RelationBillno = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConfluenceBatchNo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SupplierOrderUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TrackingNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Carrier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TrackingKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SupplierRetailPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierSubtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierShippingFee = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierOnTimeGuarantee = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierCouponPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierMembershipDiscount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierPointsDiscount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierTotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    OrderedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupplierPaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupplierUnpaid = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsMembershipOrVirtual = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SupplierOrderType = table.Column<int>(type: "integer", nullable: true),
                    FulfilAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    FulfilCardId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaidByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductIdsJson = table.Column<string>(type: "text", nullable: true),
                    SupplierStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SupplierStatusRaw = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SupplierStatusAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceiverName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReceiverPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ReceiverPostalCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    PhoneKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ZipKey = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    NameKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastRunId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MatchedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AutoMarkShipTrackingNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AutoMarkShipStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AutoMarkShipAttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AutoMarkShipError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RawPayload = table.Column<string>(type: "text", nullable: true),
                    RawPayloadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierPurchases_AspNetUsers_PaidByUserId",
                        column: x => x.PaidByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SupplierPurchases_FulfilAccounts_FulfilAccountId",
                        column: x => x.FulfilAccountId,
                        principalTable: "FulfilAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPurchases_FulfilCards_FulfilCardId",
                        column: x => x.FulfilCardId,
                        principalTable: "FulfilCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPurchases_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPurchases_AutoMarkShipStatus",
                table: "SupplierPurchases",
                column: "AutoMarkShipStatus");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPurchases_ConfluenceBatchNo",
                table: "SupplierPurchases",
                column: "ConfluenceBatchNo");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPurchases_FulfilAccountId",
                table: "SupplierPurchases",
                column: "FulfilAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPurchases_FulfilCardId",
                table: "SupplierPurchases",
                column: "FulfilCardId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPurchases_MatchState",
                table: "SupplierPurchases",
                column: "MatchState");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPurchases_OrderId",
                table: "SupplierPurchases",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPurchases_PaidByUserId",
                table: "SupplierPurchases",
                column: "PaidByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPurchases_PhoneKey_ZipKey",
                table: "SupplierPurchases",
                columns: new[] { "PhoneKey", "ZipKey" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPurchases_RelationBillno",
                table: "SupplierPurchases",
                column: "RelationBillno");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPurchases_SupplierOrderNo",
                table: "SupplierPurchases",
                column: "SupplierOrderNo",
                unique: true,
                filter: "\"SupplierOrderNo\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPurchases_SupplierStatus",
                table: "SupplierPurchases",
                column: "SupplierStatus");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPurchases_TrackingNo",
                table: "SupplierPurchases",
                column: "TrackingNo");

            CopySupplierLinks(migrationBuilder);
            MarkChargedPurchases(migrationBuilder);
            CreateManualPurchases(migrationBuilder);

            // Đổi FK của sổ cái sau khi dữ liệu đã sang bảng mới: Id được giữ nguyên nên mọi dòng chi
            // vẫn trỏ đúng lần mua nó trả tiền cho, không cần map lại.
            migrationBuilder.DropForeignKey(
                name: "FK_FulfilTransactions_OrderSupplierLinks_OrderSupplierLinkId",
                table: "FulfilTransactions");

            migrationBuilder.DropIndex(
                name: "IX_FulfilTransactions_LinkId_CostUpdate",
                table: "FulfilTransactions");

            migrationBuilder.DropIndex(
                name: "IX_FulfilTransactions_OrderId_CostUpdate",
                table: "FulfilTransactions");

            migrationBuilder.RenameColumn(
                name: "OrderSupplierLinkId",
                table: "FulfilTransactions",
                newName: "SupplierPurchaseId");

            migrationBuilder.RenameIndex(
                name: "IX_FulfilTransactions_OrderSupplierLinkId",
                table: "FulfilTransactions",
                newName: "IX_FulfilTransactions_SupplierPurchaseId");

            AttachOrderLevelLedgerRows(migrationBuilder);

            migrationBuilder.CreateIndex(
                name: "IX_FulfilTransactions_OrderId_CostUpdate",
                table: "FulfilTransactions",
                column: "OrderId",
                unique: true,
                filter: "\"OrderId\" IS NOT NULL AND \"SupplierPurchaseId\" IS NULL AND \"Source\" = 'CostUpdate'");

            migrationBuilder.CreateIndex(
                name: "IX_FulfilTransactions_PurchaseId_CostUpdate",
                table: "FulfilTransactions",
                column: "SupplierPurchaseId",
                unique: true,
                filter: "\"SupplierPurchaseId\" IS NOT NULL AND \"Source\" = 'CostUpdate'");

            migrationBuilder.AddForeignKey(
                name: "FK_FulfilTransactions_SupplierPurchases_SupplierPurchaseId",
                table: "FulfilTransactions",
                column: "SupplierPurchaseId",
                principalTable: "SupplierPurchases",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.DropTable(
                name: "OrderSupplierLinks");

            migrationBuilder.DropIndex(
                name: "IX_Orders_SupplierOrderNo",
                table: "Orders");

            foreach (var column in OrderSupplierColumns)
            {
                migrationBuilder.DropColumn(name: column, table: "Orders");
            }
        }

        /// <summary>Mười một cột mua hàng lặp lại trên <c>Orders</c>, nay chuyển hết sang bảng mới.</summary>
        private static readonly string[] OrderSupplierColumns =
        [
            "SupplierOrderNo",
            "SupplierOrderUrl",
            "SupplierRetailPrice",
            "SupplierSubtotal",
            "SupplierShippingFee",
            "SupplierOnTimeGuarantee",
            "SupplierCouponPrice",
            "SupplierMembershipDiscount",
            "SupplierPointsDiscount",
            "SupplierTax",
            "SupplierTotal"
        ];

        /// <summary>
        /// Chuyển nguyên <c>OrderSupplierLinks</c> sang bảng mới, <b>giữ nguyên Id</b> để
        /// <c>FulfilTransactions</c> chỉ cần đổi tên cột là trỏ đúng chỗ.
        ///
        /// Khoá khớp (<c>PhoneKey</c>/<c>ZipKey</c>/<c>NameKey</c>) và thông tin người nhận để trống:
        /// bảng cũ không lưu chúng, và bịa ra từ đơn sàn thì đó là dữ liệu của đơn sàn chứ không phải
        /// của lần mua. Lượt sync kế tiếp sẽ điền từ payload SHEIN.
        /// </summary>
        private static void CopySupplierLinks(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            INSERT INTO "SupplierPurchases" (
                "Id", "OrderId", "SupplierOrderNo", "Source", "MatchState", "IsCharged",
                "RelationBillno", "ConfluenceBatchNo", "SupplierOrderUrl",
                "TrackingNo", "Carrier", "TrackingKind",
                "SupplierRetailPrice", "SupplierSubtotal", "SupplierShippingFee",
                "SupplierOnTimeGuarantee", "SupplierCouponPrice", "SupplierMembershipDiscount",
                "SupplierPointsDiscount", "SupplierTax", "SupplierTotal", "Currency",
                "OrderedAt", "SupplierPaidAt", "SupplierUnpaid", "IsMembershipOrVirtual",
                "FulfilAccountId", "FulfilCardId", "PaidByUserId", "ProductIdsJson",
                "SupplierStatus", "SupplierStatusRaw", "SupplierStatusAt",
                "FirstSeenAt", "LastSeenAt", "MatchedAt",
                "AutoMarkShipTrackingNo", "AutoMarkShipStatus", "AutoMarkShipAttemptedAt",
                "AutoMarkShipError", "RawPayload", "RawPayloadAt", "CreatedAt", "UpdatedAt")
            SELECT
                l."Id", l."OrderId", l."SupplierOrderNo", 'shein', 'matched', false,
                l."RelationBillno", l."ConfluenceBatchNo", l."SupplierOrderUrl",
                l."TrackingNo", NULL, l."TrackingKind",
                l."SupplierRetailPrice", l."SupplierSubtotal", l."SupplierShippingFee",
                l."SupplierOnTimeGuarantee", l."SupplierCouponPrice", l."SupplierMembershipDiscount",
                l."SupplierPointsDiscount", l."SupplierTax", l."SupplierTotal", 'USD',
                l."OrderedAt", l."SupplierPaidAt", l."SupplierUnpaid", false,
                l."FulfilAccountId", l."FulfilCardId", l."PaidByUserId", l."ProductIdsJson",
                l."SupplierStatus", l."SupplierStatusRaw", l."SupplierStatusAt",
                l."CreatedAt", COALESCE(l."UpdatedAt", l."CreatedAt"), l."CreatedAt",
                l."AutoMarkShipTrackingNo", l."AutoMarkShipStatus", l."AutoMarkShipAttemptedAt",
                l."AutoMarkShipError", l."RawPayload", l."RawPayloadAt", l."CreatedAt", l."UpdatedAt"
            FROM "OrderSupplierLinks" l;
            """);

        /// <summary>
        /// Đặt <c>IsCharged</c> đúng luật <c>OrderSupplierCost.SelectCharged</c>, viết lại bằng SQL:
        /// loại billno chưa thanh toán, còn billno chưa huỷ/hoàn thì chỉ tính nhóm đó, hết thì giữ
        /// đúng lần checkout gần nhất (<c>relation_billno</c> / <c>confluence_batch_no</c>).
        /// </summary>
        private static void MarkChargedPurchases(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            WITH paid_rows AS (
                SELECT
                    p."Id",
                    p."OrderId",
                    CASE WHEN lower(COALESCE(p."SupplierStatus", '')) IN ('canceled', 'cancelled', 'refunded')
                         THEN 1 ELSE 0 END AS terminal,
                    lower(COALESCE(
                        NULLIF(btrim(p."RelationBillno"), ''),
                        NULLIF(btrim(p."ConfluenceBatchNo"), ''),
                        p."SupplierOrderNo",
                        p."Id"::text)) AS grp,
                    p."CreatedAt",
                    p."Id" AS tiebreak
                FROM "SupplierPurchases" p
                WHERE p."OrderId" IS NOT NULL AND p."SupplierUnpaid" = false
            ), ranked AS (
                SELECT
                    r.*,
                    COUNT(*) OVER (PARTITION BY r."OrderId") AS real_count,
                    SUM(1 - r.terminal) OVER (PARTITION BY r."OrderId") AS payable_count,
                    FIRST_VALUE(r.grp) OVER (
                        PARTITION BY r."OrderId"
                        ORDER BY r."CreatedAt" DESC, r.tiebreak DESC
                        ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS last_grp
                FROM paid_rows r
            )
            UPDATE "SupplierPurchases" p
            SET "IsCharged" = true
            FROM ranked r
            WHERE p."Id" = r."Id"
              AND (
                  (r.payable_count > 0 AND r.terminal = 0)
                  OR (r.payable_count = 0 AND r.real_count <= 1)
                  OR (r.payable_count = 0 AND r.real_count > 1 AND r.grp = r.last_grp)
              );
            """);

        /// <summary>
        /// Sinh dòng <c>manual</c> cho những đơn mà tổng trên <c>Orders</c> khác tổng vừa tính từ
        /// billno — đó chính là phần người đi đơn gõ tay qua nút "Cập nhật phí" và là thứ duy nhất
        /// mười một cột kia còn giữ mà bảng mới chưa có.
        ///
        /// Đơn mà hai bên khớp nhau thì không sinh gì: con số đã nằm sẵn trên billno, thêm một dòng
        /// nhập tay chỉ làm nó bị tính hai lần.
        /// </summary>
        private static void CreateManualPurchases(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            WITH charged AS (
                SELECT
                    p."OrderId",
                    SUM(p."SupplierRetailPrice") AS retail,
                    SUM(p."SupplierSubtotal") AS subtotal,
                    SUM(p."SupplierShippingFee") AS ship,
                    SUM(p."SupplierOnTimeGuarantee") AS ontime,
                    SUM(p."SupplierCouponPrice") AS coupon,
                    SUM(p."SupplierMembershipDiscount") AS club,
                    SUM(p."SupplierPointsDiscount") AS points,
                    SUM(p."SupplierTax") AS tax,
                    SUM(p."SupplierTotal") AS total
                FROM "SupplierPurchases" p
                WHERE p."IsCharged" AND p."OrderId" IS NOT NULL
                GROUP BY p."OrderId"
            ), divergent AS (
                SELECT o."Id" AS order_id
                FROM "Orders" o
                LEFT JOIN charged c ON c."OrderId" = o."Id"
                WHERE (
                        o."SupplierTotal" IS NOT NULL OR o."SupplierSubtotal" IS NOT NULL
                        OR o."SupplierRetailPrice" IS NOT NULL OR o."SupplierShippingFee" IS NOT NULL
                        OR o."SupplierOnTimeGuarantee" IS NOT NULL OR o."SupplierCouponPrice" IS NOT NULL
                        OR o."SupplierMembershipDiscount" IS NOT NULL OR o."SupplierPointsDiscount" IS NOT NULL
                        OR o."SupplierTax" IS NOT NULL
                      )
                  AND (
                        o."SupplierTotal" IS DISTINCT FROM c.total
                        OR o."SupplierSubtotal" IS DISTINCT FROM c.subtotal
                        OR o."SupplierRetailPrice" IS DISTINCT FROM c.retail
                        OR o."SupplierShippingFee" IS DISTINCT FROM c.ship
                        OR o."SupplierOnTimeGuarantee" IS DISTINCT FROM c.ontime
                        OR o."SupplierCouponPrice" IS DISTINCT FROM c.coupon
                        OR o."SupplierMembershipDiscount" IS DISTINCT FROM c.club
                        OR o."SupplierPointsDiscount" IS DISTINCT FROM c.points
                        OR o."SupplierTax" IS DISTINCT FROM c.tax
                      )
            )
            INSERT INTO "SupplierPurchases" (
                "Id", "OrderId", "SupplierOrderNo", "Source", "MatchState", "IsCharged",
                "SupplierOrderUrl", "TrackingKind", "Currency",
                "SupplierRetailPrice", "SupplierSubtotal", "SupplierShippingFee",
                "SupplierOnTimeGuarantee", "SupplierCouponPrice", "SupplierMembershipDiscount",
                "SupplierPointsDiscount", "SupplierTax", "SupplierTotal",
                "SupplierUnpaid", "IsMembershipOrVirtual",
                "FulfilAccountId", "FulfilCardId", "PaidByUserId", "SupplierPaidAt",
                "FirstSeenAt", "LastSeenAt", "MatchedAt", "MatchedByUserId",
                "CreatedAt", "UpdatedAt")
            SELECT
                gen_random_uuid(), o."Id",
                -- Mã gõ tay chỉ giữ khi chưa dòng nào sở hữu: unique index toàn DB, và billno đã
                -- thuộc một dòng sync thì dòng sync mới là chủ của nó.
                CASE WHEN o."SupplierOrderNo" IS NOT NULL
                      AND NOT EXISTS (
                          SELECT 1 FROM "SupplierPurchases" x
                          WHERE lower(x."SupplierOrderNo") = lower(o."SupplierOrderNo"))
                     THEN o."SupplierOrderNo" END,
                'manual', 'matched', true,
                o."SupplierOrderUrl", 'none', 'USD',
                o."SupplierRetailPrice", o."SupplierSubtotal", o."SupplierShippingFee",
                o."SupplierOnTimeGuarantee", o."SupplierCouponPrice", o."SupplierMembershipDiscount",
                o."SupplierPointsDiscount", o."SupplierTax", o."SupplierTotal",
                false, false,
                o."FulfilAccountId", o."FulfilCardId", o."PaidByUserId", o."FulfilPurchasedAt",
                NOW(), NOW(), NOW(), o."PaidByUserId",
                NOW(), NOW()
            FROM "Orders" o
            WHERE o."Id" IN (SELECT order_id FROM divergent);

            -- Dòng nhập tay có tiền thắng dòng sync (luật SelectCharged), nên phải gỡ cờ của billno
            -- cùng đơn — bằng không tổng của đơn bị cộng hai lần.
            UPDATE "SupplierPurchases" p
            SET "IsCharged" = false
            WHERE p."Source" <> 'manual'
              AND p."OrderId" IN (
                  SELECT m."OrderId" FROM "SupplierPurchases" m
                  WHERE m."Source" = 'manual' AND m."IsCharged" AND m."OrderId" IS NOT NULL);
            """);

        /// <summary>
        /// Dòng chi cấp đơn (không gắn billno nào) nay được trỏ vào dòng <c>manual</c> vừa sinh.
        /// Sau bước này mọi dòng CostUpdate đều quy kết được về một lần mua cụ thể, đúng giả định của
        /// <c>ReconcileCostUpdateTransactionsAsync</c> bản mới.
        /// </summary>
        private static void AttachOrderLevelLedgerRows(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            UPDATE "FulfilTransactions" t
            SET "SupplierPurchaseId" = m."Id"
            FROM "SupplierPurchases" m
            WHERE t."Source" = 'CostUpdate'
              AND t."SupplierPurchaseId" IS NULL
              AND t."OrderId" IS NOT NULL
              AND m."OrderId" = t."OrderId"
              AND m."Source" = 'manual';
            """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FulfilTransactions_SupplierPurchases_SupplierPurchaseId",
                table: "FulfilTransactions");

            migrationBuilder.DropTable(
                name: "SupplierPurchases");

            migrationBuilder.DropIndex(
                name: "IX_FulfilTransactions_OrderId_CostUpdate",
                table: "FulfilTransactions");

            migrationBuilder.DropIndex(
                name: "IX_FulfilTransactions_PurchaseId_CostUpdate",
                table: "FulfilTransactions");

            migrationBuilder.DropColumn(
                name: "SaveFailed",
                table: "SheinSyncRunLogs");

            migrationBuilder.RenameColumn(
                name: "SupplierPurchaseId",
                table: "FulfilTransactions",
                newName: "OrderSupplierLinkId");

            migrationBuilder.RenameIndex(
                name: "IX_FulfilTransactions_SupplierPurchaseId",
                table: "FulfilTransactions",
                newName: "IX_FulfilTransactions_OrderSupplierLinkId");

            migrationBuilder.AddColumn<decimal>(
                name: "SupplierCouponPrice",
                table: "Orders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SupplierMembershipDiscount",
                table: "Orders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SupplierOnTimeGuarantee",
                table: "Orders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierOrderNo",
                table: "Orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierOrderUrl",
                table: "Orders",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SupplierPointsDiscount",
                table: "Orders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SupplierRetailPrice",
                table: "Orders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SupplierShippingFee",
                table: "Orders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SupplierSubtotal",
                table: "Orders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SupplierTax",
                table: "Orders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SupplierTotal",
                table: "Orders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderSupplierLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FulfilAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    FulfilCardId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaidByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AutoMarkShipAttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AutoMarkShipError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AutoMarkShipStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AutoMarkShipTrackingNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Carrier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ConfluenceBatchNo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrderedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProductIdsJson = table.Column<string>(type: "text", nullable: true),
                    RawPayload = table.Column<string>(type: "text", nullable: true),
                    RawPayloadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RelationBillno = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SupplierCouponPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierMembershipDiscount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierOnTimeGuarantee = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierOrderNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SupplierOrderUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SupplierPaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupplierPointsDiscount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierRetailPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierShippingFee = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SupplierStatusAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupplierStatusRaw = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SupplierSubtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierTotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SupplierUnpaid = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    TrackingKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TrackingNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderSupplierLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderSupplierLinks_AspNetUsers_PaidByUserId",
                        column: x => x.PaidByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrderSupplierLinks_FulfilAccounts_FulfilAccountId",
                        column: x => x.FulfilAccountId,
                        principalTable: "FulfilAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderSupplierLinks_FulfilCards_FulfilCardId",
                        column: x => x.FulfilCardId,
                        principalTable: "FulfilCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderSupplierLinks_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_SupplierOrderNo",
                table: "Orders",
                column: "SupplierOrderNo");

            migrationBuilder.CreateIndex(
                name: "IX_FulfilTransactions_LinkId_CostUpdate",
                table: "FulfilTransactions",
                column: "OrderSupplierLinkId",
                unique: true,
                filter: "\"OrderSupplierLinkId\" IS NOT NULL AND \"Source\" = 'CostUpdate'");

            migrationBuilder.CreateIndex(
                name: "IX_FulfilTransactions_OrderId_CostUpdate",
                table: "FulfilTransactions",
                column: "OrderId",
                unique: true,
                filter: "\"OrderId\" IS NOT NULL AND \"OrderSupplierLinkId\" IS NULL AND \"Source\" = 'CostUpdate'");

            migrationBuilder.CreateIndex(
                name: "IX_OrderSupplierLinks_AutoMarkShipStatus",
                table: "OrderSupplierLinks",
                column: "AutoMarkShipStatus");

            migrationBuilder.CreateIndex(
                name: "IX_OrderSupplierLinks_ConfluenceBatchNo",
                table: "OrderSupplierLinks",
                column: "ConfluenceBatchNo");

            migrationBuilder.CreateIndex(
                name: "IX_OrderSupplierLinks_FulfilAccountId",
                table: "OrderSupplierLinks",
                column: "FulfilAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderSupplierLinks_FulfilCardId",
                table: "OrderSupplierLinks",
                column: "FulfilCardId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderSupplierLinks_OrderId",
                table: "OrderSupplierLinks",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderSupplierLinks_PaidByUserId",
                table: "OrderSupplierLinks",
                column: "PaidByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderSupplierLinks_RelationBillno",
                table: "OrderSupplierLinks",
                column: "RelationBillno");

            migrationBuilder.CreateIndex(
                name: "IX_OrderSupplierLinks_SupplierOrderNo",
                table: "OrderSupplierLinks",
                column: "SupplierOrderNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderSupplierLinks_SupplierStatus",
                table: "OrderSupplierLinks",
                column: "SupplierStatus");

            migrationBuilder.CreateIndex(
                name: "IX_OrderSupplierLinks_TrackingNo",
                table: "OrderSupplierLinks",
                column: "TrackingNo");

            migrationBuilder.AddForeignKey(
                name: "FK_FulfilTransactions_OrderSupplierLinks_OrderSupplierLinkId",
                table: "FulfilTransactions",
                column: "OrderSupplierLinkId",
                principalTable: "OrderSupplierLinks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
