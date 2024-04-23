using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20180114123253_events")]
    public partial class events : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
                        migrationBuilder.CreateTable(
                name: "InvoiceEvents",
                columns: table => new
                {
                    InvoiceDataId = table.Column<string>(nullable: false, maxLength: null),
                    UniqueId = table.Column<string>(nullable: false, maxLength: null),
                    Message = table.Column<string>(nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceEvents", x => new { x.InvoiceDataId, x.UniqueId });
                    table.ForeignKey(
                        name: "FK_InvoiceEvents_Invoices_InvoiceDataId",
                        column: x => x.InvoiceDataId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceEvents");
        }
    }
}
