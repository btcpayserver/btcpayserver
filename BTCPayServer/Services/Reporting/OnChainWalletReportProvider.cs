#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Dapper;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Reporting;

public class OnChainWalletReportProvider : ReportProvider
{
    public OnChainWalletReportProvider(
        NBXplorerConnectionFactory NbxplorerConnectionFactory,
        StoreRepository storeRepository,
        InvoiceRepository invoiceRepository,
        PaymentMethodHandlerDictionary handlers,
        DisplayFormatter displayFormatter,
        WalletRepository walletRepository)
    {
        this.NbxplorerConnectionFactory = NbxplorerConnectionFactory;
        StoreRepository = storeRepository;
        InvoiceRepository = invoiceRepository;
        DisplayFormatter = displayFormatter;
        _handlers = handlers;
        WalletRepository = walletRepository;
    }

    private NBXplorerConnectionFactory NbxplorerConnectionFactory { get; }
    private StoreRepository StoreRepository { get; }
    public InvoiceRepository InvoiceRepository { get; }
    public DisplayFormatter DisplayFormatter { get; }
    private PaymentMethodHandlerDictionary _handlers;
    private WalletRepository WalletRepository { get; }
    public override string Name => "Wallets";

    ViewDefinition CreateViewDefinition()
    {
        return new()
        {
            Fields =
            {
                new("Date", "datetime"),
                new("Crypto", "string"),
                // For proper rendering of explorer links, Crypto should always be before tx_id
                new("TransactionId", "tx_id"),
                new("InvoiceId", "invoice_id"),
                new("Confirmed", "boolean"),
                new("BalanceChange", "amount"),
            },
            Charts =
            {
                new()
                {
                    Name = "Group by Crypto",
                    Totals = { "Crypto" },
                    Groups = { "Crypto", "Confirmed" },
                    Aggregates = { "BalanceChange" }
                }
            }
        };
    }

    public override bool IsAvailable()
    {
        return NbxplorerConnectionFactory.Available;
    }

    public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
    {
        queryContext.ViewDefinition = CreateViewDefinition();
        await using var conn = await NbxplorerConnectionFactory.OpenConnection();
        var store = await StoreRepository.FindStore(queryContext.StoreId);
        if (store is null)
            return;

        HashSet<string> cryptoCodes = new();
        var interval = DateTimeOffset.UtcNow - queryContext.From;
        foreach (var (pmi, settings) in store.GetPaymentMethodConfigs<DerivationSchemeSettings>(_handlers))
        {
            var network = ((IHasNetwork)_handlers[pmi]).Network;
            cryptoCodes.Add(network.CryptoCode);
            var walletId = new WalletId(store.Id, network.CryptoCode);
            var command = new CommandDefinition(
                commandText:
                "SELECT r.tx_id, r.seen_at, t.blk_id, t.blk_height, r.balance_change " +
                "FROM get_wallets_recent(@wallet_id, @code, @asset_id, @interval, NULL, NULL) r " +
                "JOIN txs t USING (code, tx_id) " +
                "ORDER BY r.seen_at",
                parameters: new
                {
                    asset_id = GetAssetId(network),
                    wallet_id = NBXplorer.Client.DBUtils.nbxv1_get_wallet_id(network.CryptoCode, settings.AccountDerivation.ToString()),
                    code = network.CryptoCode,
                    interval
                },
                cancellationToken: cancellation);

            var rows = await conn.QueryAsync(command);
            foreach (var r in rows)
            {
                var date = (DateTimeOffset)r.seen_at;
                if (date > queryContext.To)
                    continue;
                var values = queryContext.AddData();
                var balanceChange = Money.Satoshis((long)r.balance_change).ToDecimal(MoneyUnit.BTC);
                values.Add(date);
                values.Add(network.CryptoCode);
                values.Add((string)r.tx_id);
                values.Add(null);
                values.Add((long?)r.blk_height is not null);
                values.Add(new FormattedAmount(balanceChange, network.Divisibility).ToJObject());
            }

            var objects = await WalletRepository.GetWalletObjects(new GetWalletObjectsQuery
            {
                Ids = queryContext.Data.Select(d => (string)d[2]!).ToArray(),
                WalletId = walletId,
                Type = WalletObjectData.Types.Tx
            });
            foreach (var row in queryContext.Data)
            {
                if (!objects.TryGetValue(new WalletObjectId(walletId, WalletObjectData.Types.Tx, (string)row[2]!), out var txObject))
                    continue;
                var invoiceId = txObject.GetLinks().Where(t => t.type == WalletObjectData.Types.Invoice).Select(t => t.id).FirstOrDefault();
                row[3] = invoiceId;
                row.Add(RateBook.FromTxWalletObject(txObject));
            }
        }

        // The currencies appearing in this report are:
        // - The currently tracked rates of the store
        // - The rates that were tracked at the invoices level
        // - The rates that were tracked at the wallet level
        var trackedCurrencies = store.GetStoreBlob().GetTrackedCurrencies().ToHashSet();
        var rates = await InvoiceRepository.GetRatesOfInvoices(queryContext.Data.Select(r => r[3]).OfType<string>().ToHashSet());
        foreach (var book in rates.Select(r => r.Value))
        {
            book.AddCurrencies(trackedCurrencies);
        }
        foreach (var row in queryContext.Data)
        {
            var rateData = row[^1] as RateBook;
            rateData?.AddCurrencies(trackedCurrencies);
        }
        trackedCurrencies.ExceptWith(cryptoCodes);
        foreach (var trackedCurrency in trackedCurrencies)
        {
            // We don't use amount here. Rounding the rates is dangerous when the price of the
            // shitcoin is very low.
            queryContext.ViewDefinition.Fields.Add(new($"Rate ({trackedCurrency})", "number"));
        }

        foreach (var row in queryContext.Data)
        {
            var rateData = row[^1] as RateBook;
            row.RemoveAt(row.Count - 1);
            var invoiceId = row[3] as string;
            var cryptoCode = row[1] as string;
            if (cryptoCode is null)
                continue;
            rates.TryGetValue(invoiceId ?? "", out var r);
            r ??= new("", new());
            r.AddRates(rateData);
            foreach (var trackedCurrency in trackedCurrencies)
            {
                if (r.TryGetRate(new CurrencyPair(cryptoCode, trackedCurrency)) is decimal v)
                    row.Add(v);
                else
                    row.Add(null);
            }
        }
    }

    private string? GetAssetId(BTCPayNetwork network)
    {
        if (network is Plugins.Altcoins.ElementsBTCPayNetwork elNetwork)
            return elNetwork.IsNativeAsset ? "" : elNetwork.AssetId.ToString();
        return null;
    }
}
