using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20220928020131_label")]
    public partial class label : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WalletLabels",
                columns: table => new
                {
                    WalletId = table.Column<string>(type: "TEXT", nullable: false),
                    LabelId = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletLabels", x => new { x.WalletId, x.LabelId });
                });

            migrationBuilder.CreateTable(
                name: "WalletObjects",
                columns: table => new
                {
                    WalletId = table.Column<string>(type: "TEXT", nullable: false),
                    ObjectTypeId = table.Column<string>(type: "TEXT", nullable: false),
                    ObjectId = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletObjects", x => new { x.WalletId, x.ObjectTypeId, x.ObjectId });
                });

            migrationBuilder.CreateTable(
                name: "WalletTaints",
                columns: table => new
                {
                    WalletId = table.Column<string>(type: "TEXT", nullable: false),
                    ObjectTypeId = table.Column<string>(type: "TEXT", nullable: false),
                    ObjectId = table.Column<string>(type: "TEXT", nullable: false),
                    TaintTypeId = table.Column<string>(type: "TEXT", nullable: false),
                    TaintId = table.Column<string>(type: "TEXT", nullable: false),
                    LabelId = table.Column<string>(type: "TEXT", nullable: true),
                    Stickiness = table.Column<int>(type: "INTEGER", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTaints", x => new { x.WalletId, x.ObjectTypeId, x.ObjectId, x.TaintTypeId, x.TaintId });
                    table.ForeignKey(
                        name: "FK_WalletTaints_WalletLabels_WalletId_LabelId",
                        columns: x => new { x.WalletId, x.LabelId },
                        principalTable: "WalletLabels",
                        principalColumns: new[] { "WalletId", "LabelId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WalletTaints_WalletObjects_WalletId_ObjectTypeId_ObjectId",
                        columns: x => new { x.WalletId, x.ObjectTypeId, x.ObjectId },
                        principalTable: "WalletObjects",
                        principalColumns: new[] { "WalletId", "ObjectTypeId", "ObjectId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTaints_WalletId_LabelId",
                table: "WalletTaints",
                columns: new[] { "WalletId", "LabelId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalletTaints");

            migrationBuilder.DropTable(
                name: "WalletLabels");

            migrationBuilder.DropTable(
                name: "WalletObjects");
        }
    }
}
