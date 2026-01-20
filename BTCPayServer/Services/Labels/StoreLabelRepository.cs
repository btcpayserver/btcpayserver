#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
            SELECT text, color
            FROM store_labels
            WHERE store_id = @storeId
              AND type     = @type
            ORDER BY text
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
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (objectIds.Length == 0)
            return new Dictionary<string, (string Label, string Color)[]>(StringComparer.Ordinal);

        await using var ctx = _contextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();

        var rows = await conn.QueryAsync<(string ObjectId, string Text, string? Color)>(
            """
            SELECT sll.object_id, sl.text, sl.color
            FROM store_label_links sll
            INNER JOIN store_labels sl
                ON sl.store_id = sll.store_id
               AND sl.id       = sll.store_label_id
            WHERE sll.store_id  = @storeId
              AND sll.object_id = ANY(@objectIds)
              AND sl.type       = @type
            ORDER BY sll.object_id, sl.text
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
            .Select(l => string.IsNullOrEmpty(l) ? string.Empty : NormalizeLabel(l))
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await using var ctx = _contextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();
        await using var tx = await ctx.Database.BeginTransactionAsync();
        var dbTx = tx.GetDbTransaction();

        var currentTexts = (await conn.QueryAsync<string>(
            """
            SELECT sl.text
            FROM store_label_links sll
            INNER JOIN store_labels sl
                ON sl.store_id = sll.store_id
               AND sl.id       = sll.store_label_id
            WHERE sll.store_id  = @storeId
              AND sll.object_id = @objectId
              AND sl.type       = @type
            """, new { storeId, type, objectId }, transaction: dbTx))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAddTexts = desired.Where(t => !currentTexts.Contains(t)).ToArray();
        var toRemoveTexts = currentTexts.Where(t => !desired.Contains(t, StringComparer.OrdinalIgnoreCase)).ToArray();

        if (toAddTexts.Length == 0 && toRemoveTexts.Length == 0)
        {
            await tx.CommitAsync();
            return;
        }

        if (toRemoveTexts.Length > 0)
        {
            var toRemoveIds = (await conn.QueryAsync<string>(
                """
                SELECT id
                FROM store_labels
                WHERE store_id = @storeId
                  AND type     = @type
                  AND text     = ANY(@toRemoveTexts)
                """, new { storeId, type, toRemoveTexts }, transaction: dbTx)).ToArray();

            if (toRemoveIds.Length > 0)
            {
                await conn.ExecuteAsync(
                    """
                    DELETE FROM store_label_links
                    WHERE store_id       = @storeId
                      AND object_id      = @objectId
                      AND store_label_id = ANY(@toRemoveIds)
                    """, new { storeId, objectId, toRemoveIds }, transaction: dbTx);
            }
        }

        if (toAddTexts.Length > 0)
        {
            var labelIdByText = await EnsureTypedLabelsExist(conn, dbTx, storeId, type, toAddTexts);
            var toAddIds = toAddTexts.Select(t => labelIdByText[t]).ToArray();

            await conn.ExecuteAsync(
                """
                INSERT INTO store_label_links (store_id, store_label_id, object_id)
                SELECT @storeId, unnest(@toAddIds), @objectId
                ON CONFLICT (store_id, store_label_id, object_id) DO NOTHING
                """, new { storeId, objectId, toAddIds }, transaction: dbTx);
        }

        await tx.CommitAsync();
    }

    public async Task<bool> RemoveStoreLabels(string storeId, string type, string[] labels)
    {
        var normalized = labels
            .Select(l => string.IsNullOrEmpty(l) ? string.Empty : NormalizeLabel(l))
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
            return false;

        var lowerNormalized = normalized.Select(n => n.ToLowerInvariant()).ToArray();
        await using var ctx = _contextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();

        var affected = await conn.ExecuteAsync(
            """
            DELETE FROM store_labels sl
            USING unnest(@lowerNormalized) AS t(lower_text)
            WHERE sl.store_id = @storeId
              AND sl.type     = @type
              AND lower(sl.text) = t.lower_text
            """, new { storeId, type, lowerNormalized });

        return affected > 0;
    }

    public async Task<bool> RenameStoreLabel(string storeId, string type, string oldLabel, string newLabel)
    {
        oldLabel = NormalizeLabel(oldLabel);
        newLabel = NormalizeLabel(newLabel);

        if (string.IsNullOrEmpty(oldLabel) || string.IsNullOrEmpty(newLabel))
            return false;

        await using var ctx = _contextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();
        await using var tx = await ctx.Database.BeginTransactionAsync();
        var dbTx = tx.GetDbTransaction();

        var oldLabelLower = oldLabel.ToLowerInvariant();
        var newLabelLower = newLabel.ToLowerInvariant();
        var oldId = await conn.QuerySingleOrDefaultAsync<string?>(
            """
            SELECT id
            FROM store_labels
            WHERE store_id = @storeId
              AND type     = @type
              AND lower(text) = @oldLabelLower
            """, new { storeId, type, oldLabelLower }, transaction: dbTx);

        if (oldId is null)
            return false;

        if (oldLabel.Equals(newLabel, StringComparison.Ordinal))
            return true;

        var newId = await conn.QuerySingleOrDefaultAsync<string?>(
            """
            SELECT id
            FROM store_labels
            WHERE store_id = @storeId
              AND type     = @type
              AND lower(text) = @newLabelLower
            """, new { storeId, type, newLabelLower }, transaction: dbTx);

        if (newId is null || newId == oldId)
        {
            // No existing label with the new text just rename the label in place
            var updatedLabels = await conn.ExecuteAsync(
                """
                UPDATE store_labels
                SET text = @newLabel
                WHERE store_id = @storeId
                  AND id       = @oldId
                """, new { storeId, oldId, newLabel }, transaction: dbTx);

            await tx.CommitAsync();
            return updatedLabels > 0;
        }

        // Merge old label into existing new label:
        // drop duplicate links
        await conn.ExecuteAsync(
            """
            DELETE FROM store_label_links old
            WHERE old.store_id       = @storeId
              AND old.store_label_id = @oldId
              AND EXISTS (
                  SELECT 1
                  FROM store_label_links cur
                  WHERE cur.store_id       = old.store_id
                    AND cur.object_id      = old.object_id
                    AND cur.store_label_id = @newId
              )
            """,
            new { storeId, oldId, newId }, transaction: dbTx);

        // relink remaining old links to newId
        await conn.ExecuteAsync(
            """
            UPDATE store_label_links
            SET store_label_id = @newId
            WHERE store_id       = @storeId
              AND store_label_id = @oldId
            """,
            new { storeId, oldId, newId }, transaction: dbTx);

        //delete old label if it becomes orphaned.
        await conn.ExecuteAsync(
            """
            DELETE FROM store_labels sl
            WHERE sl.store_id = @storeId
              AND sl.id       = @oldId
              AND NOT EXISTS (
                  SELECT 1
                  FROM store_label_links sll
                  WHERE sll.store_id       = sl.store_id
                    AND sll.store_label_id = sl.id
              )
            """,
            new { storeId, oldId }, transaction: dbTx);

        await tx.CommitAsync();
        return true;
    }

    private async Task<Dictionary<string, string>> EnsureTypedLabelsExist(
        DbConnection conn,
        DbTransaction dbTx,
        string storeId,
        string type,
        string[] texts)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (texts.Length == 0)
            return result;

        var lowerTexts = texts.Select(t => t.ToLowerInvariant()).ToArray();
        var existing = (await conn.QueryAsync<(string Id, string Text)>(
            """
            SELECT sl.id, sl.text
            FROM store_labels sl
            INNER JOIN unnest(@lowerTexts) AS t(lower_text)
              ON sl.store_id = @storeId
             AND sl.type     = @type
             AND lower(sl.text) = t.lower_text
            """, new { storeId, type, lowerTexts }, transaction: dbTx)).ToList();

        foreach (var e in existing)
            result[e.Text] = e.Id;

        var missing = texts.Where(t => !result.ContainsKey(t)).ToArray();
        if (missing.Length > 0)
        {
            var lowerMissing = missing.Select(m => m.ToLowerInvariant()).ToArray();
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
                INSERT INTO store_labels (store_id, id, type, text, color)
                VALUES (@StoreId, @Id, @Type, @Text, @Color)
                ON CONFLICT (store_id, type, lower(text)) DO UPDATE
                SET color = COALESCE(store_labels.color, EXCLUDED.color)
                """,
                rows,
                transaction: dbTx);

            var inserted = await conn.QueryAsync<(string Id, string Text)>(
                """
                SELECT sl.id, sl.text
                FROM store_labels sl
                INNER JOIN unnest(@lowerMissing) AS t(lower_text)
                  ON sl.store_id = @storeId
                 AND sl.type     = @type
                 AND lower(sl.text) = t.lower_text
                """, new { storeId, type, lowerMissing }, transaction: dbTx);

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
