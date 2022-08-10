using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Wallets;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayServer.Services
{

    public class WalletRepository
    {
        public static Dictionary<string, string> DefaultLabelColors = new Dictionary<string, string>()
        {
            {"payjoin", "#51b13e"},
            {"invoice", "#cedc21"},
            {"payment-request", "#489D77"},
            {"app", "#5093B6"},
            {"pj-exposed", "#51b13e"},
            {"payout", "#3F88AF"}
        };

        private readonly ApplicationDbContextFactory _ContextFactory;

        public WalletRepository(ApplicationDbContextFactory contextFactory)
        {
            _ContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public async Task SetWalletInfo(WalletId walletId, WalletBlobInfo blob)
        {
            ArgumentNullException.ThrowIfNull(walletId);
            using var ctx = _ContextFactory.CreateContext();
            var walletData = new WalletData() { Id = walletId.ToString() };
            walletData.SetBlobInfo(blob);
            var entity = await ctx.Wallets.AddAsync(walletData);
            entity.State = EntityState.Modified;
            try
            {
                await ctx.SaveChangesAsync();
            }
            catch (DbUpdateException) // Does not exists
            {
                entity.State = EntityState.Added;
                await ctx.SaveChangesAsync();
            }
        }

        // public async Task<Dictionary<string, WalletTransactionInfo>> GetWalletTransactionsInfo(WalletId walletId, string[] transactionIds = null)
        // {
        //     ArgumentNullException.ThrowIfNull(walletId);
        //     using var ctx = _ContextFactory.CreateContext();
        //     return (await ctx.WalletTransactions
        //                     .Where(w => w.WalletDataId == walletId.ToString())
        //                     .Where(data => transactionIds == null || transactionIds.Contains(data.TransactionId))
        //                     .Select(w => w)
        //                     .ToArrayAsync())
        //                     .ToDictionary(w => w.TransactionId, w => w.GetBlobInfo());
        // }

        public async Task AddLabels(WalletId walletId, Label[] labels, string[] scripts, string[] txs)
        {
            var walletIdStr = walletId.ToString();
            scripts ??= Array.Empty<string>();
            txs ??= Array.Empty<string>();
            await using var context = _ContextFactory.CreateContext();
           var exist =  await context.Wallets.FindAsync(walletId.ToString());
           if (exist is null)
           {
               var wd = new WalletData() {Id = walletId.ToString()};
               wd.SetBlobInfo(new WalletBlobInfo());
               await context.Wallets.AddAsync(wd);
           }
            
            var labelToAdd = labels.ToDictionary(label => label.Id);
            var labelIds = labelToAdd.Keys.ToArray();
            var existingLabels = await 
                context.WalletLabels
                    .Include(data => data.WalletScripts)
                    .Include(data => data.WalletTransactions)
                    .Where(data => data.WalletDataId == walletIdStr && labelIds.Contains(data.Label)).ToDictionaryAsync(data => data.Label);
            foreach (var label in labels)
            {
                if (!existingLabels.TryGetValue(label.Id, out var labelRecord))
                {
                    labelRecord = new WalletLabelData()
                    {
                        WalletDataId = walletIdStr, Label = label.Id, Data = label.SetLabelData()
                    };
                    await context.WalletLabels.AddAsync(labelRecord);
                }

                labelRecord.WalletScripts = new List<WalletScriptData>();
                labelRecord.WalletTransactions = new List<WalletTransactionData>();
                foreach (string script in scripts)
                {
                    if (labelRecord.WalletScripts.All(data => data.Script != script) is true)
                    {
                        var scriptData = new WalletScriptData() {WalletDataId = walletIdStr, Script = script};
                        labelRecord.WalletScripts.Add(scriptData);
                    }
                }

                foreach (string tx in txs)
                {
                    if (labelRecord.WalletTransactions.All(data => data.TransactionId != tx) is true)
                    {
                        var txData = new WalletTransactionData() {WalletDataId = walletIdStr, TransactionId = tx};
                        labelRecord.WalletTransactions.Add(txData);
                    }
                }
            }
            await context.SaveChangesAsync();

        }
        
        public class WalletTransactionListDataResult
        {
            public Dictionary<string, string> TransactionComments { get; set; }
            public Dictionary<string, List<WalletLabelData>> TransactionLabels { get; set; }
        }

        public async Task<WalletTransactionListDataResult> GetLabelsForTransactions(WalletId walletId, Dictionary<string, string[]> transactionsAndAllTheirInsAndOuts)
        {
            
            var walletIdStr = walletId.ToString();
            var txs = transactionsAndAllTheirInsAndOuts.Keys.ToArray();
            var scripts = transactionsAndAllTheirInsAndOuts.Values.SelectMany(strings => strings).Distinct().ToArray();
            await using var context = _ContextFactory.CreateContext();
            var data = context.WalletLabels
                .Include(data => data.WalletTransactions)
                .Include(data => data.WalletScripts)
                .Where(data => data.WalletDataId == walletIdStr)
                .Where(data =>
                    data.WalletTransactions.Any(transactionData => txs.Contains(transactionData.TransactionId)) ||
                    data.WalletScripts.Any(scriptData => scripts.Contains(scriptData.Script))).ToList();

            var txLabels = new Dictionary<string, List<WalletLabelData>>();
            foreach (var tx in transactionsAndAllTheirInsAndOuts)
            {
                txLabels.Add(tx.Key, data.Where(labelData => labelData.WalletScripts.Any(scriptData =>
                                                                 tx.Value.Contains(scriptData.Script)) ||
                                                         labelData.WalletTransactions.Any(transactionData =>
                                                             tx.Key ==
                                                             transactionData.TransactionId)).ToList());
            }
            
            var result = new WalletTransactionListDataResult()
            {
                TransactionComments = data.SelectMany(data => data.WalletTransactions).DistinctBy(data => data.TransactionId).ToDictionary(data => data.TransactionId, data => data.GetBlobInfo().Comment),
                TransactionLabels = txLabels
            };
            return result;

        }
        
        public class WalletScriptListDataResult
        {
            public Dictionary<string, List<WalletLabelData>> ScriptLabels { get; set; }
        }
        
        
        public class WalletUTXOListDataResult
        {
            public Dictionary<string, string>? TransactionComments { get; set; }
            public Dictionary<ReceivedCoin, List<WalletLabelData>> UTXOLabels { get; set; }
        }
        
         

        public async Task<WalletUTXOListDataResult> GetLabelsForUTXOs(WalletId walletId, ReceivedCoin[] utxos)
        {
            var walletIdStr = walletId.ToString();
            var scripts = utxos.Select(coin => coin.ScriptPubKey.ToString()).Distinct();
            var txs = utxos.Select(coin => coin.OutPoint.Hash.ToString()).Distinct();    
            
            await using var context = _ContextFactory.CreateContext();
            var data = context.WalletLabels
                .Include(data => data.WalletTransactions)
                .Include(data => data.WalletScripts)
                .Where(data => data.WalletDataId == walletIdStr)
                .Where(data =>
                    data.WalletTransactions.Any(transactionData => txs.Contains(transactionData.TransactionId)) ||
                    data.WalletScripts.Any(scriptData => scripts.Contains(scriptData.Script))).ToList();

            Dictionary<ReceivedCoin, List<WalletLabelData>> coinToLabel = new();
            foreach (var utxo in utxos)
            {
                coinToLabel.Add(utxo, data.Where(labelData => labelData.WalletScripts.Any(scriptData =>
                                                                  scriptData.Script == utxo.ScriptPubKey.ToString()) ||
                                                              labelData.WalletTransactions.Any(transactionData =>
                                                                  utxo.OutPoint.Hash.ToString() ==
                                                                  transactionData.TransactionId)).ToList());
            }
            
            return new WalletUTXOListDataResult()
            {
                
                TransactionComments = data.SelectMany(data => data.WalletTransactions)
                    .DistinctBy(data => data.TransactionId)
                    .ToDictionary(data => data.TransactionId, data => data.GetBlobInfo().Comment),
                UTXOLabels = coinToLabel
            };

        }


        public async Task<WalletScriptListDataResult> GetLabelsForScripts(WalletId walletId, string[] scripts)
        {
            await using var context = _ContextFactory.CreateContext();
            var query = context.WalletScripts.Include(data => data.WalletLabels);
          
            var scriptsRes = await query
                .Where(w => w.WalletDataId == walletId.ToString())
                .Where(data => scripts == null || scripts.Contains(data.Script))
                .AsSplitQuery()
                .ToArrayAsync();
            var result = new WalletScriptListDataResult()
            {
                ScriptLabels = scriptsRes.ToDictionary(data => data.Script, data =>  data.WalletLabels.DistinctBy(labelData => labelData.Label).ToList())
            };
            return result;
        }
        

        public async Task<WalletBlobInfo> GetWalletInfo(WalletId walletId)
        {
            ArgumentNullException.ThrowIfNull(walletId);
            await using var ctx = _ContextFactory.CreateContext();
            var data = await ctx.Wallets
                 .Where(w => w.Id == walletId.ToString())
                 .Select(w => w)
                 .FirstOrDefaultAsync();
            var blob = data?.GetBlobInfo() ?? new WalletBlobInfo();
            DefaultLabelColors.ToList().ForEach(x => blob.LabelColors.TryAdd(x.Key, x.Value));
            return blob;
        }

        // public async Task SetWalletTransactionInfo(WalletId walletId, string transactionId, WalletTransactionInfo walletTransactionInfo)
        // {
        //     ArgumentNullException.ThrowIfNull(walletId);
        //     ArgumentNullException.ThrowIfNull(transactionId);
        //     using var ctx = _ContextFactory.CreateContext();
        //     var walletData = new WalletTransactionData() { WalletDataId = walletId.ToString(), TransactionId = transactionId };
        //     walletData.SetBlobInfo(walletTransactionInfo);
        //     var entity = await ctx.WalletTransactions.AddAsync(walletData);
        //     entity.State = EntityState.Modified;
        //     try
        //     {
        //         await ctx.SaveChangesAsync();
        //     }
        //     catch (DbUpdateException) // Does not exists
        //     {
        //         entity.State = EntityState.Added;
        //         try
        //         {
        //             await ctx.SaveChangesAsync();
        //         }
        //         catch (DbUpdateException) // the Wallet does not exists in the DB
        //         {
        //             await SetWalletInfo(walletId, new WalletBlobInfo());
        //             await ctx.SaveChangesAsync();
        //         }
        //     }
        // }

        public async Task UpdateTransactionComment(WalletId walletId, string txId, string requestComment)
        {
            ArgumentNullException.ThrowIfNull(walletId);
            await using var ctx = _ContextFactory.CreateContext();
            var existing =
                await ctx.WalletTransactions.FindAsync(new {WalletDataId = walletId.ToString(), TransactionId = txId});
            if (existing is null)
            {
                existing = new() {WalletDataId = walletId.ToString(), TransactionId = txId};
                await ctx.WalletTransactions.AddAsync(existing);
            }
            var blob = existing.GetBlobInfo();
            blob.Comment = requestComment;
            existing.SetBlobInfo(blob);
            await ctx.SaveChangesAsync();
        }
        
        public async Task RemoveLabel(WalletId walletId, string[] id,string[] scripts, string[] txs )
        {
            ArgumentNullException.ThrowIfNull(walletId);
            await using var ctx = _ContextFactory.CreateContext();
            var existing =
                await ctx.WalletLabels
                    .Include(data => data.WalletTransactions)
                    .Include(data => data.WalletScripts)
                    .Where(data => data.WalletDataId ==  walletId.ToString() &&  id.Contains(data.Label)).ToListAsync();
            if (existing.Any())
            {
                foreach (WalletLabelData data in existing)
                {
                    if (scripts?.Any() is true)
                    {
                        var removedScripts = 
                            data.WalletScripts.Where(data => scripts.Contains(data.Script));
                    
                        ctx.RemoveRange(removedScripts);
                        data.WalletScripts.RemoveAll(data => scripts.Contains(data.Script));
                    }
                    if (txs?.Any() is true)
                    {
                        var removedTxs = 
                            data.WalletTransactions.Where(data => txs.Contains(data.TransactionId));
                    
                        ctx.RemoveRange(removedTxs);
                        data.WalletTransactions.RemoveAll(data => txs.Contains(data.TransactionId));
                    }

                    if (!data.WalletScripts.Any() && !data.WalletTransactions.Any())
                    {
                    
                        ctx.WalletLabels.Remove(data);
                    }
                }
                
            }
            await ctx.SaveChangesAsync();
        }
        
        
        
        public static  Dictionary<string, string[]> GetLabelFilter(IEnumerable<TransactionHistoryLine> transactionHistoryLines)
        {
            var result = new Dictionary<string, string[]>();
            foreach (var transactionHistoryLine in transactionHistoryLines)
            {
                var scripts =
                    transactionHistoryLine.Transaction is null
                        ? Array.Empty<string>()
                        : transactionHistoryLine.Transaction.Inputs
                            .Select(txIn => txIn.GetSigner().ScriptPubKey.ToString())
                            .Concat(transactionHistoryLine.Transaction.Outputs.Select(txOut =>
                                txOut.ScriptPubKey.ToString())).Distinct().ToArray();
                
                result.Add(transactionHistoryLine.TransactionId.ToString(),  scripts);
            }
            return result;
        }


        public static List<TransactionHistoryLine> Filter(List<TransactionHistoryLine> txs,
            Dictionary<string, List<WalletLabelData>> labels, TransactionStatus[] statusFilter = null, string labelFilter = null)
        {
            var result = new List<TransactionHistoryLine>();

            foreach (TransactionHistoryLine t in txs)
            {

                if (!string.IsNullOrWhiteSpace(labelFilter))
                {
                    labels.TryGetValue(t.TransactionId.ToString(),
                        out var txLabels);

                    if (txLabels?.Any(data =>
                        {
                            var l = data.GetLabel();
                            return labelFilter == l.Type || labelFilter == l.Text;
                        }) is true)
                        result.Add(t);
                }

                if (statusFilter?.Any() is true)
                {
                    if (statusFilter.Contains(TransactionStatus.Confirmed) && t.Confirmations != 0)
                        result.Add(t);
                    else if (statusFilter.Contains(TransactionStatus.Unconfirmed) && t.Confirmations == 0)
                        result.Add(t);
                }

            }

            return result;

        }

    }
}
