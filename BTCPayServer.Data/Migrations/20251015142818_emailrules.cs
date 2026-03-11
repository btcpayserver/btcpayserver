using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20251015142818_emailrules")]
    public partial class emailrules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_rules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    store_id = table.Column<string>(type: "text", nullable: true),
                    trigger = table.Column<string>(type: "text", nullable: false),
                    condition = table.Column<string>(type: "text", nullable: true),
                    to = table.Column<string[]>(type: "text[]", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    body = table.Column<string>(type: "text", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    additional_data = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_rules_Stores_store_id",
                        column: x => x.store_id,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_email_rules_store_id",
                table: "email_rules",
                column: "store_id");

            migrationBuilder.Sql(
                """
                INSERT INTO email_rules (store_id, "to", subject, body, "trigger", additional_data)
                SELECT
                  s."Id" AS store_id,
                  COALESCE(string_to_array(x."to", ','), ARRAY[]::text[]),
                  COALESCE(x.subject, ''),
                  COALESCE(x.body, ''),
                  CONCAT('WH-', x."trigger"),
                  jsonb_build_object('btcpay', jsonb_build_object('customerEmail', COALESCE(x."customerEmail", false))) AS additional_data
                FROM "Stores" AS s
                CROSS JOIN LATERAL jsonb_to_recordset(s."StoreBlob"->'emailRules')
                  AS x("to" text, subject text, body text, "trigger" text, "customerEmail" boolean)
                WHERE jsonb_typeof(s."StoreBlob"->'emailRules') = 'array' AND x."trigger" IS NOT NULL;
                """
            );
            migrationBuilder.Sql(
                """
                UPDATE "Stores"
                SET "StoreBlob" = "StoreBlob" - 'emailRules'
                WHERE jsonb_typeof("StoreBlob"->'emailRules') = 'array';
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_rules");
        }
    }
}
