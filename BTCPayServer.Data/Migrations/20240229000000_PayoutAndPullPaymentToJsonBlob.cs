using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240229000000_PayoutAndPullPaymentToJsonBlob")]
    public partial class PayoutAndPullPaymentToJsonBlob : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Payouts\" ALTER COLUMN \"Blob\" TYPE JSONB USING regexp_replace(convert_from(\"Blob\",'UTF8'), '\\\\u0000', '', 'g')::JSONB");
            migrationBuilder.Sql("ALTER TABLE \"Payouts\" ALTER COLUMN \"Proof\" TYPE JSONB USING regexp_replace(convert_from(\"Proof\",'UTF8'), '\\\\u0000', '', 'g')::JSONB");
            migrationBuilder.Sql("ALTER TABLE \"PullPayments\" ALTER COLUMN \"Blob\" TYPE JSONB USING regexp_replace(convert_from(\"Blob\",'UTF8'), '\\\\u0000', '', 'g')::JSONB");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
