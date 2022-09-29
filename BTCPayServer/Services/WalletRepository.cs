using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Services.Labels;
using Microsoft.EntityFrameworkCore;
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

            var rows = await ctx.WalletObjectLinks
                .AsNoTracking()
                .Where(w => w.WalletId == walletId.ToString() && w.ChildType == WalletObjectData.Types.Transaction && (transactionIds == null || transactionIds.Contains(w.ChildId)))
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
                    info = new WalletTransactionInfo();
                    result.Add(row.TxId, info);
                }
                if (row.AssociatedDataType == WalletObjectData.Types.Comment)
                {
                    info.Comment = data["comment"].Value<string>();
                }
                else if (row.AssociatedDataType == WalletObjectData.Types.Label)
                {
                    info.Labels.Add(row.AssociatedDataId, new WalletTransactionInfo.LabelAssociatedData(row.AssociatedDataId)
                    {
                        Color = data["color"].Value<string>()
                    });
                }
            }
            foreach (var row in rows)
            {
                if (!result.TryGetValue(row.TxId, out var info))
                    continue;
                JObject data = row.AssociatedData is null ? null : JObject.Parse(row.AssociatedData);
                var type = data["type"]?.Value<string>();
                if (type is null || !info.Labels.TryGetValue(type, out var associatedData))
                    continue;
                var label = Label.TryParse(row.AssociatedData);
                if (label is null || label.Type != type)
                    continue;
                associatedData.Metadata.Add(label);
            }
            return result;
        }

#nullable enable
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
