#if DEBUG
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Custodians;
using BTCPayServer.Abstractions.Custodians.Client;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Custodian.Client;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.FakeCustodian;

public class FakeCustodian : ICustodian, ICanDeposit, ICanWithdraw, ICanTrade
{
    private const string TargetAddress = "3AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string ValidWithdrawalId = "FAKE_WITHDRAWAL_ID";
    private static readonly decimal _validWithdrawalAmount = new(0.05);
    private static readonly decimal _btcWithdrawalFee = new(0.001);
    private const string ValidWithdrawalPaymentMethod = "BTC-OnChain";
    private const string TransactionId = "FAKE_TRANSACTION_ID";
    private const string ValidAsset = "BTC";
    private const string ValidPaymentMethod = "BTC-OnChain";
    private const string ValidTradeId = "TRADE-ID-001";
    private const string TradeFromAsset = "EUR";
    private const string TradeToAsset = "BTC";
    private static readonly decimal _tradeQtyBought = new(1);
    private static readonly decimal _tradeFeeEuro = new(12.5);
    private static readonly decimal _btcPriceInEuro = new(30000);
    private readonly CustodianAccountRepository _custodianAccountRepository;

    public FakeCustodian(CustodianAccountRepository custodianAccountRepository, Client.BTCPayServerClient client)
    {
        _custodianAccountRepository = custodianAccountRepository;
    }

    public string Code
    {
        get => "fake";
    }

    public string Name
    {
        get => "Fake Exchange";
    }

    public Task<Dictionary<string, decimal>> GetAssetBalancesAsync(JObject config, CancellationToken cancellationToken)
    {
        var fakeConfig = ParseConfig(config);
        var r = new Dictionary<string, decimal>() { { "BTC", fakeConfig.BTCBalance }, { "LTC", fakeConfig.LTCBalance }, { "USD", fakeConfig.USDBalance }, { "EUR", fakeConfig.EURBalance } };
        return Task.FromResult(r);
    }

    public Task<Form> GetConfigForm(JObject config, CancellationToken cancellationToken = default)
    {
        var form = new Form();

        var generalFieldset = Field.CreateFieldset();
        generalFieldset.Label = "General";
        // TODO we cannot validate the custodian account ID because we have no access to the correct value. This is fine given this is a development tool and won't be needed by actual custodians.
        var accountIdField = Field.Create("Custodian Account ID", "CustodianAccountId", null, false,
            "Enter the ID of this custodian account. This is needed as a workaround which only applies to the Fake Custodian. Fill out correctly to make trading and withdrawing work.");
        generalFieldset.Fields.Add(accountIdField);

        // TODO we cannot validate the store ID because we have no access to the correct value. This is fine given this is a development tool and won't be needed by actual custodians.
        var storeIdField = Field.Create("Store ID", "StoreId", null, true,
            "Enter the ID of this store. This is needed as a workaround which only applies to the Fake Custodian. Fill out correctly to make trading and withdrawing work.");
        generalFieldset.Fields.Add(storeIdField);
        form.Fields.Add(generalFieldset);

        var balancesFieldset = Field.CreateFieldset();

        // Maybe a decimal type field would be better?
        var fakeBTCBalance = Field.Create("BTC Balance", "BTCBalance", null, true,
            "Enter the amount of BTC you want to have.");
        var fakeLTCBalance = Field.Create("LTC Balance", "LTCBalance", null, true,
            "Enter the amount of LTC you want to have.");
        var fakeEURBalance = Field.Create("EUR Balance", "EURBalance", null, true,
            "Enter the amount of EUR you want to have.");
        var fakeUSDBalance = Field.Create("USD Balance", "USDBalance", null, true,
            "Enter the amount of USD you want to have.");

        balancesFieldset.Label = "Fake balances";
        balancesFieldset.Fields.Add(fakeBTCBalance);
        balancesFieldset.Fields.Add(fakeLTCBalance);
        balancesFieldset.Fields.Add(fakeEURBalance);
        balancesFieldset.Fields.Add(fakeUSDBalance);
        form.Fields.Add(balancesFieldset);

        return Task.FromResult(form);
    }

    private FakeCustodianConfig ParseConfig(JObject config)
    {
        return config?.ToObject<FakeCustodianConfig>() ?? throw new InvalidOperationException("Invalid config");
    }

    public Task<DepositAddressData> GetDepositAddressAsync(string paymentMethod, JObject config, CancellationToken cancellationToken)
    {
        if (paymentMethod.Equals(ValidPaymentMethod))
        {
            DepositAddressData r = new() { Address = "3XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX" };
            return Task.FromResult(r);
        }

        return null;
    }

    public string[] GetDepositablePaymentMethods()
    {
        return new[] { ValidPaymentMethod };
    }

    public async Task<WithdrawResult> WithdrawToStoreWalletAsync(string paymentMethod, decimal amount, JObject config, CancellationToken cancellationToken)
    {
        // TODO Store fake withdrawals in the DB so we can have a history of withdrawals
        if (ValidWithdrawalPaymentMethod.Equals(paymentMethod))
        {
            LedgerEntryData ledgerEntryWithdrawal = new(ValidAsset, -amount, LedgerEntryData.LedgerEntryType.Withdrawal);
            LedgerEntryData ledgerEntryFee = new(ValidAsset, -_btcWithdrawalFee, LedgerEntryData.LedgerEntryType.Fee);
            List<LedgerEntryData> ledgerEntries = new();
            ledgerEntries.Add(ledgerEntryWithdrawal);
            ledgerEntries.Add(ledgerEntryFee);

            var fakeConfig = ParseConfig(config);
            if (amount <= fakeConfig.BTCBalance)
            {
                fakeConfig.BTCBalance -= amount;

                if (_btcWithdrawalFee <= fakeConfig.BTCBalance)
                {
                    fakeConfig.BTCBalance -= _btcWithdrawalFee;

                    var custodianAccount = await _custodianAccountRepository.FindById(fakeConfig.StoreId, fakeConfig.CustodianAccountId);

                    if (custodianAccount == null)
                    {
                        // We could not load the custodian account using the config settings, so they are bad and should be reported to the user so he can fix them.
                        throw new BadConfigException(new[] { "StoreId", "CustodianAccountId" });
                    }

                    var newConfig = JObject.FromObject(fakeConfig);
                    custodianAccount.SetBlob(newConfig);
                    await _custodianAccountRepository.CreateOrUpdate(custodianAccount);

                    var r = new WithdrawResult(paymentMethod, ValidAsset, ledgerEntries, ValidWithdrawalId, WithdrawalResponseData.WithdrawalStatus.Queued, DateTimeOffset.Now, TargetAddress, TransactionId);
                    return r;
                }
                CustodianApiException e3 = new(400, "insufficient-funds", "Cannot withdraw " + amount + " " + ValidAsset + " because you don't have enough to pay for fees");
                throw new CannotWithdrawException(this, paymentMethod, TargetAddress, e3);
            }

            CustodianApiException e1 = new(400, "insufficient-funds", "Cannot withdraw " + amount + " " + ValidAsset + " because you only hold " + fakeConfig.BTCBalance + " " + ValidAsset);
            throw new CannotWithdrawException(this, paymentMethod, TargetAddress, e1);
        }

        CustodianApiException e2 = new(400, "only-btc-supported", "The Fake Custodian can only withdraw using payment method " + ValidWithdrawalPaymentMethod);
        throw new CannotWithdrawException(this, paymentMethod, TargetAddress, e2);
    }

    public Task<SimulateWithdrawalResult> SimulateWithdrawalAsync(string paymentMethod, decimal qty, JObject config, CancellationToken cancellationToken)
    {
        if (ValidWithdrawalPaymentMethod.Equals(paymentMethod))
        {
            LedgerEntryData ledgerEntryWithdrawal = new(ValidAsset, -qty, LedgerEntryData.LedgerEntryType.Withdrawal);
            LedgerEntryData ledgerEntryFee = new(ValidAsset, new decimal(-0.001), LedgerEntryData.LedgerEntryType.Fee);
            List<LedgerEntryData> ledgerEntries = new();
            ledgerEntries.Add(ledgerEntryWithdrawal);
            ledgerEntries.Add(ledgerEntryFee);

            var fakeConfig = ParseConfig(config);
            var r = new SimulateWithdrawalResult(paymentMethod, ValidAsset, ledgerEntries, new decimal(0.001), fakeConfig.BTCBalance);
            return Task.FromResult(r);
        }

        CustodianApiException e = new(400, "only-btc-onchain-supported", "The Fake Custodian can only withdraw using payment method " + ValidWithdrawalPaymentMethod);
        throw new CannotWithdrawException(this, paymentMethod, TargetAddress, e);
    }

    public Task<WithdrawResult> GetWithdrawalInfoAsync(string paymentMethod, string withdrawalId, JObject config, CancellationToken cancellationToken)
    {
        // TODO make this Fake Custodian smarter and store previous fake withdrawals in the DB
        if (ValidWithdrawalPaymentMethod.Equals(paymentMethod) && withdrawalId.Equals(ValidWithdrawalId))
        {
            LedgerEntryData ledgerEntryWithdrawal = new(ValidAsset, _validWithdrawalAmount, LedgerEntryData.LedgerEntryType.Withdrawal);
            LedgerEntryData ledgerEntryFee = new(ValidAsset, new decimal(0.001), LedgerEntryData.LedgerEntryType.Fee);
            List<LedgerEntryData> ledgerEntries = new();
            ledgerEntries.Add(ledgerEntryWithdrawal);
            ledgerEntries.Add(ledgerEntryFee);

            var r = new WithdrawResult(paymentMethod, ValidAsset, ledgerEntries, ValidWithdrawalId, WithdrawalResponseData.WithdrawalStatus.Queued, DateTimeOffset.Now, TargetAddress, TransactionId);
            return Task.FromResult(r);
        }

        CustodianApiException e = new(400, "withdrawal-not-found", "The Fake Custodian can only fetch withdrawal ID " + ValidWithdrawalId);
        throw new CannotWithdrawException(this, paymentMethod, TargetAddress, e);
    }

    public string[] GetWithdrawablePaymentMethods()
    {
        return new[] { ValidPaymentMethod };
    }


    public List<AssetPairData> GetTradableAssetPairs()
    {
        // We only support trading BTC -> EUR and EUR -> BTC
        var r = new List<AssetPairData>();
        r.Add(new AssetPairData(ValidAsset, "EUR", (decimal)0.0001));
        r.Add(new AssetPairData("EUR", ValidAsset, (decimal)5));
        return r;
    }


    public async Task<MarketTradeResult> TradeMarketAsync(string fromAsset, string toAsset, decimal qty, JObject config, CancellationToken cancellationToken)
    {
        // TODO store fake traded in the DB + Update the balances so this fake custodian behaves like the real thing.
        if ((fromAsset.Equals("EUR") && toAsset.Equals(ValidAsset)) || (fromAsset.Equals(ValidAsset) && toAsset.Equals("EUR")))
        {
            // We only support trading BTC -> EUR and EUR -> BTC

            var fakeConfig = ParseConfig(config);

            if (fromAsset.Equals("BTC") && qty > fakeConfig.BTCBalance)
            {
                throw new InsufficientFundsException($"Insufficient funds. You only have {fakeConfig.BTCBalance} to trade.");
            }

            if (fromAsset.Equals("EUR") && qty > fakeConfig.EURBalance)
            {
                throw new InsufficientFundsException($"Insufficient funds. You only have {fakeConfig.EURBalance} to trade.");
            }

            decimal rate;

            rate = getRate(fromAsset, toAsset);
            var qtyReceived = qty / rate;

            var ledgerEntries = new List<LedgerEntryData>();
            ledgerEntries.Add(new LedgerEntryData(fromAsset, -qty, LedgerEntryData.LedgerEntryType.Trade));
            ledgerEntries.Add(new LedgerEntryData(toAsset, qtyReceived, LedgerEntryData.LedgerEntryType.Trade));
            ledgerEntries.Add(new LedgerEntryData("EUR", -1 * _tradeFeeEuro, LedgerEntryData.LedgerEntryType.Fee));


            if (fromAsset.Equals("BTC"))
            {
                fakeConfig.BTCBalance -= qty;
            }

            if (fromAsset.Equals("EUR"))
            {
                fakeConfig.EURBalance -= qty;
            }

            if (toAsset.Equals("BTC"))
            {
                fakeConfig.BTCBalance += qtyReceived;
            }

            if (toAsset.Equals("EUR"))
            {
                fakeConfig.EURBalance += qtyReceived;
            }

            // Fees are always in EUR... for now...
            if (_tradeFeeEuro <= fakeConfig.EURBalance)
            {
                fakeConfig.EURBalance -= _tradeFeeEuro;
            }
            else
            {
                throw new InsufficientFundsException($"Insufficient funds. You don't have enough EUR to pay for fees.");
            }

            var custodianAccount = await _custodianAccountRepository.FindById(fakeConfig.StoreId, fakeConfig.CustodianAccountId);

            if (custodianAccount == null)
            {
                // We could not load the custodian account using the config settings, so they are bad and should be reported to the user so he can fix them.
                throw new BadConfigException(new[] { "StoreId", "CustodianAccountId" });
            }

            var newConfig = JObject.FromObject(fakeConfig);
            custodianAccount.SetBlob(newConfig);
            await _custodianAccountRepository.CreateOrUpdate(custodianAccount);


            return new MarketTradeResult(fromAsset, toAsset, ledgerEntries, ValidTradeId);
        }

        throw new WrongTradingPairException(fromAsset, toAsset);
    }

    private static decimal getRate(string fromAsset, string toAsset)
    {
        decimal rate;
        if (fromAsset.Equals("EUR") && toAsset.Equals(ValidAsset))
        {
            rate = _btcPriceInEuro;
        }
        else
        {
            rate = 1 / _btcPriceInEuro;
        }

        return rate;
    }

    public Task<MarketTradeResult> GetTradeInfoAsync(string tradeId, JObject config, CancellationToken cancellationToken)
    {
        // TODO load the transaction from the DB which contains previous fake trades
        if (tradeId == ValidTradeId)
        {
            var ledgerEntries = new List<LedgerEntryData>();
            ledgerEntries.Add(new LedgerEntryData(ValidAsset, _tradeQtyBought, LedgerEntryData.LedgerEntryType.Trade));
            ledgerEntries.Add(new LedgerEntryData("EUR", -1 * _tradeQtyBought * _btcPriceInEuro, LedgerEntryData.LedgerEntryType.Trade));
            ledgerEntries.Add(new LedgerEntryData("EUR", -1 * _tradeFeeEuro, LedgerEntryData.LedgerEntryType.Fee));
            var r = new MarketTradeResult(TradeFromAsset, TradeToAsset, ledgerEntries, ValidTradeId);

            return Task.FromResult(r);
        }

        return Task.FromResult<MarketTradeResult>(null);
    }

    public Task<AssetQuoteResult> GetQuoteForAssetAsync(string fromAsset, string toAsset, JObject config, CancellationToken cancellationToken)
    {
        // TODO use the current market price for a realistic price

        if ((fromAsset.Equals("EUR") && toAsset.Equals(ValidAsset)) || (fromAsset.Equals(ValidAsset) && toAsset.Equals("EUR")))
        {
            // We only support trading BTC -> EUR and EUR -> BTC
            decimal rate = getRate(fromAsset, toAsset);
            return Task.FromResult(new AssetQuoteResult(fromAsset, toAsset, rate, rate));
        }

        throw new WrongTradingPairException(fromAsset, toAsset);
    }
}

public class FakeCustodianConfig
{
    public string CustodianAccountId { get; set; }
    public string StoreId { get; set; }
    public decimal BTCBalance { get; set; }
    public decimal LTCBalance { get; set; }
    public decimal USDBalance { get; set; }
    public decimal EURBalance { get; set; }

    public FakeCustodianConfig()
    {
    }
}
#endif
