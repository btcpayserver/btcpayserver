#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Labels;

public class StoreLabelRepository
{
    private readonly ApplicationDbContextFactory _contextFactory;

    public StoreLabelRepository(ApplicationDbContextFactory contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<(string Label, string Color)[]> GetStoreLabels(string storeId)
    {
        await using var ctx = _contextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();

        var rows = (await conn.QueryAsync<(string LabelId, string? Data)>(
            """
            SELECT "LabelId", "Data"
            FROM "StoreLabels"
            WHERE "StoreId" = @storeId
            """, new { storeId })).ToList();

        return rows.Select(r => FormatToLabel(r.LabelId, r.Data)).ToArray();
    }

    public async Task<(string Label, string Color)[]> GetStoreLabels(string storeId, string type, string id)
    {
        await using var ctx = _contextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();

        var rows = (await conn.QueryAsync<(string LabelId, string? Data)>(
            """
            SELECT sl."LabelId", sl."Data"
            FROM "StoreLabelLinks" sll
            INNER JOIN "StoreLabels" sl
                ON sl."StoreId" = sll."StoreId"
               AND sl."LabelId" = sll."LabelId"
            WHERE sll."StoreId" = @storeId
              AND sll."Type" = @type
              AND sll."ObjectId" = @id
            """, new { storeId, type, id })).ToList();

        return rows.Select(r => FormatToLabel(r.LabelId, r.Data)).ToArray();
    }

    public async Task<(string Label, string Color)[]> GetStoreLabelsByLinkedType(string storeId, string linkedType)
    {
        await using var ctx = _contextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();

        var rows = (await conn.QueryAsync<(string LabelId, string? Data)>(
            """
            SELECT DISTINCT sl."LabelId", sl."Data"
            FROM "StoreLabelLinks" sll
            INNER JOIN "StoreLabels" sl
                ON sl."StoreId" = sll."StoreId"
               AND sl."LabelId" = sll."LabelId"
            WHERE sll."StoreId" = @storeId
              AND sll."Type" = @linkedType
            """, new { storeId, linkedType })).ToList();

        return rows.Select(r => FormatToLabel(r.LabelId, r.Data)).ToArray();
    }

    public async Task SetStoreObjectLabels(string storeId, string type, string id, string[] labels)
    {
        labels = labels
            .Select(NormalizeLabel)
            .Where(l => !string.IsNullOrEmpty(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await using var ctx = _contextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();

        var current = (await conn.QueryAsync<string>(
            """
            SELECT "LabelId"
            FROM "StoreLabelLinks"
            WHERE "StoreId" = @storeId
              AND "Type" = @type
              AND "ObjectId" = @id
            """, new { storeId, type, id })).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = labels.Where(l => !current.Contains(l)).ToArray();
        var toRemove = current.Where(l => !labels.Contains(l, StringComparer.OrdinalIgnoreCase)).ToArray();

        await using var tx = await ctx.Database.BeginTransactionAsync();

        if (toRemove.Length > 0)
        {
            await conn.ExecuteAsync(
                """
                DELETE FROM "StoreLabelLinks"
                WHERE "StoreId" = @storeId
                  AND "Type" = @type
                  AND "ObjectId" = @id
                  AND "LabelId" = ANY(@toRemove)
                """, new { storeId, type, id, toRemove });
        }

        if (toAdd.Length > 0)
        {
            var labelRows = toAdd.Select(l => new
            {
                StoreId = storeId,
                LabelId = l,
                Data = CreateLabelDataJson(l)
            }).ToArray();

            await conn.ExecuteAsync(
                """
                INSERT INTO "StoreLabels" ("StoreId", "LabelId", "Data")
                VALUES (@StoreId, @LabelId, @Data::jsonb)
                ON CONFLICT ("StoreId", "LabelId")
                DO UPDATE SET "Data" = COALESCE("StoreLabels"."Data", EXCLUDED."Data")
                """,
                labelRows);

            await conn.ExecuteAsync(
                """
                INSERT INTO "StoreLabelLinks" ("StoreId", "LabelId", "Type", "ObjectId", "Data")
                SELECT @storeId, unnest(@toAdd), @type, @id, NULL
                ON CONFLICT ("StoreId", "LabelId", "Type", "ObjectId") DO NOTHING
                """, new { storeId, type, id, toAdd });
        }

        await tx.CommitAsync();
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

        var rows = await conn.QueryAsync<(string ObjectId, string LabelId, string? Data)>(
            """
            SELECT sll."ObjectId", sl."LabelId", sl."Data"
            FROM "StoreLabelLinks" sll
            INNER JOIN "StoreLabels" sl
                ON sl."StoreId" = sll."StoreId"
               AND sl."LabelId" = sll."LabelId"
            WHERE sll."StoreId" = @storeId
              AND sll."Type" = @type
              AND sll."ObjectId" = ANY(@objectIds)
            ORDER BY sll."ObjectId", sl."LabelId"
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

            list.Add(FormatToLabel(r.LabelId, r.Data));
        }

        return dict.ToDictionary(k => k.Key, v => v.Value.ToArray(), StringComparer.Ordinal);
    }

    public async Task<bool> RemoveStoreLabels(string storeId, string[] labels)
    {
        labels = labels
            .Select(l => l?.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;

        if (labels.Length == 0)
            return false;

        await using var ctx = _contextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();

        var affected = await conn.ExecuteAsync(
            """
            DELETE FROM "StoreLabels"
            WHERE "StoreId" = @storeId
              AND "LabelId" = ANY(@labels)
            """,
            new { storeId, labels });

        return affected > 0;
    }

    public async Task<bool> RenameStoreLabel(string storeId, string oldLabel, string newLabel)
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

        await conn.ExecuteAsync(
            """
            INSERT INTO "StoreLabels" ("StoreId", "LabelId", "Data")
            VALUES (@storeId, @newLabel, @data::jsonb)
            ON CONFLICT ("StoreId", "LabelId")
            DO UPDATE SET "Data" = COALESCE("StoreLabels"."Data", EXCLUDED."Data")
            """,
            new { storeId, newLabel, data = CreateLabelDataJson(newLabel) });

        await conn.ExecuteAsync(
            """
            DELETE FROM "StoreLabelLinks" old
            WHERE old."StoreId" = @storeId
              AND old."LabelId" = @oldLabel
              AND EXISTS (
                  SELECT 1
                  FROM "StoreLabelLinks" cur
                  WHERE cur."StoreId"  = old."StoreId"
                    AND cur."Type"     = old."Type"
                    AND cur."ObjectId" = old."ObjectId"
                    AND cur."LabelId"  = @newLabel
              )
            """,
            new { storeId, oldLabel, newLabel });

        var updated = await conn.ExecuteAsync(
            """
            UPDATE "StoreLabelLinks"
            SET "LabelId" = @newLabel
            WHERE "StoreId" = @storeId
              AND "LabelId" = @oldLabel
            """,
            new { storeId, oldLabel, newLabel });

        if (updated > 0)
        {
            await conn.ExecuteAsync(
                """
                DELETE FROM "StoreLabels"
                WHERE "StoreId" = @storeId
                  AND "LabelId" = @oldLabel
                """,
                new { storeId, oldLabel });
        }

        await tx.CommitAsync();
        return updated > 0;
    }

    private static (string Label, string Color) FormatToLabel(string labelId, string? data)
    {
        if (string.IsNullOrEmpty(data))
            return (labelId, ColorPalette.Default.DeterministicColor(labelId));

        try
        {
            var color = JObject.Parse(data)["color"]?.Value<string>();
            return (labelId, color ?? ColorPalette.Default.DeterministicColor(labelId));
        }
        catch
        {
            return (labelId, ColorPalette.Default.DeterministicColor(labelId));
        }
    }

    private const int MaxLabelSize = 50;

    private static string NormalizeLabel(string label)
    {
        label = label.Trim();
        if (label.Length > MaxLabelSize)
            label = label[..MaxLabelSize];
        return label;
    }

    private static string CreateLabelDataJson(string labelId)
    {
        var o = new JObject
        {
            ["color"] = ColorPalette.Default.DeterministicColor(labelId)
        };
        return o.ToString(Newtonsoft.Json.Formatting.None);
    }
}
