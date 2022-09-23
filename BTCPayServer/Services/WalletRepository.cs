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

           var existingWalletTransactions = await context.WalletTransactions
               .Where(data => data.WalletDataId == walletIdStr && txs.Contains(data.TransactionId))
               .ToDictionaryAsync(data => data.TransactionId);  
           var existingWalletScripts = await context.WalletScripts
               .Where(data => data.WalletDataId == walletIdStr && scripts.Contains(data.Script))
               .ToDictionaryAsync(data => data.Script);  
            
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

                labelRecord.WalletScripts??= new List<WalletScriptData>();
                labelRecord.WalletTransactions??= new List<WalletTransactionData>();
                foreach (string script in scripts)
                {
                    if (!existingWalletScripts.TryGetValue(script, out var scriptData))
                    {
                        scriptData = new WalletScriptData() {WalletDataId = walletIdStr, Script = script};
                        await context.WalletScripts.AddAsync(scriptData);
                    }
                    if (labelRecord.WalletScripts.All(data => data.Script != script))
                    {
                        labelRecord.WalletScripts.Add(scriptData);
                    }
                }
                foreach (string tx in txs)
                {
                    if (!existingWalletTransactions.TryGetValue(tx, out var txData))
                    {
                        txData =new WalletTransactionData() {WalletDataId = walletIdStr, TransactionId = tx};
                        await context.WalletTransactions.AddAsync(txData);
                    }
                    if (labelRecord.WalletTransactions.All(data => data.TransactionId != tx))
                    {
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

        public async Task<WalletTransactionListDataResult> GetLabelsForTransactions(WalletId walletId, string[] txs)
        {
            
            var walletIdStr = walletId.ToString();
            await using var context = _ContextFactory.CreateContext();

            var query = context.WalletTransactions
                .Where(transaction =>
                    transaction.WalletDataId == walletIdStr && txs.Contains(transaction.TransactionId))
                .Include(data => data.WalletLabels)
                .Include(data => data.WalletScripts)
                .ThenInclude(data => data.WalletLabels)
                .Select(transaction => new
                {
                    tx = transaction.TransactionId,
                    labels = transaction.WalletLabels,
                    scriptLabels = transaction.WalletScripts.SelectMany(scriptData => scriptData.WalletLabels),
                    blob = transaction.Blob
                });
               
         
            var result = new WalletTransactionListDataResult()
            {
                TransactionComments = query.ToDictionary(data => data.tx, data => data.blob.GetWalletTransactionInfo().Comment),
                TransactionLabels = query.ToDictionary(data => data.tx, data => data.labels.Concat(data.scriptLabels).DistinctBy(labelData => labelData.Label).ToList()),
            };
            return result;

        }  
        public async Task<WalletScriptListDataResult> GetLabelsForScripts(WalletId walletId, string[] scripts)
        {
            
            var walletIdStr = walletId.ToString();
            await using var context = _ContextFactory.CreateContext();

            var query = context.WalletScripts
                .Where(scriptData =>  
                    scriptData.WalletDataId == walletIdStr && scripts.Contains(scriptData.Script))
                .Include(data => data.WalletLabels)
                .Include(data => data.WalletTransactions)
                .ThenInclude(data => data.WalletLabels)
                .Select(scriptData => new
                {
                    script = scriptData.Script,
                    labels = scriptData.WalletLabels,
                    transactionLabels = scriptData.WalletTransactions.SelectMany(transactionData => transactionData.WalletLabels),
                });
            
            var result = new WalletScriptListDataResult()
            {
                ScriptLabels = query.ToDictionary(data => data.script, data => data.labels.Concat(data.transactionLabels).DistinctBy(labelData => labelData.Label).ToList()),
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

        

        public async Task<WalletBlobInfo> GetWalletInfo(WalletId walletId)
        {
            ArgumentNullException.ThrowIfNull(walletId);
            await using var ctx = _ContextFactory.CreateContext();
            var data = await ctx.Wallets
                 .Where(w => w.Id == walletId.ToString())
                 .Select(w => w)
                 .FirstOrDefaultAsync();
            var blob = data?.GetBlobInfo() ?? new WalletBlobInfo();
            return blob;
        }

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

        public async Task<List<WalletLabelData>> GetWalletLabels(WalletId walletId)
        {
                ArgumentNullException.ThrowIfNull(walletId);
                await using var ctx = _ContextFactory.CreateContext();
            
                var walletIdStr = walletId.ToString();
                return await ctx.WalletLabels
                    .Where(data => data.WalletDataId == walletIdStr).ToListAsync();
        }

        public async Task RemoveLabel(WalletId walletId, string[] id,  string[] scripts, string[] txs)
        {
            ArgumentNullException.ThrowIfNull(walletId);
            await using var ctx = _ContextFactory.CreateContext();
            
            var walletIdStr = walletId.ToString();

            var labels = ctx.WalletLabels
                .Where(data => data.WalletDataId == walletIdStr && id.Contains(data.Label))
                .Include(data => data.WalletTransactions)
                .ThenInclude(data => data.WalletScripts)
                .Include(data => data.WalletScripts)
                .ThenInclude(data => data.WalletTransactions);
                

            var txsToRemove = await labels.SelectMany(data =>
                data.WalletTransactions.Where(transactionData => txs.Contains(transactionData.TransactionId) || transactionData.WalletScripts.Any(scriptData => scripts.Contains(scriptData.Script))).ToList()).ToListAsync();
            
            var scriptsToRemove = await labels.SelectMany(data =>
                data.WalletScripts.Where(scriptData => scripts.Contains(scriptData.Script) ||  scriptData.WalletTransactions.Any(transactionData => txs.Contains(transactionData.TransactionId))).ToList()).ToListAsync();


            ctx.WalletScripts.RemoveRange(scriptsToRemove);
            ctx.WalletTransactions.RemoveRange(txsToRemove);

            ctx.WalletLabels.RemoveRange(labels.Where(data =>
                !data.WalletScripts.Any() && !data.WalletTransactions.Any()));
            
            await ctx.SaveChangesAsync();

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

        public async Task AssociateTransactionToScripts(Transaction transactionDataTransaction)
        {
            var txHash = transactionDataTransaction.GetHash().ToString();
            var scripts = transactionDataTransaction.Inputs
                .Select(txIn => txIn.GetSigner().ScriptPubKey.ToString())
                .Concat(transactionDataTransaction.Outputs.Select(txOut =>
                    txOut.ScriptPubKey.ToString())).Distinct().ToArray();
            
            await using var ctx = _ContextFactory.CreateContext();
            var matchedWalletScripts =
                (await ctx.WalletScripts.Where(data => scripts.Contains(data.Script)).ToListAsync()).GroupBy(data =>
                    data.WalletDataId);
            var walletids = matchedWalletScripts.Select(datas => datas.Key).ToList();
            var matchedWalletTransactions = await ctx.WalletTransactions
                .Where(data => walletids.Contains(data.WalletDataId) && data.TransactionId == txHash).Include(data => data.WalletScripts).ToDictionaryAsync(data => data.WalletDataId);
            foreach (var walletScriptSet in matchedWalletScripts)
            {
                if (!matchedWalletTransactions.TryGetValue(walletScriptSet.Key, out var walletTransaction))
                {
                    walletTransaction = new WalletTransactionData()
                    {
                        WalletDataId = walletScriptSet.Key, TransactionId = txHash,
                        WalletScripts = new ()
                    };
                    await ctx.WalletTransactions.AddAsync(walletTransaction);
                }
                foreach (var walletScript in walletScriptSet)
                {
                    walletTransaction.WalletScripts.Add(walletScript);
                }
            }

            await ctx.SaveChangesAsync();


        }

        public async Task<string[]> GetTransactionsWithLabel(WalletId walletId,string label)
        {
            
            ArgumentNullException.ThrowIfNull(walletId);
            await using var ctx = _ContextFactory.CreateContext();
            
            var walletIdStr = walletId.ToString();
            var baseQuery = ctx.WalletLabels
                .Where(data => data.WalletDataId == walletIdStr && data.Label.StartsWith(label));

            var txs = baseQuery.Include(data => data.WalletTransactions).SelectMany(data =>
                data.WalletTransactions.Select(transactionData => transactionData.TransactionId)); 
            
            var secondLeveltxs = baseQuery.Include(data => data.WalletScripts).ThenInclude(data => data.WalletTransactions).SelectMany(data =>
                data.WalletScripts.SelectMany(scriptData =>  scriptData.WalletTransactions.Select(transactionData => transactionData.TransactionId)));

            return await txs.Concat(secondLeveltxs).Distinct().ToArrayAsync();
        }
    }
}
