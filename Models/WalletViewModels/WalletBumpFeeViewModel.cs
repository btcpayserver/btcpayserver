using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using NBitcoin;
using static BTCPayServer.Models.WalletViewModels.WalletBumpFeeViewModel;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletBumpFeeViewModel
    {
        public string ReturnUrl { get; set; }
        [Display(Name = "Transaction Id")]
        public uint256 TransactionId { get; set; }
        public List<SelectListItem> BumpFeeMethods { get; set; } = new();
        public string[] Outpoints { get; set; }
        public string[] TransactionHashes { get; set; }
        public List<WalletSendModel.FeeRateOption> RecommendedSatoshiPerByte { get; set; }
        [Display]
        public decimal? FeeSatoshiPerByte { get; set; }
        [Display]
        public decimal? CurrentFeeSatoshiPerByte { get; set; }
        public bool IsMultiSigOnServer { get; set; }
        [Display(Name = "Fee bump method")]
        public string BumpMethod { get; set; }

        public string Command { get; set; }
#nullable enable
        public record BumpTarget(HashSet<OutPoint> Outpoints, HashSet<uint256> TxIds)
        {

            public uint256? GetSingleTransactionId()
                => this switch
                {
                    { TxIds: { Count: 1 } ids, Outpoints: { Count: 0 } } => ids.First(),
                    { TxIds: { Count: 0 }, Outpoints: { Count: 1 } outpoints } => outpoints.First().Hash,
                    _ => null
                };
            public HashSet<uint256> GetTransactionIds()
            => (TxIds.Concat(Outpoints.Select(o => o.Hash))).ToHashSet();

            public BumpTarget Filter(HashSet<uint256> elligibleTxs)
            => new BumpTarget(
                    Outpoints.Where(o => elligibleTxs.Contains(o.Hash)).ToHashSet(),
                    TxIds.Where(t => elligibleTxs.Contains(t)).ToHashSet());

            public List<OutPoint> GetMatchedOutpoints(IEnumerable<OutPoint> outpoints)
            {
                List<OutPoint> matches = new();
                HashSet<uint256> bumpedTxs = new();
                foreach (var outpoint in outpoints)
                {
                    if (Outpoints.Contains(outpoint))
                    {
                        matches.Add(outpoint);
                        bumpedTxs.Add(outpoint.Hash);
                    }
                    else if (TxIds.Contains(outpoint.Hash) && bumpedTxs.Add(outpoint.Hash))
                    {
                        matches.Add(outpoint);
                    }
                }
                return matches;
            }
        }

        public BumpTarget GetBumpTarget()
        {
            if (TransactionId is not null)
                return new BumpTarget(new(), new([TransactionId]));
            HashSet<OutPoint> outpoints = new();
            HashSet<uint256> txids = new();
            foreach (var o in Outpoints ?? [])
            {
                try
                {
                    outpoints.Add(OutPoint.Parse(o));
                }
                catch { }
            }
            foreach (var o in TransactionHashes ?? [])
            {
                try
                {
                    txids.Add(uint256.Parse(o));
                }
                catch { }
            }
            return new BumpTarget(outpoints, txids);
        }
#nullable restore
    }
}
