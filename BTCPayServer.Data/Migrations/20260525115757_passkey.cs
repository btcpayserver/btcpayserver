using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260525115757_passkey")]
    public partial class passkey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastUsedAt",
                table: "Fido2Credentials",
                type: "timestamp with time zone",
                nullable: true);
            migrationBuilder.AddColumn<bool>(
                name: "AuthenticatorEnabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                                 UPDATE "AspNetUsers" u
                                 SET "AuthenticatorEnabled" = true
                                 FROM "AspNetUserTokens" t
                                 WHERE u."Id" = t."UserId"
                                   AND u."TwoFactorEnabled" = true
                                   AND t."Name" = 'AuthenticatorKey';

                                 UPDATE "AspNetUsers"
                                 SET "TwoFactorEnabled" = true
                                 WHERE "TwoFactorEnabled" = false;
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
