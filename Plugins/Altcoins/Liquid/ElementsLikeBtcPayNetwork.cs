using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Common;
using NBitcoin;
using NBXplorer;
using NBXplorer.Models;

namespace BTCPayServer.Plugins.Altcoins;

public class ElementsBTCPayNetwork : BTCPayNetwork
{
    public string NetworkCryptoCode { get; set; }
    public uint256 AssetId { get; set; }
    public override bool ReadonlyWallet { get; set; } = true;
    public bool IsNativeAsset => NetworkCryptoCode == CryptoCode;

    public override IEnumerable<(MatchedOutput matchedOutput, OutPoint outPoint)> GetValidOutputs(
        NewTransactionEvent evtOutputs)
    {
        return evtOutputs.Outputs.Where(output =>
            output.Value is AssetMoney assetMoney && assetMoney.AssetId == AssetId).Select(output =>
        {
            var outpoint = new OutPoint(evtOutputs.TransactionData.TransactionHash, output.Index);
            return (output, outpoint);
        });
    }

    public override List<TransactionInformation> FilterValidTransactions(List<TransactionInformation> transactionInformationSet)
    {
        return transactionInformationSet.FindAll(information =>
            information.Outputs.Any(output =>
                output.Value is AssetMoney assetMoney && assetMoney.AssetId == AssetId) ||
            information.Inputs.Any(output =>
                output.Value is AssetMoney assetMoney && assetMoney.AssetId == AssetId));
    }

    public override PaymentUrlBuilder GenerateBIP21(string cryptoInfoAddress, decimal? cryptoInfoDue)
    {
        //precision 0: 10 = 0.00000010
        //precision 2: 10 = 0.00001000
        //precision 8: 10 = 10
        var money = cryptoInfoDue / (decimal)Math.Pow(10, 8 - Divisibility);
        var builder = base.GenerateBIP21(cryptoInfoAddress, money);
        builder.QueryParams.Add("assetid", AssetId.ToString());
        return builder;
    }
}
