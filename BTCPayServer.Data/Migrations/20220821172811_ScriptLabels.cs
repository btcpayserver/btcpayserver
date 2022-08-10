using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    public partial class ScriptLabels : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "CustodianAccount",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "WalletLabels",
                columns: table => new
                {
                    WalletDataId = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletLabels", x => new { x.WalletDataId, x.Label });
                    table.ForeignKey(
                        name: "FK_WalletLabels_Wallets_WalletDataId",
                        column: x => x.WalletDataId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WalletScripts",
                columns: table => new
                {
                    WalletDataId = table.Column<string>(type: "TEXT", nullable: false),
                    Script = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletScripts", x => new { x.WalletDataId, x.Script });
                    table.ForeignKey(
                        name: "FK_WalletScripts_Wallets_WalletDataId",
                        column: x => x.WalletDataId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WalletLabelDataWalletTransactionData",
                columns: table => new
                {
                    WalletLabelsWalletDataId = table.Column<string>(type: "TEXT", nullable: false),
                    WalletLabelsLabel = table.Column<string>(type: "TEXT", nullable: false),
                    WalletTransactionsWalletDataId = table.Column<string>(type: "TEXT", nullable: false),
                    WalletTransactionsTransactionId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletLabelDataWalletTransactionData", x => new { x.WalletLabelsWalletDataId, x.WalletLabelsLabel, x.WalletTransactionsWalletDataId, x.WalletTransactionsTransactionId });
                    table.ForeignKey(
                        name: "FK_WalletLabelDataWalletTransactionData_WalletLabels_WalletLabelsWalletDataId_WalletLabelsLabel",
                        columns: x => new { x.WalletLabelsWalletDataId, x.WalletLabelsLabel },
                        principalTable: "WalletLabels",
                        principalColumns: new[] { "WalletDataId", "Label" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WalletLabelDataWalletTransactionData_WalletTransactions_WalletTransactionsWalletDataId_WalletTransactionsTransactionId",
                        columns: x => new { x.WalletTransactionsWalletDataId, x.WalletTransactionsTransactionId },
                        principalTable: "WalletTransactions",
                        principalColumns: new[] { "WalletDataId", "TransactionId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WalletLabelDataWalletScriptData",
                columns: table => new
                {
                    WalletLabelsWalletDataId = table.Column<string>(type: "TEXT", nullable: false),
                    WalletLabelsLabel = table.Column<string>(type: "TEXT", nullable: false),
                    WalletScriptsWalletDataId = table.Column<string>(type: "TEXT", nullable: false),
                    WalletScriptsScript = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletLabelDataWalletScriptData", x => new { x.WalletLabelsWalletDataId, x.WalletLabelsLabel, x.WalletScriptsWalletDataId, x.WalletScriptsScript });
                    table.ForeignKey(
                        name: "FK_WalletLabelDataWalletScriptData_WalletLabels_WalletLabelsWalletDataId_WalletLabelsLabel",
                        columns: x => new { x.WalletLabelsWalletDataId, x.WalletLabelsLabel },
                        principalTable: "WalletLabels",
                        principalColumns: new[] { "WalletDataId", "Label" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WalletLabelDataWalletScriptData_WalletScripts_WalletScriptsWalletDataId_WalletScriptsScript",
                        columns: x => new { x.WalletScriptsWalletDataId, x.WalletScriptsScript },
                        principalTable: "WalletScripts",
                        principalColumns: new[] { "WalletDataId", "Script" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WalletScriptDataWalletTransactionData",
                columns: table => new
                {
                    WalletScriptsWalletDataId = table.Column<string>(type: "TEXT", nullable: false),
                    WalletScriptsScript = table.Column<string>(type: "TEXT", nullable: false),
                    WalletTransactionsWalletDataId = table.Column<string>(type: "TEXT", nullable: false),
                    WalletTransactionsTransactionId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletScriptDataWalletTransactionData", x => new { x.WalletScriptsWalletDataId, x.WalletScriptsScript, x.WalletTransactionsWalletDataId, x.WalletTransactionsTransactionId });
                    table.ForeignKey(
                        name: "FK_WalletScriptDataWalletTransactionData_WalletScripts_WalletScriptsWalletDataId_WalletScriptsScript",
                        columns: x => new { x.WalletScriptsWalletDataId, x.WalletScriptsScript },
                        principalTable: "WalletScripts",
                        principalColumns: new[] { "WalletDataId", "Script" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WalletScriptDataWalletTransactionData_WalletTransactions_WalletTransactionsWalletDataId_WalletTransactionsTransactionId",
                        columns: x => new { x.WalletTransactionsWalletDataId, x.WalletTransactionsTransactionId },
                        principalTable: "WalletTransactions",
                        principalColumns: new[] { "WalletDataId", "TransactionId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalletLabelDataWalletScriptData_WalletScriptsWalletDataId_WalletScriptsScript",
                table: "WalletLabelDataWalletScriptData",
                columns: new[] { "WalletScriptsWalletDataId", "WalletScriptsScript" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletLabelDataWalletTransactionData_WalletTransactionsWalletDataId_WalletTransactionsTransactionId",
                table: "WalletLabelDataWalletTransactionData",
                columns: new[] { "WalletTransactionsWalletDataId", "WalletTransactionsTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletScriptDataWalletTransactionData_WalletTransactionsWalletDataId_WalletTransactionsTransactionId",
                table: "WalletScriptDataWalletTransactionData",
                columns: new[] { "WalletTransactionsWalletDataId", "WalletTransactionsTransactionId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalletLabelDataWalletScriptData");

            migrationBuilder.DropTable(
                name: "WalletLabelDataWalletTransactionData");

            migrationBuilder.DropTable(
                name: "WalletScriptDataWalletTransactionData");

            migrationBuilder.DropTable(
                name: "WalletLabels");

            migrationBuilder.DropTable(
                name: "WalletScripts");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "CustodianAccount",
                type: "TEXT",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 50);
        }
    }
}
