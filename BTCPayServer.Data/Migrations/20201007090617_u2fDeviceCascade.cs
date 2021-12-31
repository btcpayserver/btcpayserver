using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20201007090617_u2fDeviceCascade")]
    public partial class u2fDeviceCascade : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (this.SupportDropForeignKey(migrationBuilder.ActiveProvider))
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_U2FDevices_AspNetUsers_ApplicationUserId",
                    table: "U2FDevices");

                migrationBuilder.AddForeignKey(
                    name: "FK_U2FDevices_AspNetUsers_ApplicationUserId",
                    table: "U2FDevices",
                    column: "ApplicationUserId",
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (this.SupportDropForeignKey(migrationBuilder.ActiveProvider))
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_U2FDevices_AspNetUsers_ApplicationUserId",
                    table: "U2FDevices");

                migrationBuilder.AddForeignKey(
                    name: "FK_U2FDevices_AspNetUsers_ApplicationUserId",
                    table: "U2FDevices",
                    column: "ApplicationUserId",
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            }
        }
    }
}
