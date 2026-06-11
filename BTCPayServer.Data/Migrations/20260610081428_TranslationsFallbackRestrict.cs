using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260610081428_TranslationsFallbackRestrict")]
    public partial class TranslationsFallbackRestrict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE lang_dictionaries DROP CONSTRAINT lang_dictionaries_fallback_fkey;");
            migrationBuilder.Sql(
                "ALTER TABLE lang_dictionaries ADD CONSTRAINT lang_dictionaries_fallback_fkey FOREIGN KEY (fallback) REFERENCES lang_dictionaries(dict_id) ON UPDATE CASCADE ON DELETE RESTRICT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
