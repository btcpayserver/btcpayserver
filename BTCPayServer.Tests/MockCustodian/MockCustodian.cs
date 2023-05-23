using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Custodians;
using BTCPayServer.Abstractions.Custodians.Client;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Client.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Custodian.Client.MockCustodian;

public class MockCustodian : ICustodian, ICanDeposit, ICanTrade, ICanWithdraw
{
    public const string DepositPaymentMethod = "BTC-OnChain";
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
    public static readonly string WithdrawalAmountPercentage = "12.5%";
    public static readonly decimal WithdrawalMinAmount = new decimal(0.001);
    public static readonly decimal WithdrawalMaxAmount = new decimal(0.6);
    public static readonly decimal WithdrawalFee = new decimal(0.0005);
    public const string WithdrawalTransactionId = "yyy";
    public const string WithdrawalTargetAddress = "bc1qyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyy";
    public const WithdrawalResponseData.WithdrawalStatus WithdrawalStatus = WithdrawalResponseData.WithdrawalStatus.Queued;
    public static readonly decimal BalanceBTC = new decimal(1.23456);
    public static readonly decimal BalanceLTC = new decimal(50.123456);
    public static readonly decimal BalanceUSD = new decimal(1500.55);
    public static readonly decimal BalanceEUR = new decimal(1235.15);

    public string Code
    {
        get => "mock";
    }

    public string Name
    {
        get => "MOCK Exchange";
    }

    public Task<Dictionary<string, decimal>> GetAssetBalancesAsync(JObject config, CancellationToken cancellationToken)
    {
        var r = new Dictionary<string, decimal>()
        {
            { "BTC", BalanceBTC }, { "LTC", BalanceLTC }, { "USD", BalanceUSD }, { "EUR", BalanceEUR },
        };
        return Task.FromResult(r);
    }

    public Task<Form> GetConfigForm(JObject config, CancellationToken cancellationToken = default)
    {
        return null;
    }

    public Task<DepositAddressData> GetDepositAddressAsync(string paymentMethod, JObject config, CancellationToken cancellationToken)
    {
        if (paymentMethod.Equals(DepositPaymentMethod))
        {
            var r = new DepositAddressData();
            r.Address = DepositAddress;
            return Task.FromResult(r);
        }

        throw new CustodianFeatureNotImplementedException($"Only BTC-OnChain is implemented for {this.Name}");
    }

    public string[] GetDepositablePaymentMethods()
    {
        return new[] { "BTC-OnChain" };
    }

    public List<AssetPairData> GetTradableAssetPairs()
    {
        var r = new List<AssetPairData>();
        r.Add(new AssetPairData("BTC", "EUR", (decimal)0.0001));
        return r;
    }

    private MarketTradeResult GetMarketTradeResult()
    {
        var ledgerEntries = new List<LedgerEntryData>();
        ledgerEntries.Add(new LedgerEntryData("BTC", TradeQtyBought, LedgerEntryData.LedgerEntryType.Trade));
        ledgerEntries.Add(new LedgerEntryData("EUR", -1 * TradeQtyBought * BtcPriceInEuro, LedgerEntryData.LedgerEntryType.Trade));
        ledgerEntries.Add(new LedgerEntryData("EUR", -1 * TradeFeeEuro, LedgerEntryData.LedgerEntryType.Fee));
        return new MarketTradeResult(TradeFromAsset, TradeToAsset, ledgerEntries, TradeId);
    }

    public Task<MarketTradeResult> TradeMarketAsync(string fromAsset, string toAsset, decimal qty, JObject config, CancellationToken cancellationToken)
    {
        if (!fromAsset.Equals("EUR") || !toAsset.Equals("BTC"))
        {
            throw new WrongTradingPairException(fromAsset, toAsset);
        }

        if (qty != TradeQtyBought)
        {
            throw new InsufficientFundsException($"With {Name}, you can only buy {TradeQtyBought} {TradeToAsset} with {TradeFromAsset} and nothing else.");
        }

        return Task.FromResult(GetMarketTradeResult());
    }

    public Task<MarketTradeResult> GetTradeInfoAsync(string tradeId, JObject config, CancellationToken cancellationToken)
    {
        if (tradeId == TradeId)
        {
            return Task.FromResult(GetMarketTradeResult());
        }

        return Task.FromResult<MarketTradeResult>(null);
    }

    public Task<AssetQuoteResult> GetQuoteForAssetAsync(string fromAsset, string toAsset, JObject config, CancellationToken cancellationToken)
    {
        if (fromAsset.Equals(TradeFromAsset) && toAsset.Equals(TradeToAsset))
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
        DateTimeOffset createdTime = new DateTimeOffset(2021, 9, 1, 6, 45, 0, new TimeSpan(-7, 0, 0));
        var r = new WithdrawResult(WithdrawalPaymentMethod, WithdrawalAsset, ledgerEntries, WithdrawalId, WithdrawalStatus, createdTime, WithdrawalTargetAddress, WithdrawalTransactionId);
        return r;
    }

    private SimulateWithdrawalResult CreateWithdrawSimulationResult()
    {
        var ledgerEntries = new List<LedgerEntryData>();
        ledgerEntries.Add(new LedgerEntryData(WithdrawalAsset, WithdrawalAmount - WithdrawalFee, LedgerEntryData.LedgerEntryType.Withdrawal));
        ledgerEntries.Add(new LedgerEntryData(WithdrawalAsset, WithdrawalFee, LedgerEntryData.LedgerEntryType.Fee));
        var r = new SimulateWithdrawalResult(WithdrawalPaymentMethod, WithdrawalAsset, ledgerEntries, WithdrawalMinAmount, WithdrawalMaxAmount);
        return r;
    }

    public Task<WithdrawResult> WithdrawToStoreWalletAsync(string paymentMethod, decimal amount, JObject config, CancellationToken cancellationToken)
    {
        if (paymentMethod == WithdrawalPaymentMethod)
        {
            if (amount.ToString(CultureInfo.InvariantCulture).Equals("" + WithdrawalAmount, StringComparison.InvariantCulture) || WithdrawalAmountPercentage.Equals(amount))
            {
                return Task.FromResult(CreateWithdrawResult());
            }

            throw new InsufficientFundsException($"{Name} only supports withdrawals of {WithdrawalAmount} or {WithdrawalAmountPercentage}");
        }

        throw new CannotWithdrawException(this, paymentMethod, $"Only {WithdrawalPaymentMethod} can be withdrawn from {Name}");
    }

    public Task<SimulateWithdrawalResult> SimulateWithdrawalAsync(string paymentMethod, decimal amount, JObject config, CancellationToken cancellationToken)
    {
        if (paymentMethod == WithdrawalPaymentMethod)
        {
            if (amount == WithdrawalAmount)
            {
                return Task.FromResult(CreateWithdrawSimulationResult());
            }

            throw new InsufficientFundsException($"{Name} only supports withdrawals of {WithdrawalAmount}");
        }

        throw new CannotWithdrawException(this, paymentMethod, $"Only {WithdrawalPaymentMethod} can be withdrawn from {Name}");
    }

    public Task<WithdrawResult> GetWithdrawalInfoAsync(string paymentMethod, string withdrawalId, JObject config, CancellationToken cancellationToken)
    {
        if (withdrawalId == WithdrawalId && WithdrawalPaymentMethod.Equals(paymentMethod))
        {
            return Task.FromResult(CreateWithdrawResult());
        }

        return Task.FromResult<WithdrawResult>(null);
    }

    public string[] GetWithdrawablePaymentMethods()
    {
        return GetDepositablePaymentMethods();
    }
}
