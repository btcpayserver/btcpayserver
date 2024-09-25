using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{

    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240923065254_refactorpayments")]
    public partial class refactorpayments : DBScriptsMigration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Payments",
                table: "Payments");

            migrationBuilder.RenameColumn(
                name: "Type",
                table: "Payments",
                newName: "PaymentMethodId");
            migrationBuilder.Sql("UPDATE \"Payments\" SET \"PaymentMethodId\"='' WHERE \"PaymentMethodId\" IS NULL;");
            migrationBuilder.AddPrimaryKey(
                name: "PK_Payments",
                table: "Payments",
                columns: new[] { "Id", "PaymentMethodId" });
            base.Up(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
