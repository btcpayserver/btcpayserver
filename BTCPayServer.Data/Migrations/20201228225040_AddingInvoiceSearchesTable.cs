using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20201228225040_AddingInvoiceSearchesTable")]
    public partial class AddingInvoiceSearchesTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceSearches",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        // manually added
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn)
                        .Annotation("MySql:ValueGeneratedOnAdd", true)
                        .Annotation("Sqlite:Autoincrement", true),
                    // eof manually added
                    InvoiceDataId = table.Column<string>(maxLength: 255, nullable: true),
                    Value = table.Column<string>(maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceSearches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceSearches_Invoices_InvoiceDataId",
                        column: x => x.InvoiceDataId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSearches_InvoiceDataId",
                table: "InvoiceSearches",
                column: "InvoiceDataId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSearches_Value",
                table: "InvoiceSearches",
                column: "Value");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceSearches");
        }
    }
}
