using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20210313141415_Fido2Credentials")]
    public partial class Fido2Credentials : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fido2Credentials",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    Name = table.Column<string>(nullable: true),
                    DescriptorBlob = table.Column<byte[]>(nullable: true),
                    PublicKey = table.Column<byte[]>(nullable: true),
                    UserHandle = table.Column<byte[]>(nullable: true),
                    SignatureCounter = table.Column<uint>(nullable: false),
                    CredType = table.Column<string>(nullable: true),
                    RegDate = table.Column<DateTimeOffset>(nullable: false),
                    AaGuid = table.Column<string>(nullable: true),
                    ApplicationUserId = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fido2Credentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fido2Credentials_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fido2Credentials_ApplicationUserId",
                table: "Fido2Credentials",
                column: "ApplicationUserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fido2Credentials");
        }
    }
}
