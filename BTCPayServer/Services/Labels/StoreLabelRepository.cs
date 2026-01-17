#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Services.Labels;

public class StoreLabelRepository
{
    private readonly ApplicationDbContextFactory _contextFactory;

    public StoreLabelRepository(ApplicationDbContextFactory contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<(string Label, string Color)[]> GetStoreLabels(string storeId, string type)
    {
        await using var ctx = _contextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();

        var rows = (await conn.QueryAsync<(string Text, string? Color)>(
            """
            SELECT "Text", "Color"
            FROM "store_labels"
            WHERE "StoreId" = @storeId
              AND "Type"    = @type
            ORDER BY "Text"
            """, new { storeId, type })).ToList();

        return rows.Select(r => FormatToLabel(r.Text, r.Color)).ToArray();
    }

    public async Task<Dictionary<string, (string Label, string Color)[]>> GetStoreLabelsForObjects(
        string storeId,
        string type,
        string[]? objectIds)
    {
        objectIds ??= Array.Empty<string>();
        objectIds = objectIds
            .Select(o => o.Trim())
            .Where(o => !string.IsNullOrEmpty(o))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (objectIds.Length == 0)
            return new Dictionary<string, (string Label, string Color)[]>(StringComparer.Ordinal);

        await using var ctx = _contextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();

        var rows = await conn.QueryAsync<(string ObjectId, string Text, string? Color)>(
            """
            SELECT sll."ObjectId", sl."Text", sl."Color"
            FROM "store_label_links" sll
            INNER JOIN "store_labels" sl
                ON sl."StoreId" = sll."StoreId"
               AND sl."Id"      = sll."StoreLabelId"
            WHERE sll."StoreId"  = @storeId
              AND sll."ObjectId" = ANY(@objectIds)
              AND sl."Type"      = @type
            ORDER BY sll."ObjectId", sl."Text"
            """,
            new { storeId, type, objectIds });

        var dict = new Dictionary<string, List<(string Label, string Color)>>(StringComparer.Ordinal);

        foreach (var r in rows)
        {
            if (!dict.TryGetValue(r.ObjectId, out var list))
            {
                list = new List<(string Label, string Color)>();
                dict.Add(r.ObjectId, list);
            }

            list.Add(FormatToLabel(r.Text, r.Color));
        }

        return dict.ToDictionary(k => k.Key, v => v.Value.ToArray(), StringComparer.Ordinal);
    }

    public async Task SetStoreObjectLabels(string storeId, string type, string objectId, string[] labels)
    {
        var desired = labels
            .Select(NormalizeLabel)
            .Where(l => !string.IsNullOrEmpty(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await using var ctx = _contextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();

        var currentTexts = (await conn.QueryAsync<string>(
            """
            SELECT sl."Text"
            FROM "store_label_links" sll
            INNER JOIN "store_labels" sl
                ON sl."StoreId" = sll."StoreId"
               AND sl."Id"      = sll."StoreLabelId"
            WHERE sll."StoreId"  = @storeId
              AND sll."ObjectId" = @objectId
              AND sl."Type"      = @type
            """, new { storeId, type, objectId }))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAddTexts = desired.Where(t => !currentTexts.Contains(t)).ToArray();
        var toRemoveTexts = currentTexts.Where(t => !desired.Contains(t, StringComparer.OrdinalIgnoreCase)).ToArray();

        if (toAddTexts.Length == 0 && toRemoveTexts.Length == 0)
            return;

        await using var tx = await ctx.Database.BeginTransactionAsync();

        if (toRemoveTexts.Length > 0)
        {
            var toRemoveIds = (await conn.QueryAsync<string>(
                """
                SELECT "Id"
                FROM "store_labels"
                WHERE "StoreId" = @storeId
                  AND "Type"    = @type
                  AND "Text"    = ANY(@toRemoveTexts)
                """, new { storeId, type, toRemoveTexts })).ToArray();

            if (toRemoveIds.Length > 0)
            {
                await conn.ExecuteAsync(
                    """
                    DELETE FROM "store_label_links"
                    WHERE "StoreId"      = @storeId
                      AND "ObjectId"     = @objectId
                      AND "StoreLabelId" = ANY(@toRemoveIds)
                    """, new { storeId, objectId, toRemoveIds });
            }
        }

        if (toAddTexts.Length > 0)
        {
            var labelIdByText = await EnsureTypedLabelsExist(conn, storeId, type, toAddTexts);
            var toAddIds = toAddTexts.Select(t => labelIdByText[t]).ToArray();

            await conn.ExecuteAsync(
                """
                INSERT INTO "store_label_links" ("StoreId", "StoreLabelId", "ObjectId")
                SELECT @storeId, unnest(@toAddIds), @objectId
                ON CONFLICT ("StoreId", "StoreLabelId", "ObjectId") DO NOTHING
                """, new { storeId, objectId, toAddIds });
        }

        await tx.CommitAsync();
    }

    public async Task<bool> RemoveStoreLabels(string storeId, string type, string[] labels)
    {
        var normalized = labels
            .Select(l => l is null ? null : NormalizeLabel(l))
            .Where(l => !string.IsNullOrEmpty(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;

        if (normalized.Length == 0)
            return false;

        await using var ctx = _contextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();

        var affected = await conn.ExecuteAsync(
            """
            DELETE FROM "store_labels"
            WHERE "StoreId" = @storeId
              AND "Type"    = @type
              AND "Text"    = ANY(@normalized)
            """, new { storeId, type, normalized });

        return affected > 0;
    }

    public async Task<bool> RenameStoreLabel(string storeId, string type, string oldLabel, string newLabel)
    {
        oldLabel = NormalizeLabel(oldLabel);
        newLabel = NormalizeLabel(newLabel);

        if (string.IsNullOrEmpty(oldLabel) || string.IsNullOrEmpty(newLabel))
            return false;

        if (oldLabel.Equals(newLabel, StringComparison.OrdinalIgnoreCase))
            return true;

        await using var ctx = _contextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var oldId = await conn.QuerySingleOrDefaultAsync<string?>(
            """
            SELECT "Id"
            FROM "store_labels"
            WHERE "StoreId" = @storeId
              AND "Type"    = @type
              AND "Text"    = @oldLabel
            """, new { storeId, type, oldLabel });

        if (oldId is null)
            return false;

        var newId = await conn.QuerySingleOrDefaultAsync<string?>(
            """
            SELECT "Id"
            FROM "store_labels"
            WHERE "StoreId" = @storeId
              AND "Type"    = @type
              AND "Text"    = @newLabel
            """, new { storeId, type, newLabel });

        if (newId is null)
        {
            var updatedLabels = await conn.ExecuteAsync(
                """
                UPDATE "store_labels"
                SET "Text" = @newLabel
                WHERE "StoreId" = @storeId
                  AND "Id"      = @oldId
                """, new { storeId, oldId, newLabel });

            await tx.CommitAsync();
            return updatedLabels > 0;
        }

        await conn.ExecuteAsync(
            """
            DELETE FROM "store_label_links" old
            WHERE old."StoreId"      = @storeId
              AND old."StoreLabelId" = @oldId
              AND EXISTS (
                  SELECT 1
                  FROM "store_label_links" cur
                  WHERE cur."StoreId"      = old."StoreId"
                    AND cur."ObjectId"     = old."ObjectId"
                    AND cur."StoreLabelId" = @newId
              )
            """,
            new { storeId, oldId, newId });

        var updated = await conn.ExecuteAsync(
            """
            UPDATE "store_label_links"
            SET "StoreLabelId" = @newId
            WHERE "StoreId"      = @storeId
              AND "StoreLabelId" = @oldId
            """,
            new { storeId, oldId, newId });

        if (updated > 0)
        {
            await conn.ExecuteAsync(
                """
                DELETE FROM "store_labels" sl
                WHERE sl."StoreId" = @storeId
                  AND sl."Id"      = @oldId
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "store_label_links" sll
                      WHERE sll."StoreId"      = sl."StoreId"
                        AND sll."StoreLabelId" = sl."Id"
                  )
                """,
                new { storeId, oldId });
        }

        await tx.CommitAsync();
        return updated > 0;
    }

    private async Task<Dictionary<string, string>> EnsureTypedLabelsExist(
        System.Data.IDbConnection conn,
        string storeId,
        string type,
        string[] texts)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (texts.Length == 0)
            return result;

        var existing = (await conn.QueryAsync<(string Id, string Text)>(
            """
            SELECT "Id", "Text"
            FROM "store_labels"
            WHERE "StoreId" = @storeId
              AND "Type"    = @type
              AND "Text"    = ANY(@texts)
            """, new { storeId, type, texts })).ToList();

        foreach (var e in existing)
            result[e.Text] = e.Id;

        var missing = texts.Where(t => !result.ContainsKey(t)).ToArray();
        if (missing.Length > 0)
        {
            var rows = missing.Select(t => new
            {
                StoreId = storeId,
                Id = Guid.NewGuid().ToString(),
                Type = type,
                Text = t,
                Color = ColorPalette.Default.DeterministicColor(t)
            }).ToArray();

            await conn.ExecuteAsync(
                """
                INSERT INTO "store_labels" ("StoreId", "Id", "Type", "Text", "Color")
                VALUES (@StoreId, @Id, @Type, @Text, @Color)
                ON CONFLICT ("StoreId", "Type", "Text") DO UPDATE
                SET "Color" = COALESCE("store_labels"."Color", EXCLUDED."Color")
                """,
                rows);

            var inserted = await conn.QueryAsync<(string Id, string Text)>(
                """
                SELECT "Id", "Text"
                FROM "store_labels"
                WHERE "StoreId" = @storeId
                  AND "Type"    = @type
                  AND "Text"    = ANY(@missing)
                """, new { storeId, type, missing });

            foreach (var i in inserted)
                result[i.Text] = i.Id;
        }

        return result;
    }

    private static (string Label, string Color) FormatToLabel(string text, string? color)
    {
        return !string.IsNullOrEmpty(color) ? (text, color) : (text, ColorPalette.Default.DeterministicColor(text));
    }

    private const int MaxLabelSize = 50;

    private static string NormalizeLabel(string label)
    {
        label = label.Trim();
        if (label.Length > MaxLabelSize)
            label = label[..MaxLabelSize];
        return label;
    }

}
