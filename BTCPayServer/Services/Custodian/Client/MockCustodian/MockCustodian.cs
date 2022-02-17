using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Custodian.Client.Exception;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Custodian.Client.MockCustodian;

public class MockCustodian : ICustodian, ICanDeposit, ICanTrade, ICanWithdraw
{
    public const string DepositAddress = "bc1qxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";
    public const string TradeId = "TRADE-ID-001";
    public const string TradeFromAsset = "EUR";
    public const string TradeToAsset = "BTC";
    public static readonly decimal TradeQtyBought = new decimal(1);
    public static readonly decimal TradeFeeEuro = new decimal(12.5);
    public static readonly decimal BtcPriceInEuro = new decimal(30000);
    public const string WithdrawalPaymentMethod = "BTC-OnChain";
    public const string WithdrawalAsset = "BTC";
    public const string WithdrawalId = "WITHDRAWAL-ID-001";
    public static readonly decimal WithdrawalAmount = new decimal(0.5);
    public static readonly decimal WithdrawalFee = new decimal(0.0005);
    public const string WithdrawalTransactionId = "yyy";
    public const string WithdrawalTargetAddress = "bc1qyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyy";
    
    
    public string GetCode()
    {
        return "mock";
    }

    public string GetName()
    {
        return "Mock";
    }

    public Task<Dictionary<string, decimal>> GetAssetBalancesAsync(JObject config, CancellationToken cancellationToken)
    {
        var r = new Dictionary<string, decimal>()
        {
            { "BTC", new decimal(1.23456) }, { "LTC", new decimal(50.123456) }, { "USD", new decimal(1500.55) }, { "EUR", new decimal(1235.15) },
        };
        return Task.FromResult(r);
    }

    public Task<DepositAddressData> GetDepositAddressAsync(string paymentMethod, JObject config, CancellationToken cancellationToken)
    {
        if (paymentMethod.Equals("BTC-OnChain"))
        {
            var r = new DepositAddressData();
            r.Address = DepositAddress;
            return Task.FromResult(r);
        }

        throw new CustodianFeatureNotImplementedException($"Only BTC-OnChain is implemented for {this.GetName()}");
    }

    public string[] GetDepositablePaymentMethods()
    {
        return new[] { "BTC-OnChain" };
    }

    public List<AssetPairData> GetTradableAssetPairs()
    {
        var r = new List<AssetPairData>();
        r.Add(new AssetPairData("BTC", "EUR"));
        return r;
    }

    private MarketTradeResult getMarketTradeResult()
    {
        var ledgerEntries = new List<LedgerEntryData>();
        ledgerEntries.Add(new LedgerEntryData("BTC", TradeQtyBought, LedgerEntryData.LedgerEntryType.Trade));
        ledgerEntries.Add(new LedgerEntryData("EUR", -1 * TradeQtyBought * BtcPriceInEuro, LedgerEntryData.LedgerEntryType.Trade));
        ledgerEntries.Add(new LedgerEntryData("EUR", -1 * TradeFeeEuro, LedgerEntryData.LedgerEntryType.Fee));
        return new MarketTradeResult(TradeFromAsset, TradeToAsset, ledgerEntries, TradeId);
    }

    public Task<MarketTradeResult> TradeMarketAsync(string fromAsset, string toAsset, decimal qty, JObject config, CancellationToken cancellationToken)
    {
        if (fromAsset != "EUR" && toAsset != "BTC")
        {
            throw new WrongTradingPairException(fromAsset, toAsset);
        }

        if (qty != TradeQtyBought)
        {
            throw new InsufficientFundsException($"With {GetName()}, you can only buy {TradeQtyBought} {TradeToAsset} with {TradeFromAsset} and nothing else.");
        }

        return Task.FromResult(getMarketTradeResult());
    }

    public Task<MarketTradeResult> GetTradeInfoAsync(string tradeId, JObject config, CancellationToken cancellationToken)
    {
        if (tradeId == TradeId)
        {
            return Task.FromResult(getMarketTradeResult());
        }

        return Task.FromResult<>(null);
    }

    public Task<AssetQuoteResult> GetQuoteForAssetAsync(string fromAsset, string toAsset, JObject config, CancellationToken cancellationToken)
    {
        if (fromAsset == TradeFromAsset && toAsset == TradeToAsset)
        {
            return Task.FromResult(new AssetQuoteResult(TradeFromAsset, TradeToAsset, BtcPriceInEuro, BtcPriceInEuro));
        }

        throw new WrongTradingPairException(fromAsset, toAsset);
        //throw new AssetQuoteUnavailableException(pair);
    }

    private WithdrawResult CreateWithdrawResult()
    {
        var ledgerEntries = new List<LedgerEntryData>();
        ledgerEntries.Add(new LedgerEntryData(WithdrawalAsset, WithdrawalAmount - WithdrawalFee, LedgerEntryData.LedgerEntryType.Withdrawal));
        ledgerEntries.Add(new LedgerEntryData(WithdrawalAsset, WithdrawalFee, LedgerEntryData.LedgerEntryType.Fee));
        var r = new WithdrawResult(WithdrawalPaymentMethod, WithdrawalAsset, ledgerEntries, WithdrawalId, WithdrawResultData.WithdrawalStatus.Queued, WithdrawalTargetAddress, WithdrawalTransactionId);
        return r;
    }

    public Task<WithdrawResult> WithdrawAsync(string paymentMethod, decimal amount, JObject config, CancellationToken cancellationToken)
    {
        if (paymentMethod == WithdrawalPaymentMethod)
        {
            if (amount == WithdrawalAmount)
            {
                return Task.FromResult(CreateWithdrawResult());
            }

            throw new InsufficientFundsException($"{GetName()} only supports withdrawals of {WithdrawalAmount}");
        }

        throw new CannotWithdrawException(this, paymentMethod, $"Only {WithdrawalPaymentMethod} can be withdrawn from {GetName()}");
    }

    public Task<WithdrawResult> GetWithdrawalInfoAsync(string paymentMethod, string withdrawalId, JObject config, CancellationToken cancellationToken)
    {
        if (withdrawalId == WithdrawalId && paymentMethod == WithdrawalPaymentMethod)
        {
            return Task.FromResult(CreateWithdrawResult());
        }

        return Task.FromResult<>(null);
    }

    public string[] GetWithdrawablePaymentMethods()
    {
        return GetDepositablePaymentMethods();
    }
}
