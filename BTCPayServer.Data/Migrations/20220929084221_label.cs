using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20220929084221_label")]
    public partial class label : Migration
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
                name: "WalletObjects",
                columns: table => new
                {
                    WalletId = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: migrationBuilder.IsNpgsql() ? "JSONB" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletObjects", x => new { x.WalletId, x.Type, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "WalletObjectLinks",
                columns: table => new
                {
                    WalletId = table.Column<string>(type: "TEXT", nullable: false),
                    ParentId = table.Column<string>(type: "TEXT", nullable: false),
                    ParentType = table.Column<string>(type: "TEXT", nullable: false),
                    ChildId = table.Column<string>(type: "TEXT", nullable: false),
                    ChildType = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: migrationBuilder.IsNpgsql() ? "JSONB" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletObjectLinks", x => new { x.WalletId, x.ParentId, x.ParentType, x.ChildId, x.ChildType });
                    table.ForeignKey(
                        name: "FK_WalletObjectLinks_WalletObjects_WalletId_ChildType_ChildId",
                        columns: x => new { x.WalletId, x.ChildType, x.ChildId },
                        principalTable: "WalletObjects",
                        principalColumns: new[] { "WalletId", "Type", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WalletObjectLinks_WalletObjects_WalletId_ParentType_ParentId",
                        columns: x => new { x.WalletId, x.ParentType, x.ParentId },
                        principalTable: "WalletObjects",
                        principalColumns: new[] { "WalletId", "Type", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalletObjectLinks_WalletId_ChildId_ChildType",
                table: "WalletObjectLinks",
                columns: new[] { "WalletId", "ChildId", "ChildType" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletObjectLinks_WalletId_ChildType_ChildId",
                table: "WalletObjectLinks",
                columns: new[] { "WalletId", "ChildType", "ChildId" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletObjectLinks_WalletId_ParentType_ParentId",
                table: "WalletObjectLinks",
                columns: new[] { "WalletId", "ParentType", "ParentId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalletObjectLinks");

            migrationBuilder.DropTable(
                name: "WalletObjects");

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
