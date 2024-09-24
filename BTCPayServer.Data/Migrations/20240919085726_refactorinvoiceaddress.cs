using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240919085726_refactorinvoiceaddress")]
    public partial class refactorinvoiceaddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AddressInvoices",
                table: "AddressInvoices");

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethodId",
                table: "AddressInvoices",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "AddressInvoices"
                SET 
                "Address" = (string_to_array("Address", '#'))[1],
                "PaymentMethodId" = CASE WHEN (string_to_array("Address", '#'))[2] IS NULL THEN 'BTC-CHAIN'
                                         WHEN STRPOS((string_to_array("Address", '#'))[2], '_') = 0 THEN (string_to_array("Address", '#'))[2] || '-CHAIN'
                                         WHEN STRPOS((string_to_array("Address", '#'))[2], '_MoneroLike') > 0 THEN replace((string_to_array("Address", '#'))[2],'_MoneroLike','-CHAIN')
                                         WHEN STRPOS((string_to_array("Address", '#'))[2], '_ZcashLike') > 0 THEN replace((string_to_array("Address", '#'))[2],'_ZcashLike','-CHAIN')
                                         ELSE '' END;
                ALTER TABLE "AddressInvoices" DROP COLUMN IF EXISTS "CreatedTime";
                DELETE FROM "AddressInvoices" WHERE "PaymentMethodId" = '';
                """);
            migrationBuilder.AddPrimaryKey(
                name: "PK_AddressInvoices",
                table: "AddressInvoices",
                columns: new[] { "Address", "PaymentMethodId" });
            migrationBuilder.Sql("VACUUM (ANALYZE) \"AddressInvoices\";", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AddressInvoices",
                table: "AddressInvoices");

            migrationBuilder.DropColumn(
                name: "PaymentMethodId",
                table: "AddressInvoices");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AddressInvoices",
                table: "AddressInvoices",
                column: "Address");

            migrationBuilder.CreateTable(
                name: "PendingInvoices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingInvoices_Invoices_Id",
                        column: x => x.Id,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }
    }
}
