using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20200402065615_AddApiKeyBlob")]
    public partial class AddApiKeyBlob : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (this.SupportDropColumn(migrationBuilder.ActiveProvider))
            {
                migrationBuilder.DropColumn(
                    name: "Permissions",
                    table: "ApiKeys");
            }

            migrationBuilder.AddColumn<byte[]>(
                name: "Blob",
                table: "ApiKeys",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (this.SupportDropColumn(migrationBuilder.ActiveProvider))
            {
                migrationBuilder.DropColumn(
                    name: "Blob",
                    table: "ApiKeys");
            }

            migrationBuilder.AddColumn<string>(
                name: "Permissions",
                table: "ApiKeys",
                type: "TEXT",
                nullable: true);
        }
    }
}
