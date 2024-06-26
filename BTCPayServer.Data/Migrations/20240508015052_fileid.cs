using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240508015052_fileid")]
    public partial class fileid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
UPDATE "Settings"
SET "Value" = jsonb_set(
    "Value",
    '{LogoUrl}',
    to_jsonb('fileid:' || ("Value"->>'LogoFileId'))) - 'LogoFileId'
WHERE "Id" = 'BTCPayServer.Services.ThemeSettings'
AND "Value"->>'LogoFileId' IS NOT NULL;

UPDATE "Settings"
SET "Value" = jsonb_set(
    "Value",
    '{CustomThemeCssUrl}',
    to_jsonb('fileid:' || ("Value"->>'CustomThemeFileId'))) - 'CustomThemeFileId'
WHERE "Id" = 'BTCPayServer.Services.ThemeSettings'
AND "Value"->>'CustomThemeFileId' IS NOT NULL;

UPDATE "Stores"
SET "StoreBlob" = jsonb_set(
    "StoreBlob",
    '{logoUrl}',
    to_jsonb('fileid:' || ("StoreBlob"->>'logoFileId'))) - 'logoFileId'
WHERE "StoreBlob"->>'logoFileId' IS NOT NULL;

UPDATE "Stores"
SET "StoreBlob" = jsonb_set(
    "StoreBlob",
    '{cssUrl}',
    to_jsonb('fileid:' || ("StoreBlob"->>'cssFileId'))) - 'cssFileId'
WHERE "StoreBlob"->>'cssFileId' IS NOT NULL;

UPDATE "Stores"
SET "StoreBlob" = jsonb_set(
    "StoreBlob",
    '{paymentSoundUrl}',
    to_jsonb('fileid:' || ("StoreBlob"->>'soundFileId'))) - 'soundFileId'
WHERE "StoreBlob"->>'soundFileId' IS NOT NULL;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
