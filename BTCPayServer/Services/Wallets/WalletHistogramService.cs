using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Invoices;
using Dapper;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Services.Wallets;

public class WalletHistogramService
{
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly NBXplorerConnectionFactory _connectionFactory;

    public WalletHistogramService(
        PaymentMethodHandlerDictionary handlers,
        NBXplorerConnectionFactory connectionFactory)
    {
        _handlers = handlers;
        _connectionFactory = connectionFactory;
    }

    public async Task<HistogramData> GetHistogram(StoreData store, WalletId walletId, HistogramType type)
    {
        // https://github.com/dgarage/NBXplorer/blob/master/docs/Postgres-Schema.md
        if (_connectionFactory.Available)
        {
            var derivationSettings = store.GetDerivationSchemeSettings(_handlers, walletId.CryptoCode);
            if (derivationSettings != null)
            {
                var network = _handlers.GetBitcoinHandler(walletId.CryptoCode);
                var wallet_id = derivationSettings.GetNBXWalletId(network.Network.NBitcoinNetwork);
                await using var conn = await _connectionFactory.OpenConnection();

                var code = walletId.CryptoCode;
                var to = DateTimeOffset.UtcNow;
                var (days, pointCount) = type switch
                {
                    HistogramType.Day => (1, 30),
                    HistogramType.Week => (7, 30),
                    HistogramType.Month => (30, 30),
                    HistogramType.YTD => (DateTimeOffset.Now.DayOfYear - 1, 30),
                    HistogramType.Year => (365, 30),
                    HistogramType.TwoYears => (730, 30),
                    _ => throw new ArgumentException($"HistogramType {type} does not exist.")
                };
                var from = to - TimeSpan.FromDays(days);
                var interval = TimeSpan.FromTicks((to - from).Ticks / pointCount);
                var balance = await conn.ExecuteScalarAsync<decimal>(
                    "SELECT to_btc(available_balance) FROM wallets_balances WHERE wallet_id=@wallet_id AND code=@code AND asset_id=''",
                    new { code, wallet_id });
                var rows = await conn.QueryAsync("SELECT date, to_btc(balance) balance FROM get_wallets_histogram(@wallet_id, @code, '', @from, @to, @interval)",
                    new { code, wallet_id, from, to, interval });
                var data = rows.AsList();
                var series = new List<decimal>(pointCount);
                var labels = new List<DateTimeOffset>(pointCount);
                for (int i = 0; i < data.Count; i++)
                {
                    var r = data[i];
                    series.Add((decimal)r.balance);
                    labels.Add((DateTimeOffset)r.date);
                }
                series[^1] = balance;
                return new HistogramData
                {
                    Series = series,
                    Labels = labels,
                    Balance = balance,
                    Type = type
                };
            }
        }

        return null;
    }
}
