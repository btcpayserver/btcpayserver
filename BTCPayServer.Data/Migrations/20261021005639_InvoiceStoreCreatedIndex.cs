using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261021005639_InvoiceStoreCreatedIndex")]
    public partial class InvoiceStoreCreatedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Invoices_StoreDataId_Created",
                table: "Invoices",
                columns: new[] { "StoreDataId", "Created" },
                descending: new[] { false, true });
        }
    }
}
