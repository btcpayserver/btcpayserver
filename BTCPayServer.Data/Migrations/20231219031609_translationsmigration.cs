using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20231219031609_translationsmigration")]
    public partial class translationsmigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.IsNpgsql())
            {
                migrationBuilder.Sql("""
CREATE TABLE lang_dictionaries (
    dict_id TEXT PRIMARY KEY,
    fallback TEXT DEFAULT NULL,
    source TEXT DEFAULT NULL,
    metadata JSONB DEFAULT NULL,
    FOREIGN KEY (fallback) REFERENCES lang_dictionaries(dict_id) ON UPDATE CASCADE ON DELETE SET NULL
);
INSERT INTO lang_dictionaries(dict_id, source) VALUES ('English', 'Default');

CREATE TABLE lang_translations (
    dict_id TEXT NOT NULL,
    sentence TEXT NOT NULL,
    translation TEXT NOT NULL,
    PRIMARY KEY (dict_id, sentence),
    FOREIGN KEY (dict_id) REFERENCES lang_dictionaries(dict_id) ON UPDATE CASCADE ON DELETE CASCADE
);

CREATE VIEW translations AS
WITH RECURSIVE translations_with_paths AS (
    SELECT d.dict_id, t.sentence, t.translation, ARRAY[d.dict_id] AS path FROM lang_translations t
    INNER JOIN lang_dictionaries d USING (dict_id)

    UNION ALL

    SELECT d.dict_id, t.sentence, t.translation, d.dict_id || t.path FROM translations_with_paths t
    INNER JOIN lang_dictionaries d ON d.fallback=t.dict_id
),
ranked_translations AS (
    SELECT *,
    ROW_NUMBER() OVER (PARTITION BY dict_id, sentence ORDER BY array_length(path, 1)) AS rn
    FROM translations_with_paths
)
SELECT dict_id, sentence, translation, path FROM ranked_translations WHERE rn=1;
COMMENT ON VIEW translations IS 'Compute the translation for all sentences for all dictionaries, taking into account fallbacks';
""");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
