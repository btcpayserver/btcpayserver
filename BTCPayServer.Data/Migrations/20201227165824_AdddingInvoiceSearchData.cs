using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20201227165824_AdddingInvoiceSearchData")]
    public partial class AdddingInvoiceSearchData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceSearchDatas",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        // manually added
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn)
                        .Annotation("MySql:ValueGeneratedOnAdd", true)
                        .Annotation("Sqlite:Autoincrement", true),
                        // eof manually added
                    InvoiceDataId = table.Column<string>(nullable: true),
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
                name: "IX_InvoiceSearchDatas_InvoiceDataId",
                table: "InvoiceSearchDatas",
                column: "InvoiceDataId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSearchDatas_Value",
                table: "InvoiceSearchDatas",
                column: "Value");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceSearchDatas");
        }
    }
}
