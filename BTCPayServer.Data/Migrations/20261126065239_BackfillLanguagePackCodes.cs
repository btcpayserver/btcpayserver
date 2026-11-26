using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261126065239_BackfillLanguagePackCodes")]
public class BackfillLanguagePackCodes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Published pack names and codes, including all historical manifest names:
        // https://github.com/btcpayserver/btcpayserver-translator/blob/098e3541ba5a1b8a688e1ef41d4538580fd5d564/manifest.json
        migrationBuilder.Sql("""
            UPDATE lang_dictionaries AS dictionary
            SET metadata = COALESCE(dictionary.metadata, '{}'::jsonb) || jsonb_build_object('code', pack.code)
            FROM (VALUES
                ('Amharic', 'am-ET'),
                ('Arabic', 'ar'),
                ('Armenian', 'hy'),
                ('Azerbaijani', 'az'),
                ('Bosnian', 'bs-BA'),
                ('Bulgarian', 'bg-BG'),
                ('Catalan', 'ca-ES'),
                ('Chinese (Simplified)', 'zh-CN'),
                ('Chinese (Traditional)', 'zh-TW'),
                ('Croatian', 'hr-HR'),
                ('Czech', 'cs-CZ'),
                ('Danish', 'da-DK'),
                ('Dutch', 'nl-NL'),
                ('Finnish', 'fi-FI'),
                ('French', 'fr-FR'),
                ('Georgian', 'ka'),
                ('German', 'de-DE'),
                ('Greek', 'el-GR'),
                ('Hebrew', 'he'),
                ('Hindi', 'hi'),
                ('Hungarian', 'hu-HU'),
                ('Icelandic', 'is-IS'),
                ('Indonesian', 'id'),
                ('Italian', 'it-IT'),
                ('Japanese', 'ja-JP'),
                ('Kazakh', 'kk-KZ'),
                ('Korean', 'ko'),
                ('Latvian', 'lv'),
                ('Nepali', 'ne-NP'),
                ('Norwegian', 'no'),
                ('Persian', 'fa'),
                ('Polish', 'pl'),
                ('Portuguese (Brazil)', 'pt-BR'),
                ('Portuguese (Portugal)', 'pt-PT'),
                ('Romanian', 'ro'),
                ('Russian', 'ru-RU'),
                ('Serbian', 'sr'),
                ('Slovak', 'sk-SK'),
                ('Slovenian', 'sl-SI'),
                ('Spanish', 'es-ES'),
                ('Swahili', 'sw'),
                ('Swedish', 'sv'),
                ('Thai', 'th-TH'),
                ('Turkish', 'tr'),
                ('Ukrainian', 'uk-UA'),
                ('Vietnamese', 'vi-VN')
            ) AS pack(name, code)
            WHERE lower(dictionary.dict_id) = lower(pack.name)
              AND dictionary.source = 'LanguagePack'
              AND NULLIF(btrim(dictionary.metadata->>'code'), '') IS NULL;
            """);
    }
}
