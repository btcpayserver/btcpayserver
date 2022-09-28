using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Data.Data;
using BTCPayServer.Services.Labels;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services
{
    public class WalletRepository
    {
        private readonly ApplicationDbContextFactory _ContextFactory;

        public WalletRepository(ApplicationDbContextFactory contextFactory)
        {
            _ContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public async Task SetWalletInfo(WalletId walletId, WalletBlobInfo blob)
        {
            ArgumentNullException.ThrowIfNull(walletId);
            using var ctx = _ContextFactory.CreateContext();
            var labels = await ctx.WalletLabels.Where(l => l.WalletId == walletId.ToString())
                .ToDictionaryAsync(l => l.LabelId, l => l);

            foreach (var l in labels)
            {
                if (blob?.LabelColors is null || !blob.LabelColors.ContainsKey(l.Key))
                    ctx.WalletLabels.Remove(l.Value);
                else
                {
                    var jobj = JObject.Parse(l.Value.Data);
                    var existingColor = jobj["color"].Value<string>();
                    if (existingColor != blob.LabelColors[l.Key])
                    {
                        jobj["color"] = blob.LabelColors[l.Key];
                        l.Value.Data = jobj.ToString();
                    }
                }
            }
            foreach (var l in blob.LabelColors)
            {
                if (!labels.ContainsKey(l.Key))
                {
                    var data = new JObject();
                    data.Add("color", l.Value);
                    var wld = new WalletLabelData()
                    {
                        WalletId = walletId.ToString(),
                        LabelId = l.Key,
                        Data = data.ToString()
                    };
                    ctx.WalletLabels.Add(wld);
                    try
                    {
                        await ctx.SaveChangesAsync();
                    }
                    catch (DbUpdateException) // concurrency issue, this has already been added
                    {
                        ctx.Entry(wld).State = EntityState.Modified;
                        await ctx.SaveChangesAsync();
                    }
                }
            }
            await ctx.SaveChangesAsync();
        }

        public async Task<Dictionary<string, WalletTransactionInfo>> GetWalletTransactionsInfo(WalletId walletId, string[] transactionIds = null)
        {
            ArgumentNullException.ThrowIfNull(walletId);


            using var ctx = _ContextFactory.CreateContext();
            var rows = await ctx.WalletObjects
                .Where(wo => wo.WalletId == walletId.ToString() && wo.ObjectTypeId == WalletObjectData.ObjectTypes.Tx)
                .Where(wo => transactionIds == null || transactionIds.Contains(wo.ObjectId))
                .Select(wo =>
                new
                {
                    TxId = wo.ObjectId,
                    Taints = wo.Taints.Select(t =>
                    new
                    {
                        t.LabelId,
                        t.Data
                    }),
                    TxData = wo.Data
                })
                .ToArrayAsync();
            var result = new Dictionary<string, WalletTransactionInfo>();
            foreach (var r in rows)
            {
                if (!result.TryGetValue(r.TxId, out var wti))
                {
                    wti = new WalletTransactionInfo();
                    wti.Comment = JObject.Parse(r.TxData)["comment"]?.Value<string>();
                    result.Add(r.TxId, wti);
                    foreach (var l in r.Taints)
                        wti.Labels.TryAdd(l.LabelId, Label.Parse(l.Data));
                }
            }
            return result;
        }

        public async Task<WalletBlobInfo> GetWalletInfo(WalletId walletId)
        {
            ArgumentNullException.ThrowIfNull(walletId);
            using var ctx = _ContextFactory.CreateContext();
            var labels = await ctx.WalletLabels
                .Where(l => l.WalletId == walletId.ToString())
                .Select(l => new
                {
                    LabelId = l.LabelId,
                    Data = l.Data
                })
                .ToArrayAsync();

            WalletBlobInfo wbi = new WalletBlobInfo();
            foreach (var l in labels)
            {
                wbi.LabelColors.Add(l.LabelId, JObject.Parse(l.Data)["color"].Value<string>());
            }
            return wbi;
        }

        public async Task SetWalletTransactionInfo(WalletId walletId, string transactionId, WalletTransactionInfo walletTransactionInfo)
        {
            // Note: This method is really unoptimized and should be refactored. It makes 2 request to db to change comment + 2 or 3 requests per label.
            // This method should ideally be removed and replaced by more specific methods such as SetComment, AddLabel, RemoveLabel
            ArgumentNullException.ThrowIfNull(walletId);
            ArgumentNullException.ThrowIfNull(transactionId);
            using var ctx = _ContextFactory.CreateContext();

            // Saving comment
            var walletData = new WalletObjectData() { WalletId = walletId.ToString(), ObjectTypeId = WalletObjectData.ObjectTypes.Tx, ObjectId = transactionId };
            var existing = await ctx.WalletObjects
                             .Include(wo => wo.Taints)
                             .ThenInclude(t => t.Label)
                             .Where(wo => wo.WalletId == walletData.WalletId && wo.ObjectTypeId == walletData.ObjectTypeId && wo.ObjectId == walletData.ObjectId)
                             .FirstOrDefaultAsync();
            if (existing is not null)
                walletData = existing;
            else
                ctx.WalletObjects.Add(walletData);
            JObject blob;
            if (walletData.Data is not null)
            {
                blob = JObject.Parse(walletData.Data);
                blob.Remove("comment");
            }
            else
            {
                blob = new JObject();
            }
            blob.Add("comment", walletTransactionInfo.Comment);
            walletData.Data = blob.ToString();
            await ctx.SaveChangesAsync();
            // Comment saved!

            foreach (var label in walletTransactionInfo.Labels)
            {
                var labelData = new WalletLabelData() { WalletId = walletData.WalletId, LabelId = label.Key };
                var existingLabel = await ctx.WalletLabels.FindAsync(labelData.WalletId, labelData.LabelId);
                if (existingLabel is null)
                {
                    ctx.WalletLabels.Add(labelData);
                    try
                    {
                        await ctx.SaveChangesAsync();
                    }
                    catch (DbUpdateException) // Probably already exists, move along
                    {
                        ctx.Entry(labelData).State = EntityState.Unchanged;
                    }
                }
                var taintData = new WalletTaintData()
                {
                    WalletId = walletData.WalletId,
                    LabelId = labelData.LabelId,
                    TaintId = label.Value.TaintId,
                    Stickiness = 0,
                    TaintTypeId = label.Value.Type,
                    ObjectId = walletData.ObjectId,
                    ObjectTypeId = walletData.ObjectTypeId,
                    Data = JsonConvert.SerializeObject(label.Value, CamelCase)
                };

                var existingTaintData = await ctx.WalletTaints.FindAsync(taintData.WalletId,
                            taintData.ObjectTypeId,
                            taintData.ObjectId,
                            taintData.TaintTypeId,
                            taintData.TaintId);
                if (existingTaintData != null)
                {
                    existingTaintData.Data = taintData.Data;
                    taintData = existingTaintData;
                }
                else
                {
                    ctx.WalletTaints.Add(taintData);
                }
                try
                {
                    await ctx.SaveChangesAsync();
                }
                catch (DbUpdateException) // Probably already exists, let's modify instead.
                {
                    ctx.Entry(taintData).State = EntityState.Modified;
                    await ctx.SaveChangesAsync();
                }
            }

            foreach (var existingTaint in walletData.Taints?.ToList() ?? new List<WalletTaintData>())
            {
                if (!walletTransactionInfo.Labels.TryGetValue(existingTaint.LabelId, out var presentLabel) ||
                    existingTaint.TaintId != presentLabel.TaintId ||
                    existingTaint.TaintTypeId != presentLabel.Type)
                {
                    walletData.Taints.Remove(existingTaint);
                }
            }
            await ctx.SaveChangesAsync();
        }

        static JsonSerializerSettings CamelCase = new JsonSerializerSettings() { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() };
    }
}
