using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    /// <inheritdoc />
    public partial class AppStuff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppStorageItems",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppStorageItems", x => new { x.Key, x.Version, x.UserId });
                    table.ForeignKey(
                        name: "FK_AppStorageItems_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppStorageItems_Key_UserId",
                table: "AppStorageItems",
                columns: new[] { "Key", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppStorageItems_UserId",
                table: "AppStorageItems",
                column: "UserId");

            migrationBuilder.Sql("CREATE FUNCTION \"LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA\"() RETURNS trigger as $LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA$\r\nBEGIN\r\n  DELETE FROM \"AppStorageItems\"\r\n  WHERE NEW.\"UserId\" = \"AppStorageItems\".\"UserId\" AND NEW.\"Key\" = \"AppStorageItems\".\"Key\" AND NEW.\"Version\" > \"AppStorageItems\".\"Version\";\r\nRETURN NEW;\r\nEND;\r\n$LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA$ LANGUAGE plpgsql;\r\nCREATE TRIGGER LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA BEFORE INSERT\r\nON \"AppStorageItems\"\r\nFOR EACH ROW EXECUTE PROCEDURE \"LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA\"();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION \"LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA\"() CASCADE;");

            migrationBuilder.DropTable(
                name: "AppStorageItems");
        }
    }
}
