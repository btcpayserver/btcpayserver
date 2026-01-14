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
                name: "StoreLabels",
                columns: table => new
                {
                    StoreId = table.Column<string>(type: "text", nullable: false),
                    LabelId = table.Column<string>(type: "text", nullable: false),
                    Data = table.Column<string>(type: "JSONB", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreLabels", x => new { x.StoreId, x.LabelId });
                });

            migrationBuilder.CreateTable(
                name: "StoreLabelLinks",
                columns: table => new
                {
                    StoreId = table.Column<string>(type: "text", nullable: false),
                    LabelId = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    ObjectId = table.Column<string>(type: "text", nullable: false),
                    Data = table.Column<string>(type: "JSONB", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreLabelLinks", x => new { x.StoreId, x.LabelId, x.Type, x.ObjectId });
                    table.ForeignKey(
                        name: "FK_StoreLabelLinks_StoreLabels_StoreId_LabelId",
                        columns: x => new { x.StoreId, x.LabelId },
                        principalTable: "StoreLabels",
                        principalColumns: new[] { "StoreId", "LabelId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreLabelLinks_StoreId_Type_LabelId",
                table: "StoreLabelLinks",
                columns: new[] { "StoreId", "Type", "LabelId" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreLabelLinks_StoreId_Type_ObjectId",
                table: "StoreLabelLinks",
                columns: new[] { "StoreId", "Type", "ObjectId" });


            // Copy Payment Request label objects (label metadata) into StoreLabels
            migrationBuilder.Sql(@"
            WITH pr_links AS (
                SELECT DISTINCT
                    wol.""WalletId"" AS ""WalletId"",
                    wol.""AId""      AS ""LabelId"",
                    wol.""BId""      AS ""PaymentRequestId""
                FROM ""WalletObjectLinks"" wol
                WHERE wol.""AType"" = 'label'
                  AND wol.""BType"" = 'payment-request'
            ),
            pr_labels AS (
                SELECT DISTINCT
                    pr.""StoreDataId"" AS ""StoreId"",
                    pl.""LabelId"",
                    wo.""Data""        AS ""LabelData""
                FROM pr_links pl
                INNER JOIN ""PaymentRequests"" pr
                  ON pr.""Id"" = pl.""PaymentRequestId""
                INNER JOIN ""WalletObjects"" wo
                  ON wo.""WalletId"" = pl.""WalletId""
                 AND wo.""Type"" = 'label'
                 AND wo.""Id"" = pl.""LabelId""
            )
            INSERT INTO ""StoreLabels"" (""StoreId"", ""LabelId"", ""Data"")
            SELECT ""StoreId"", ""LabelId"", ""LabelData""
            FROM pr_labels
            ON CONFLICT (""StoreId"", ""LabelId"") DO NOTHING;
            ");

            // Copy Payment Request label links into StoreLabelLinks
            migrationBuilder.Sql(@"
            WITH pr_links AS (
                SELECT DISTINCT
                    pr.""StoreDataId"" AS ""StoreId"",
                    wol.""AId""        AS ""LabelId"",
                    wol.""BType""      AS ""Type"",
                    wol.""BId""        AS ""ObjectId"",
                    wol.""Data""       AS ""LinkData""
                FROM ""WalletObjectLinks"" wol
                INNER JOIN ""PaymentRequests"" pr
                  ON pr.""Id"" = wol.""BId""
                WHERE wol.""AType"" = 'label'
                  AND wol.""BType"" = 'payment-request'
            )
            INSERT INTO ""StoreLabelLinks"" (""StoreId"", ""LabelId"", ""Type"", ""ObjectId"", ""Data"")
            SELECT ""StoreId"", ""LabelId"", ""Type"", ""ObjectId"", ""LinkData""
            FROM pr_links
            ON CONFLICT (""StoreId"", ""LabelId"", ""Type"", ""ObjectId"") DO NOTHING;
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
                name: "StoreLabelLinks");

            migrationBuilder.DropTable(
                name: "StoreLabels");
        }
    }
}
