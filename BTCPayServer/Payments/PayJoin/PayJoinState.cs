using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Services.Wallets;
using NBitcoin;

namespace BTCPayServer.Payments.PayJoin
{
    public class PayJoinState
    {
        //keep track of all transactions sent to us via this protocol
        private readonly ConcurrentDictionary<string, PayJoinStateRecordedItem> RecordedTransactions =
            new ConcurrentDictionary<string, PayJoinStateRecordedItem>();

        //utxos that have been exposed but the original tx was broadcasted instead.
        private readonly ConcurrentDictionary<string, ReceivedCoin> ExposedCoins;

        public PayJoinState(ConcurrentDictionary<string, ReceivedCoin> exposedCoins = null)
        {
            ExposedCoins = exposedCoins ?? new ConcurrentDictionary<string, ReceivedCoin>();
        }

        public IEnumerable<PayJoinStateRecordedItem> GetRecords()
        {
            return RecordedTransactions.Values;
        }

        public IEnumerable<PayJoinStateRecordedItem> GetStaleRecords(TimeSpan cutoff)
        {
            return GetRecords().Where(pair =>
                DateTimeOffset.Now.Subtract(pair.Timestamp).TotalMilliseconds >=
                cutoff.TotalMilliseconds);
        }

        public enum TransactionValidityResult
        {
            Valid_ExactMatch,
            Invalid_PartialMatch, 
            Valid_NoMatch,
            Invalid_Inputs_Seen,
            Valid_SameInputs
        }

        public TransactionValidityResult CheckIfTransactionValid(Transaction transaction, string invoiceId)
        {
            if (RecordedTransactions.ContainsKey($"{invoiceId}_{transaction.GetHash()}"))
            {
                return TransactionValidityResult.Valid_ExactMatch;
            }

            var hashes = transaction.Inputs.Select(txIn => txIn.PrevOut.ToString());

            var matches = RecordedTransactions.Where(record =>
                record.Key.Contains(invoiceId, StringComparison.InvariantCultureIgnoreCase) ||
                record.Key.Contains(transaction.GetHash().ToString(), StringComparison.InvariantCultureIgnoreCase));

            if (matches.Any())
            {
                if(matches.Any(record => 
                    record.Key.Contains(invoiceId, StringComparison.InvariantCultureIgnoreCase) &&
                    record.Value.Transaction.RBF &&
                    record.Value.Transaction.Inputs.All(recordTxIn => hashes.Contains(recordTxIn.PrevOut.ToString()))))
                {
                    return TransactionValidityResult.Valid_SameInputs;
                }
                
                return TransactionValidityResult.Invalid_PartialMatch;
            }

            return RecordedTransactions.Any(record =>
                record.Value.Transaction.Inputs.Any(txIn => hashes.Contains(txIn.PrevOut.ToString())))
                ? TransactionValidityResult.Invalid_Inputs_Seen: TransactionValidityResult.Valid_NoMatch;
        }

        public void AddRecord(PayJoinStateRecordedItem recordedItem)
        {
            RecordedTransactions.TryAdd(recordedItem.ToString(), recordedItem);
            foreach (var receivedCoin in recordedItem.CoinsExposed)
            {
                ExposedCoins.TryRemove(receivedCoin.OutPoint.ToString(), out _);
            }
        }

        public void RemoveRecord(PayJoinStateRecordedItem item, bool keepExposed)
        {
            if (keepExposed)
            {
                foreach (var receivedCoin in item.CoinsExposed)
                {
                    ExposedCoins.AddOrReplace(receivedCoin.OutPoint.ToString(), receivedCoin);
                }
            }

            RecordedTransactions.TryRemove(item.ToString(), out _);
        }

        public void RemoveRecord(uint256 proposedTxHash)
        {
            var id = RecordedTransactions.SingleOrDefault(pair =>
                pair.Value.ProposedTransactionHash == proposedTxHash ||
                pair.Value.OriginalTransactionHash == proposedTxHash).Key;
            if (id != null)
            {
                RecordedTransactions.TryRemove(id, out _);
            }
        }
        
        public void RemoveRecord(string invoiceId)
        {
            var id = RecordedTransactions.Single(pair =>
                pair.Value.InvoiceId == invoiceId).Key;
            RecordedTransactions.TryRemove(id, out _);
        }

        public List<ReceivedCoin> GetExposed(Transaction transaction)
        {
            return RecordedTransactions.Values
                .Where(pair =>
                    pair.Transaction.Inputs.Any(txIn =>
                        transaction.Inputs.Any(txIn2 => txIn.PrevOut == txIn2.PrevOut)))
                .SelectMany(pair => pair.CoinsExposed).ToList();
        }

        public bool TryGetWithProposedHash(uint256 hash, out PayJoinStateRecordedItem item)
        {
            item =
                RecordedTransactions.Values.SingleOrDefault(
                    recordedItem => recordedItem.ProposedTransactionHash == hash);
            return item != null;
        }

        public IEnumerable<ReceivedCoin> GetExposedCoins(bool includeOnesInOngoingBPUs = false)
        {
            var result = ExposedCoins.Values;
            return includeOnesInOngoingBPUs
                ? result.Concat(RecordedTransactions.Values.SelectMany(item => item.CoinsExposed))
                : result;
        }

        public void PruneExposedButSpentCoins(IEnumerable<ReceivedCoin> stillAvailable)
        {
            var keys = stillAvailable.Select(coin => coin.OutPoint.ToString());
            var keysToRemove = ExposedCoins.Keys.Where(s => !keys.Contains(s));
            foreach (var key in keysToRemove)
            {
                ExposedCoins.TryRemove(key, out _);
            }
        }


        public void PruneExposedBySpentCoins(IEnumerable<OutPoint> taken)
        {
            var keys = taken.Select(coin => coin.ToString());
            var keysToRemove = ExposedCoins.Keys.Where(s => keys.Contains(s));
            foreach (var key in keysToRemove)
            {
                ExposedCoins.TryRemove(key, out _);
            }
        }

        public void PruneRecordsOfUsedInputs(TxInList transactionInputs)
        {
            foreach (PayJoinStateRecordedItem payJoinStateRecordedItem in RecordedTransactions.Values)
            {
                if (payJoinStateRecordedItem.CoinsExposed.Any(coin =>
                    transactionInputs.Any(txin => txin.PrevOut == coin.OutPoint)))
                {
                    RemoveRecord(payJoinStateRecordedItem, true);
                }
            }

            PruneExposedBySpentCoins(transactionInputs.Select(coin => coin.PrevOut));
        }
    }
}
