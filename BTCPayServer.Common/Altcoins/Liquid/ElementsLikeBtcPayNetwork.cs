using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBXplorer;
using NBXplorer.Models;

namespace BTCPayServer
{
    public class ElementsBTCPayNetwork:BTCPayNetwork
    {
        public string NetworkCryptoCode { get; set; }
        public uint256 AssetId { get; set; }
        public override bool WalletSupported { get; set; } = false;
        public int Divisibility { get; set; } = 8;

        public override IEnumerable<(MatchedOutput matchedOutput, OutPoint outPoint)> GetValidOutputs(NewTransactionEvent evtOutputs)
        {
            return  evtOutputs.Outputs.Where(output =>
                output.Value is AssetMoney assetMoney && assetMoney.AssetId == AssetId).Select(output =>
            {
                var outpoint = new OutPoint(evtOutputs.TransactionData.TransactionHash, output.Index);
                return (output, outpoint);
            });
        }
    }
}
