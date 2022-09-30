using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Services.Labels;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.Crypto;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services
{
#nullable enable
    public record WalletObjectId(WalletId WalletId, string Type, string Id);
#nullable restore
    public class WalletRepository
    {
        private readonly ApplicationDbContextFactory _ContextFactory;

        public WalletRepository(ApplicationDbContextFactory contextFactory)
        {
            _ContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public async Task<Dictionary<string, WalletTransactionInfo>> GetWalletTransactionsInfo(WalletId walletId, string[] transactionIds = null)
        {
            ArgumentNullException.ThrowIfNull(walletId);
            using var ctx = _ContextFactory.CreateContext();

            IQueryable<WalletObjectLinkData> wols;

            // If we are using postgres, the `transactionIds.Contains(w.ChildId)` result in a long query like `ANY(@txId1, @txId2, @txId3, @txId4)`
            // Such request isn't well optimized by postgres, and create different requests clogging up
            // pg_stat_statements output, making it impossible to analyze the performance impact of this query.
            if (ctx.Database.IsNpgsql() && transactionIds is not null)
            {
                wols = ctx.WalletObjectLinks
                    .FromSqlInterpolated($"SELECT wol.* FROM unnest({transactionIds}) t JOIN \"WalletObjectLinks\" wol ON wol.\"WalletId\"={walletId.ToString()} AND wol.\"ChildType\"={WalletObjectData.Types.Transaction} AND wol.\"ChildId\"=t")
                    .AsNoTracking();
            }
            else // Unefficient path
            {
                wols = ctx.WalletObjectLinks
                .AsNoTracking()
                .Where(w => w.WalletId == walletId.ToString() && w.ChildType == WalletObjectData.Types.Transaction && (transactionIds == null || transactionIds.Contains(w.ChildId)));
            }
            var rows = await wols
                .Select(tx =>
                new
                {
                    TxId = tx.ChildId,
                    AssociatedDataId = tx.ParentId,
                    AssociatedDataType = tx.ParentType,
                    AssociatedData = tx.Parent.Data
                })
                .ToArrayAsync();

            var result = new Dictionary<string, WalletTransactionInfo>(rows.Length);
            foreach (var row in rows)
            {
                JObject data = row.AssociatedData is null ? null : JObject.Parse(row.AssociatedData);
                if (!result.TryGetValue(row.TxId, out var info))
                {
                    info = new WalletTransactionInfo(walletId);
                    result.Add(row.TxId, info);
                }
                if (row.AssociatedDataType == WalletObjectData.Types.Comment)
                {
                    info.Comment = data["comment"].Value<string>();
                }
                else if (row.AssociatedDataType == WalletObjectData.Types.Label)
                {
                    info.LabelColors.TryAdd(row.AssociatedDataId, data["color"]?.Value<string>() ?? "#000");
                }
                else
                {
                    info.Tags.Add(new TransactionTag(row.AssociatedDataType, row.AssociatedDataId, row.AssociatedData is null ? null : JObject.Parse(row.AssociatedData)));
                }
            }
            return result;
        }

#nullable enable

        public async Task<(string Label, string Color)[]> GetWalletLabels(WalletId walletId)
        {
            using var ctx = _ContextFactory.CreateContext();
            return (await ctx.WalletObjects
                .AsNoTracking()
                .Where(w => w.WalletId == walletId.ToString() && w.Type == WalletObjectData.Types.Label)
                .Select(o => new { o.Id, o.Data })
                .ToArrayAsync())
                .Select(o => (o.Id, JObject.Parse(o.Data)["color"]!.Value<string>()!))
                .ToArray();
        }

        public async Task EnsureWalletObjectLink(WalletObjectId parent, WalletObjectId child)
        {
            using var ctx = _ContextFactory.CreateContext();
            var l = new WalletObjectLinkData()
            {
                WalletId = parent.WalletId.ToString(),
                ChildType = child.Type,
                ChildId = child.Id,
                ParentType = parent.Type,
                ParentId = parent.Id
            };
            ctx.WalletObjectLinks.Add(l);
            try
            {
                await ctx.SaveChangesAsync();
            }
            catch (DbUpdateException) // already exists
            {
            }
        }

        public static int MaxCommentSize = 200;
        public async Task SetWalletObjectComment(WalletObjectId id, string comment)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(comment);
            await EnsureWalletObject(id);
            var commentObjId = new WalletObjectId(id.WalletId, WalletObjectData.Types.Comment, "");
            await SetWalletObject(commentObjId, new JObject()
            {
                ["comment"] = comment.Trim().Truncate(MaxCommentSize)
            });
            await EnsureWalletObjectLink(commentObjId, id);
        }
        const int MaxLabelSize = 20;
        public async Task AddWalletObjectLabels(WalletObjectId id, params string[] labels)
        {
            ArgumentNullException.ThrowIfNull(id);
            await EnsureWalletObject(id);
            foreach (var l in labels.Select(l => l.Trim().Truncate(MaxLabelSize)))
            {
                var labelObjId = new WalletObjectId(id.WalletId, WalletObjectData.Types.Label, l);
                await EnsureWalletObject(labelObjId, new JObject()
                {
                    ["color"] = ColorPalette.Default.DeterministicColor(l)
                });
                await EnsureWalletObjectLink(labelObjId, id);
            }
        }

        public Task AddWalletTransactionTags(WalletId walletId, uint256 txId, TransactionTag tag)
        {
            return AddWalletTransactionTags(walletId, txId, new[] { tag });
        }
        public async Task AddWalletTransactionTags(WalletId walletId, uint256 txId, IEnumerable<TransactionTag> tags)
        {
            ArgumentNullException.ThrowIfNull(walletId);
            ArgumentNullException.ThrowIfNull(txId);
            var txObjId = new WalletObjectId(walletId, WalletObjectData.Types.Transaction, txId.ToString());
            await EnsureWalletObject(txObjId);
            foreach (var tag in tags)
            {
                var labelObjId = new WalletObjectId(walletId, WalletObjectData.Types.Label, tag.Label);
                await EnsureWalletObject(labelObjId, new JObject()
                {
                    ["color"] = ColorPalette.Default.DeterministicColor(tag.Label)
                });
                await EnsureWalletObjectLink(labelObjId, txObjId);
                if (tag.AssociatedData is not null || tag.Id.Length != 0)
                {
                    var data = new WalletObjectId(walletId, tag.Label, tag.Id);
                    await EnsureWalletObject(data, tag.AssociatedData);
                    await EnsureWalletObjectLink(data, txObjId);
                }
            }
        }
        public async Task RemoveWalletObjectLabels(WalletObjectId id, params string[] labels)
        {
            ArgumentNullException.ThrowIfNull(id);
            foreach (var l in labels.Select(l => l.Trim()))
            {
                var labelObjId = new WalletObjectId(id.WalletId, WalletObjectData.Types.Label, l);
                using var ctx = _ContextFactory.CreateContext();
                ctx.WalletObjectLinks.Remove(new WalletObjectLinkData()
                {
                    WalletId = id.WalletId.ToString(),
                    ChildId = id.Id,
                    ChildType = id.Type,
                    ParentId = labelObjId.Id,
                    ParentType = labelObjId.Type
                });
                try
                {
                    await ctx.SaveChangesAsync();
                }
                catch (DbUpdateException) // Already deleted, do nothing
                {
                }
            }
        }

        public async Task SetWalletObject(WalletObjectId id, JObject data)
        {
            ArgumentNullException.ThrowIfNull(id);
            using var ctx = _ContextFactory.CreateContext();
            var o = new WalletObjectData()
            {
                WalletId = id.WalletId.ToString(),
                Type = id.Type,
                Id = id.Id,
                Data = data?.ToString()
            };
            ctx.WalletObjects.Add(o);
            try
            {
                await ctx.SaveChangesAsync();
            }
            catch (DbUpdateException) // already exists
            {
                ctx.Entry(o).State = EntityState.Modified;
                await ctx.SaveChangesAsync();
            }
        }

        public async Task EnsureWalletObject(WalletObjectId id, JObject? data = null)
        {
            ArgumentNullException.ThrowIfNull(id);
            using var ctx = _ContextFactory.CreateContext();
            ctx.WalletObjects.Add(new WalletObjectData()
            {
                WalletId = id.WalletId.ToString(),
                Type = id.Type,
                Id = id.Id,
                Data = data?.ToString()
            });
            try
            {
                await ctx.SaveChangesAsync();
            }
            catch (DbUpdateException) // already exists
            {
            }
        }
#nullable restore
    }
}
