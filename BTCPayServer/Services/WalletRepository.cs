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
            IQueryable<WalletObjectData> wos;

            // If we are using postgres, the `transactionIds.Contains(w.ChildId)` result in a long query like `ANY(@txId1, @txId2, @txId3, @txId4)`
            // Such request isn't well optimized by postgres, and create different requests clogging up
            // pg_stat_statements output, making it impossible to analyze the performance impact of this query.
            if (ctx.Database.IsNpgsql() && transactionIds is not null)
            {
                wos = ctx.WalletObjects
                    .FromSqlInterpolated($"SELECT wos.* FROM unnest({transactionIds}) t JOIN \"WalletObjects\" wos ON wos.\"WalletId\"={walletId.ToString()} AND wos.\"Type\"={WalletObjectData.Types.Tx} AND wos.\"Id\"=t")
                    .AsNoTracking();
                wols = ctx.WalletObjectLinks
                    .FromSqlInterpolated($"SELECT wol.* FROM unnest({transactionIds}) t JOIN \"WalletObjectLinks\" wol ON wol.\"WalletId\"={walletId.ToString()} AND wol.\"ChildType\"={WalletObjectData.Types.Tx} AND wol.\"ChildId\"=t")
                    .AsNoTracking();
            }
            else // Unefficient path
            {
                wos = ctx.WalletObjects
                    .AsNoTracking()
                    .Where(w => w.WalletId == walletId.ToString() && w.Type == WalletObjectData.Types.Tx && (transactionIds == null || transactionIds.Contains(w.Id)));
                wols = ctx.WalletObjectLinks
                .AsNoTracking()
                .Where(w => w.WalletId == walletId.ToString() && w.ChildType == WalletObjectData.Types.Tx && (transactionIds == null || transactionIds.Contains(w.ChildId)));
            }
            var links = await wols
                .Select(tx =>
                new
                {
                    TxId = tx.ChildId,
                    AssociatedDataId = tx.ParentId,
                    AssociatedDataType = tx.ParentType,
                    AssociatedData = tx.Parent.Data
                })
                .ToArrayAsync();
            var objs = await wos
                .Select(tx =>
                new
                {
                    TxId = tx.Id,
                    Data = tx.Data
                })
                .ToArrayAsync();

            var result = new Dictionary<string, WalletTransactionInfo>(objs.Length);
            foreach (var obj in objs)
            {
                var data = obj.Data is null ? null : JObject.Parse(obj.Data);
                result.Add(obj.TxId, new WalletTransactionInfo(walletId)
                {
                    Comment = data?["comment"]?.Value<string>()
                });
            }

            
            foreach (var row in links)
            {
                JObject data = row.AssociatedData is null ? null : JObject.Parse(row.AssociatedData);
                var info = result[row.TxId];

                if (row.AssociatedDataType == WalletObjectData.Types.Label)
                {
                    info.LabelColors.TryAdd(row.AssociatedDataId, data["color"]?.Value<string>() ?? "#000");
                }
                else
                {
                    info.Attachments.Add(new Attachment(row.AssociatedDataType, row.AssociatedDataId, row.AssociatedData is null ? null : JObject.Parse(row.AssociatedData)));
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
            if (!string.IsNullOrEmpty(comment))
                await ModifyWalletObjectData(id, (o) => o["comment"] = comment.Trim().Truncate(MaxCommentSize));
            else
                await ModifyWalletObjectData(id, (o) => o.Remove("comment"));
        }


        static WalletObjectData NewWalletObjectData(WalletObjectId id, JObject? data = null)
        {
            return new WalletObjectData()
            {
                WalletId = id.WalletId.ToString(),
                Type = id.Type,
                Id = id.Id,
                Data = data?.ToString()
            };
        }
        public async Task ModifyWalletObjectData(WalletObjectId id, Action<JObject> modify)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(modify);
            using var ctx = _ContextFactory.CreateContext();
            var obj = await ctx.WalletObjects.FindAsync(id.WalletId.ToString(), id.Type, id.Id);
            if (obj is null)
            {
                obj = NewWalletObjectData(id);
                ctx.WalletObjects.Add(obj);
            }
            var currentData = obj.Data is null ? new JObject() : JObject.Parse(obj.Data);
            modify(currentData);
            obj.Data = currentData.ToString();
            if (obj.Data == "{}")
                obj.Data = null;
            await ctx.SaveChangesAsync();
        }

        const int MaxLabelSize = 50;
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

        public Task AddWalletTransactionAttachment(WalletId walletId, uint256 txId, Attachment attachment)
        {
            return AddWalletTransactionAttachment(walletId, txId, new[] { attachment });
        }
        public async Task AddWalletTransactionAttachment(WalletId walletId, uint256 txId, IEnumerable<Attachment> attachments)
        {
            ArgumentNullException.ThrowIfNull(walletId);
            ArgumentNullException.ThrowIfNull(txId);
            var txObjId = new WalletObjectId(walletId, WalletObjectData.Types.Tx, txId.ToString());
            await EnsureWalletObject(txObjId);
            foreach (var attachment in attachments)
            {
                var labelObjId = new WalletObjectId(walletId, WalletObjectData.Types.Label, attachment.Type);
                await EnsureWalletObject(labelObjId, new JObject()
                {
                    ["color"] = ColorPalette.Default.DeterministicColor(attachment.Type)
                });
                await EnsureWalletObjectLink(labelObjId, txObjId);
                if (attachment.Data is not null || attachment.Id.Length != 0)
                {
                    var data = new WalletObjectId(walletId, attachment.Type, attachment.Id);
                    await EnsureWalletObject(data, attachment.Data);
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

        public async Task SetWalletObject(WalletObjectId id, JObject? data)
        {
            ArgumentNullException.ThrowIfNull(id);
            using var ctx = _ContextFactory.CreateContext();
            var o = NewWalletObjectData(id, data);
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
            ctx.WalletObjects.Add(NewWalletObjectData(id, data));
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
