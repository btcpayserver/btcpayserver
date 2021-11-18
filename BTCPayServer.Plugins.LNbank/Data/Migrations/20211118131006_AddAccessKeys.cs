using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Plugins.LNbank.Data.Migrations
{
    public partial class AddAccessKeys : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessKeys",
                schema: "BTCPayServer.Plugins.LNbank",
                columns: table => new
                {
                    Key = table.Column<string>(nullable: false),
                    WalletId = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessKeys", x => x.Key);
                    table.ForeignKey(
                        name: "FK_AccessKeys_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalSchema: "BTCPayServer.Plugins.LNbank",
                        principalTable: "Wallets",
                        principalColumn: "WalletId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessKeys_WalletId",
                schema: "BTCPayServer.Plugins.LNbank",
                table: "AccessKeys",
                column: "WalletId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessKeys",
                schema: "BTCPayServer.Plugins.LNbank");
        }
    }
}
