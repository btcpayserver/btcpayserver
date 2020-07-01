using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20171012020112_PendingInvoices")]
    public partial class PendingInvoices : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            int? maxLength = this.IsMySql(migrationBuilder.ActiveProvider) ? (int?)255 : null;
            if (this.SupportDropColumn(migrationBuilder.ActiveProvider))
            {
                migrationBuilder.DropColumn(
                    name: "Name",
                    table: "PairingCodes");

                migrationBuilder.DropColumn(
                    name: "Name",
                    table: "PairedSINData");
            }
            migrationBuilder.CreateTable(
                name: "PendingInvoices",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false, maxLength: maxLength)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingInvoices", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingInvoices");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "PairingCodes",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "PairedSINData",
                nullable: true);
        }
    }
}
