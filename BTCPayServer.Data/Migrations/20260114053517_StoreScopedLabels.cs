using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260114053517_StoreScopedLabels")]
    public partial class StoreScopedLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "store_labels",
                columns: table => new
                {
                    store_id = table.Column<string>(type: "text", nullable: false),
                    id       = table.Column<string>(type: "text", nullable: false),
                    type     = table.Column<string>(type: "text", nullable: false),
                    text     = table.Column<string>(type: "text", nullable: false),
                    color    = table.Column<string>(type: "text", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreLabels", x => new { x.store_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreLabels_StoreId_Type_Text",
                table: "store_labels",
                columns: new[] { "store_id", "type", "text" },
                unique: true);


            migrationBuilder.CreateTable(
                name: "store_label_links",
                columns: table => new
                {
                    store_id       = table.Column<string>(type: "text", nullable: false),
                    store_label_id = table.Column<string>(type: "text", nullable: false),
                    object_id      = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreLabelLinks", x => new { x.store_id, x.store_label_id, x.object_id });
                    table.ForeignKey(
                        name: "FK_StoreLabelLinks_StoreLabels_StoreId_StoreLabelId",
                        columns: x => new { x.store_id, x.store_label_id },
                        principalTable: "store_labels",
                        principalColumns: new[] { "store_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreLabelLinks_StoreId_ObjectId",
                table: "store_label_links",
                columns: new[] { "store_id", "object_id" });

            migrationBuilder.Sql(@"CREATE EXTENSION IF NOT EXISTS pgcrypto;");

            // Copy Payment Request label objects (label metadata) into StoreLabels
            migrationBuilder.Sql(@"
            WITH pr_links AS (
                SELECT DISTINCT
                    wol.""WalletId"" AS ""WalletId"",
                    wol.""AId"" AS ""LabelText"",
                    wol.""BId"" AS ""PaymentRequestId""
                FROM ""WalletObjectLinks"" wol
                WHERE wol.""AType"" = 'label'
                  AND wol.""BType"" = 'payment-request'
            ),
            pr_labels AS (
                SELECT DISTINCT
                    pr.""StoreDataId"" AS ""StoreId"",
                    pl.""LabelText"",
                    wo.""Data"" AS ""LabelData""
                FROM pr_links pl
                INNER JOIN ""PaymentRequests"" pr
                  ON pr.""Id"" = pl.""PaymentRequestId""
                INNER JOIN ""WalletObjects"" wo
                  ON wo.""WalletId"" = pl.""WalletId""
                 AND wo.""Type"" = 'label'
                 AND wo.""Id"" = pl.""LabelText""
            )
            INSERT INTO store_labels (store_id, id, type, text, color)
            SELECT
                ""StoreId"",
                gen_random_uuid()::text,
                'payment-request',
                ""LabelText"",
                (""LabelData""::jsonb ->> 'color')
            FROM pr_labels
            ON CONFLICT (store_id, type, text) DO NOTHING;
            ");

            // Copy Payment Request label links into StoreLabelLinks
            migrationBuilder.Sql(@"
            WITH pr_links AS (
                SELECT DISTINCT
                    pr.""StoreDataId"" AS ""StoreId"",
                    wol.""AId""        AS ""LabelText"",
                    wol.""BId""        AS ""ObjectId""
                FROM ""WalletObjectLinks"" wol
                INNER JOIN ""PaymentRequests"" pr
                  ON pr.""Id"" = wol.""BId""
                WHERE wol.""AType"" = 'label'
                  AND wol.""BType"" = 'payment-request'
            )
            INSERT INTO store_label_links (store_id, store_label_id, object_id)
            SELECT
                pl.""StoreId"",
                sl.id AS store_label_id,
                pl.""ObjectId""
            FROM pr_links pl
            INNER JOIN store_labels sl
              ON sl.store_id = pl.""StoreId""
             AND sl.type     = 'payment-request'
             AND sl.text     = pl.""LabelText""
            ON CONFLICT (store_id, store_label_id, object_id) DO NOTHING;
            ");

            // Remove the Payment Request label links from the wallet graph
            migrationBuilder.Sql(@"
            DELETE FROM ""WalletObjectLinks"" wol
            WHERE wol.""AType"" = 'label'
              AND wol.""BType"" = 'payment-request';
            ");

            // Remove unlinked Labels from WalletObjects
                        migrationBuilder.Sql(@"
            WITH pr_wallets AS (
                SELECT DISTINCT wo.""WalletId""
                FROM ""WalletObjects"" wo
                WHERE wo.""Type"" = 'payment-request'
            )
            DELETE FROM ""WalletObjects"" wo
            WHERE wo.""Type"" = 'label'
              AND wo.""WalletId"" IN (SELECT ""WalletId"" FROM pr_wallets)
              AND NOT EXISTS (
                  SELECT 1
                  FROM ""WalletObjectLinks"" wol
                  WHERE wol.""WalletId"" = wo.""WalletId""
                    AND (
                         (wol.""AType"" = 'label' AND wol.""AId"" = wo.""Id"")
                      OR (wol.""BType"" = 'label' AND wol.""BId"" = wo.""Id"")
                    )
              );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "store_label_links");

            migrationBuilder.DropTable(
                name: "store_labels");
        }
    }
}
