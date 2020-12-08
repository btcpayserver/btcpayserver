using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20201208150236_InvoiceSearchTable")]
    public partial class InvoiceSearchTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            int? maxLength = this.IsMySql(migrationBuilder.ActiveProvider) ? (int?)255 : null;
            migrationBuilder.CreateTable(
                name: "InvoiceSearchDatas",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false, maxLength:maxLength),
                    InvoiceDataId = table.Column<string>(nullable: true, maxLength:maxLength),
                    Value = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceSearchDatas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceSearchDatas_Invoices_InvoiceDataId",
                        column: x => x.InvoiceDataId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSearchDatas_Value",
                table: "InvoiceSearchDatas",
                column: "Value");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSearchDatas_InvoiceDataId_Value",
                table: "InvoiceSearchDatas",
                columns: new[] { "InvoiceDataId", "Value" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceSearchDatas");
        }
    }
}
