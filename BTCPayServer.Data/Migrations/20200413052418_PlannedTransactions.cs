using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20200413052418_PlannedTransactions")]
    public partial class PlannedTransactions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlannedTransactions",
                columns: table => new
                {
                    Id = table.Column<string>(maxLength: 100, nullable: false),
                    BroadcastAt = table.Column<DateTimeOffset>(nullable: false),
                    Blob = table.Column<byte[]>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedTransactions", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "PayjoinLocks",
                columns: table => new
                {
                    Id = table.Column<string>(maxLength: 100, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayjoinLocks", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "OffchainTransactions",
                columns: table => new
                {
                    Id = table.Column<string>(maxLength: 64, nullable: false),
                    Blob = table.Column<byte[]>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OffchainTransactions", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayjoinLocks");
            migrationBuilder.DropTable(
                name: "PlannedTransactions");
            migrationBuilder.DropTable(
                name: "OffchainTransactions");
        }
    }
}
