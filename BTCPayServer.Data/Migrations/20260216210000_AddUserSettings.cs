using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260216210000_AddUserSettings")]
    public partial class AddUserSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 CREATE TABLE "UserSettings" (
                                     "UserId" TEXT NOT NULL,
                                     "Name" TEXT NOT NULL,
                                     "Value" JSONB,
                                     CONSTRAINT "PK_UserSettings" PRIMARY KEY ("UserId", "Name"),
                                     CONSTRAINT "FK_UserSettings_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
                                 );
                                 CREATE INDEX "IX_UserSettings_UserId" ON "UserSettings" ("UserId");
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 DROP TABLE IF EXISTS "UserSettings";
                                 """);
        }
    }
}
