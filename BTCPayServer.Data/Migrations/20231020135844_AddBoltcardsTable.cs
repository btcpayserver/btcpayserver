using System.Security.Permissions;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20231020135844_AddBoltcardsTable")]
    public partial class AddBoltcardsTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "boltcards",
                columns: table => new
                {
                    id = table.Column<string>(maxLength: 32, nullable: false),
                    counter = table.Column<int>(type: "INT", nullable: false, defaultValue: 0),
                    ppid = table.Column<string>(maxLength: 30, nullable: true),
                    version = table.Column<int>(nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_id", x => x.id);
                    table.ForeignKey("FK_boltcards_PullPayments", x => x.ppid, "PullPayments", "Id", onDelete: ReferentialAction.SetNull);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("boltcards");
        }
    }
}
