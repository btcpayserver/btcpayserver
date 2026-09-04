using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260904133933_hardenapikey")]
    public partial class hardenapikey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 ALTER TABLE "ApiKeys"
                                     ADD COLUMN "CreatedAt" timestamptz NULL DEFAULT now(),
                                     ADD COLUMN "Hash" text NULL,
                                     ADD COLUMN "Key" text NULL,
                                     ADD COLUMN "Prefix" text NULL;

                                 DELETE FROM "ApiKeys"
                                 WHERE "Type" = 0;

                                 DELETE FROM "ApiKeyPermissionUsages" AS usage
                                 WHERE NOT EXISTS (
                                     SELECT 1
                                     FROM "ApiKeys" AS api_key
                                     WHERE api_key."Id" = usage."ApiKey"
                                 );

                                 ALTER TABLE "ApiKeys"
                                     DROP COLUMN "Type",
                                     ALTER COLUMN "Id" TYPE text,
                                     ALTER COLUMN "StoreId" TYPE text,
                                     ALTER COLUMN "UserId" TYPE text;

                                 ALTER TABLE "ApiKeyPermissionUsages"
                                     RENAME COLUMN "ApiKey" TO "ApiKeyId";

                                 UPDATE "ApiKeyPermissionUsages" AS usage
                                 SET "Id" = 'akid_' || left(encode(sha256(sha256(convert_to(api_key."Id", 'UTF8'))), 'hex'), 16) ||
                                            substring(usage."Id" FROM char_length(api_key."Id") + 1),
                                     "ApiKeyId" = 'akid_' || left(encode(sha256(sha256(convert_to(api_key."Id", 'UTF8'))), 'hex'), 16)
                                 FROM "ApiKeys" AS api_key
                                 WHERE usage."ApiKeyId" = api_key."Id";

                                 UPDATE "ApiKeys"
                                 SET "Hash" = encode(sha256(convert_to("Id", 'UTF8')), 'hex'),
                                     "Prefix" = left("Id", 6),
                                     "Id" = 'akid_' || left(encode(sha256(sha256(convert_to("Id", 'UTF8'))), 'hex'), 16);
                                 """);
        }
    }
}
